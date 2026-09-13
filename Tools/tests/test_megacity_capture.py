"""Small data-integrity fixtures, not native workload or timing evidence."""
import copy
import unittest
from pso_megacity_capture import EXPECTED_SCENES, movement, scene_payloads, sustained_population


class MegacityCaptureTests(unittest.TestCase):
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
