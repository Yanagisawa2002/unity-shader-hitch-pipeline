"""Small data-integrity fixtures, not native workload or timing evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from pso_megacity_capture import EXPECTED_SCENES, MENU, MAIN, original_route_renders, engine_window, movement, scene_payloads, sustained_population, policy_execution_failures, installed_plan
from pso_boat_comparison import ENVIRONMENT_KEYS
from pso_external_capture import sha, statistics
from pso_megacity_comparison import aggregate_samples


class MegacityCaptureTests(unittest.TestCase):
    def test_persistent_original_camera_requires_explicit_active_route_and_readiness(self):
        row = dict(scene='', cameraSceneName='DontDestroyOnLoad', activeScene=MENU,
            originalHybridCamera=True, cameraType='Game', targetTexture=False,
            pixelWidth=1920, pixelHeight=1080, menuInitialized=True, hybridInitialized=False)
        self.assertEqual(original_route_renders([row], MENU), [row])
        self.assertEqual(original_route_renders([row], MAIN), [])
        main = dict(row, activeScene=MAIN, hybridInitialized=True)
        self.assertEqual(original_route_renders([main], MAIN), [main])
        for field, value in [('originalHybridCamera', False), ('targetTexture', True),
                             ('cameraType', 'Reflection'), ('pixelWidth', 0), ('menuInitialized', False)]:
            self.assertEqual(original_route_renders([dict(row, **{field: value})], MENU), [])
        self.assertEqual(original_route_renders([dict(main, hybridInitialized=False)], MAIN), [])
        # Camera ownership alone is not a route, even if it names the target scene.
        legacy = dict(row, scene=MENU); legacy.pop('activeScene')
        self.assertEqual(original_route_renders([legacy], MENU), [])

    def test_partial_content_failure_retains_available_tail_without_invented_windows(self):
        metrics = statistics([1,2,600])
        good = dict(policy='scheduled',accepted=True,elapsedSeconds=61,allUpdates=metrics,firstLoad=metrics,mainObservation=metrics)
        failed = dict(policy='scheduled',accepted=False,elapsedSeconds=481,allUpdates=metrics,firstLoad=None,mainObservation=None)
        rows = [good,failed]
        result = aggregate_samples(rows,[50])['scheduled']
        self.assertEqual((result['processes'],result['accepted'],result['updateAvailableProcesses']),(2,1,2))
        self.assertEqual(result['totalHitches']['50'],2)
        self.assertEqual(result['maximumUpdateMilliseconds'],600)
        self.assertEqual(result['windows']['mainObservation']['availableProcesses'],1)
        self.assertEqual(result['windows']['mainObservation']['p99'],600)
        empty = aggregate_samples([dict(failed,allUpdates=None)],[50])['scheduled']
        self.assertIsNone(empty['maximumUpdateMilliseconds'])
        self.assertIsNone(empty['totalHitches']['50'])
        self.assertIsNone(empty['windows']['firstLoad']['p99'])
        self.assertEqual(empty['windows']['firstLoad']['availableProcesses'],0)
        self.assertIsNone(rows[1]['mainObservation'])

    def test_actual_installed_plan_rejects_changed_collection_or_executable_bytes(self):
        with tempfile.TemporaryDirectory() as tmp:
            player = Path(tmp)/'Fixture.exe'; player.write_bytes(b'CPU artifact fixture')
            root = player.parent/'Fixture_Data/StreamingAssets/ShaderHitchPipeline'
            root.mkdir(parents=True)
            collection = root/'collection'; collection.write_bytes(b'CPU collection byte fixture, not native evidence')
            plan = dict(planSha256='fixture-content',phases=[dict(phase='megacity-process',collectionFile='collection',
                collectionSha256=sha(collection),graphicsStateCount=10)])
            (root/'plan.json').write_text(json.dumps(plan))
            command = dict(player=str(player),sha256=sha(player))
            self.assertEqual(installed_plan(command)[1]['collections'][0]['states'],10)
            collection.write_bytes(b'changed')
            with self.assertRaises(ValueError): installed_plan(command)
            player.write_bytes(b'changed')
            with self.assertRaises(ValueError): installed_plan(command)

    def test_content_and_requested_policy_cannot_replace_matching_native_execution(self):
        command = dict(policy='disabled', planBaselineForDisabled=True)
        run = dict(error='', phases=[], cacheMissTrace=dict(armed=True,error=''),
                   strategy='scheduled', planSha256='same-plan')
        scheduler = dict(hasInFlightBatch=False, hasPendingRetirement=False, activations=[],
                         policy='scheduled', planSha256='same-plan')
        environment = {key: 'stable-'+key for key in ENVIRONMENT_KEYS}
        run['environment'] = environment
        capture = dict(buildGuid=environment['buildGuid'],environment=environment)
        plan = dict(phases=[dict(phase='megacity-process',graphicsStateCount=10,prewarmAtStartup=False)])
        def check(cmd=command, warm=run, schedule=scheduler, observed=capture, installed=plan, digest='same-plan'):
            return policy_execution_failures(cmd,[dict(data=warm)],[dict(data=schedule)],observed,installed,digest)
        self.assertEqual(check(),[])
        self.assertTrue(policy_execution_failures(command,[],[],capture,plan,'same-plan'))
        self.assertTrue(check(cmd=dict(command,policy='scheduled')))
        self.assertTrue(check(schedule=dict(scheduler,policy='throughput')))
        self.assertTrue(check(schedule=dict(scheduler,planSha256='different-plan')))
        self.assertTrue(check(schedule=dict(scheduler,hasPendingRetirement=True)))
        self.assertTrue(check(observed=dict(capture,buildGuid='other-player')))
        self.assertTrue(check(observed=dict(capture,environment=dict(environment,driverIdentity='other-driver'))))
        self.assertTrue(check(digest='other-installed-plan'))
        self.assertTrue(check(installed=dict(phases=[dict(plan['phases'][0],prewarmAtStartup=True)])))
        phase = dict(phase='megacity-process',error='',totalGraphicsStates=10,completedGraphicsStates=10,
            backendReportedWarmedUp=True,batchCount=1,strategy='throughput',backendSchedulingMode='native-async-throughput')
        activation = dict(phase='megacity-process',state='Completed',failure='',ownerReleased=True,hasInFlightBatch=False,
            backendReportedWarmedUp=True,completedPermutations=9,totalGraphicsStates=10)
        active_run = dict(run,strategy='throughput',phases=[phase])
        active_scheduler = dict(scheduler,policy='throughput',activations=[activation])
        # Real warmup permutations and graphics states are distinct units; 9 vs
        # 10 is valid only with the backend completion/owner/count evidence.
        self.assertEqual(check(cmd=dict(command,policy='all-at-once'),warm=active_run,schedule=active_scheduler),[])
        self.assertTrue(check(cmd=dict(command,policy='all-at-once'),warm=active_run,
            schedule=dict(active_scheduler,activations=[dict(activation,totalGraphicsStates=9)])))
        self.assertTrue(check(cmd=dict(command,policy='all-at-once'),schedule=active_scheduler,
            warm=dict(active_run,phases=[dict(phase,backendSchedulingMode='progressive-batches')])))

    def test_native_window_uses_direct_engine_clock_not_stopwatch_offset(self):
        start = dict(frame=100, seconds=5.0, engineRealtimeSeconds=500.0)
        end = dict(frame=1000, seconds=65.0, engineRealtimeSeconds=560.25)
        self.assertEqual(engine_window(start,end),(500.0,560.25))
        with self.assertRaises(ValueError): engine_window(start,dict(end,engineRealtimeSeconds=499))
        with self.assertRaises(ValueError): engine_window(start,dict(end,frame=100))
        with self.assertRaises(KeyError): engine_window(start,dict(frame=1000,seconds=65))

    def test_scene_labels_and_loaded_flags_do_not_replace_payloads(self):
        section = dict(guid='original', requested=True, loaded=True, fileBytes=100, payloadEntities=2, renderEntities=1)
        snapshot = dict(scenes=[dict(guid='original', requested=True, loaded=True, sections=[section])])
        self.assertIn('original', scene_payloads(snapshot))
        for field, value in [('payloadEntities', 0), ('fileBytes', 0), ('requested', False), ('loaded', False), ('guid', 'other')]:
            changed = copy.deepcopy(snapshot)
            changed['scenes'][0]['sections'][0][field] = value
            self.assertEqual(scene_payloads(changed), {})
        changed = copy.deepcopy(snapshot)
        changed['scenes'][0]['sections'].append(dict(section, loaded=False))
        self.assertEqual(scene_payloads(changed), {})

    def test_motion_requires_same_live_entity_and_native_simulation_state(self):
        rows = [dict(worldSequence=1, realtimeSeconds=t, simulationSeconds=t, samples=[dict(
            kind='traffic', entity=3, version=1, position=dict(x=t,y=0,z=0), roadIndex=0, splinePosition=t)]) for t in (0, 30)]
        self.assertTrue(movement(rows, 'traffic')[0]['positiveMovement'])
        for identity, value in [('entity', 4), ('version', 2)]:
            changed = copy.deepcopy(rows); changed[-1]['samples'][0][identity] = value
            self.assertFalse(any(r['positiveMovement'] for r in movement(changed, 'traffic')))
        changed = copy.deepcopy(rows); changed[-1]['worldSequence'] = 2
        self.assertFalse(any(r['positiveMovement'] for r in movement(changed, 'traffic')))
        changed = copy.deepcopy(rows); changed[-1]['simulationSeconds'] = 0
        self.assertFalse(movement(changed, 'traffic')[0]['positiveMovement'])
        changed = copy.deepcopy(rows); changed[-1]['samples'][0]['splinePosition'] = 0
        self.assertFalse(movement(changed, 'traffic')[0]['positiveMovement'])

    def test_initial_readiness_cannot_cover_later_scene_disappearance_or_freeze(self):
        def snapshot(t):
            return dict(worldSequence=1,realtimeSeconds=t,simulationSeconds=t,gameLoadInfoPresent=True,
                requestedSections=6,loadedSections=6,singlePlayers=1,vehicles=100,blimps=2,renderEntities=1000,
                scenes=[dict(guid=g,requested=True,loaded=True,sections=[dict(guid=g,requested=True,loaded=True,
                    fileBytes=100,payloadEntities=10,renderEntities=1)]) for g in EXPECTED_SCENES])
        rows = [snapshot(t) for t in range(29,61)]
        before = snapshot(28); before['scenes'] = []
        self.assertTrue(sustained_population([before]+rows,30,60)[0]['accepted'])
        changed = copy.deepcopy(rows); changed[5]['scenes'].pop()
        self.assertFalse(sustained_population(changed,30,60)[0]['accepted'])
        changed = copy.deepcopy(rows); changed[-1]['simulationSeconds']=changed[-2]['simulationSeconds']
        self.assertFalse(sustained_population(changed,30,60)[0]['accepted'])
        self.assertFalse(sustained_population(rows[:3],30,60)[0]['accepted'])


if __name__ == '__main__': unittest.main()
