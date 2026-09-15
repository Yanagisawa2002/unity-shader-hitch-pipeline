"""Adversarial tiny fixtures for evidence gates, not native or performance results."""
from copy import deepcopy
from pathlib import Path
import struct
import json
import tempfile
import unittest
import zlib

from pso_whole_task_evidence import (cell_failures, native_progressive_summary, png_dimensions,
    profiler_pair, checkpoint_summary, CHECKPOINTS, NATIVE_BACKEND, ORIGINAL_BACKEND, NATIVE_API, PHASES)
from pso_external_capture import sha


def cell(linux=False):
    return dict(version=1, contract='external-runtime-observation-v1',
        cell='linux-vulkan-v1' if linux else 'windows-d3d12-v1', unityVersion='6000.5.9f1',
        runtimePlatform='LinuxPlayer' if linux else 'WindowsPlayer', graphicsApi='Vulkan' if linux else 'Direct3D12',
        graphicsDeviceName='fixture GPU', graphicsDeviceVersion='fixture driver', graphicsDeviceId=1,
        graphicsDeviceVendorId=1, processorCount=208 if linux else 20, operatingSystem='fixture OS',
        processorType='fixture CPU', renderingThreadingMode='MultiThreaded', buildGuid='fixture build',
        buildInputSha256='a'*64, shaderSha256='b'*64, contentSha256='c'*64,
        backend=ORIGINAL_BACKEND, nativeApiContract='none', linuxCgroup='0::/' if linux else None,
        cpuMax='2500000 100000' if linux else None, cpusetEffective='0-207' if linux else None,
        memoryMax='96636764160' if linux else None, cpuStat='nr_throttled 0' if linux else None,
        nvidiaKernelVersion='fixture kernel driver' if linux else None, observationError=None,
        driverBytesAttested=False, calibratedLinuxCostReuseSupported=False)


def native():
    return dict(version=1, backend=NATIVE_BACKEND, apiContract=NATIVE_API, fixedCountPerSubmission=16,
        normalQuit=True, allLoadedCollectionsWarmed=True, error=None,
        phases=[dict(phase=p, loaded=True, warmed=True, expectedStates=2, nativeStates=2, collectionSha256='d'*64) for p in sorted(PHASES)],
        calls=[dict(phase='urp-loading',api='WarmUpProgressively',state='completed',count=16,frame=4,
            originalWarmupWindow=True,traceCacheMisses=False,startedSeconds=1,completedSeconds=1.1,
            completedBefore=0,completedAfter=2,shutdownFence=False)])


def audit(profiled):
    return dict(contentAccepted=True, failures=[], contentValidation=dict(screenshotsEnabled=False),
        command=dict(arguments=['player','-pso-output','on' if profiled else 'off'] + (['-pso-whole-task-profile'] if profiled else []),
                     sha256='e'*64,cacheCondition='retained unknown'),
        unityVersion='6000.5.9f1',buildGuid='fixture build',gpu='fixture GPU',wholeTaskCell=cell(),
        recorder={} if profiled else None,rawIdentities={'capture': 'f'*64},allCpuIntervals=dict(
            maximumMilliseconds=220 + profiled*20, excessAbove33333Milliseconds=186.667 + profiled*30,
            observerToFirstUpdateSeconds=3.5, lastUpdateToQuitCallbackSeconds=.1,observerSeconds=265+profiled))


def png(width=1, height=1, rows=b'\0\x20\x40\x60'):
    def chunk(kind, data): return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(rows))+chunk(b'IEND',b'')


class WholeTaskEvidenceTests(unittest.TestCase):
    def test_linux_quota_is_separate_from_visible_cpu_count_and_drift_fails(self):
        start=cell(True); end=deepcopy(start)
        self.assertEqual(cell_failures(start,end,'linux-vulkan-v1','fixture GPU'),[])
        end['cpuMax']='100000 100000'
        self.assertTrue(any('cpuMax' in x for x in cell_failures(start,end,'linux-vulkan-v1')))

    def test_linux_metadata_cannot_claim_driver_bytes_or_cost_reuse(self):
        for field in ('driverBytesAttested','calibratedLinuxCostReuseSupported'):
            value=cell(True); value[field]=True
            self.assertTrue(cell_failures(value,value,'linux-vulkan-v1'))
        value=cell(True); value['linuxCgroup']='0::/unrecognized/nested'
        self.assertTrue(cell_failures(value,value,'linux-vulkan-v1'))

    def test_missing_or_mislabeled_backend_device_or_content_fails(self):
        self.assertTrue(cell_failures(None,None,'linux-vulkan-v1'))
        for field,value in (('graphicsApi','Vulkan'),('backend','auto'),('nativeApiContract',NATIVE_API),('shaderSha256','0'*64)):
            data=cell(); data[field]=value
            self.assertTrue(cell_failures(data,data,'windows-d3d12-v1'))
        self.assertTrue(cell_failures(cell(),cell(),'windows-d3d12-v1','other GPU'))

    def test_actual_native_calls_required_and_wrong_api_or_count_rejected(self):
        self.assertTrue(native_progressive_summary(native(),16)['accepted'])
        for field,value in (('api','WarmUp'),('count',8),('originalWarmupWindow',False),('traceCacheMisses',True)):
            data=native(); data['calls'][0][field]=value
            self.assertFalse(native_progressive_summary(data,16)['accepted'])
        data=native(); data['calls']=[]
        self.assertFalse(native_progressive_summary(data,16)['accepted'])

    def test_native_partial_completion_and_overlap_fail_without_trimming(self):
        data=native(); data['phases'][0]['warmed']=False
        self.assertFalse(native_progressive_summary(data,16)['accepted'])
        data=native(); data['calls'].append(dict(data['calls'][0],frame=5,startedSeconds=1.05))
        result=native_progressive_summary(data,16)
        self.assertFalse(result['accepted']); self.assertEqual(result['actualApiCalls'],2)

    def test_png_checks_real_row_lengths_and_crc(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'fixture.png'; path.write_bytes(png())
            self.assertEqual(png_dimensions(path),(1,1))
            for invalid in (png()[:-2],png(1920,1080),png(rows=b'\x05\0\0\0'),png()[:-1]+b'x'):
                path.write_bytes(invalid)
                with self.assertRaises(ValueError): png_dimensions(path)

    def test_profiler_pair_retains_long_frame_differences_without_certification(self):
        result=profiler_pair(audit(False),audit(True))
        self.assertTrue(result['pairComparable']); self.assertFalse(result['lowOverheadCertified'])
        self.assertEqual(result['profiledMinusUnprofiled']['maximumMilliseconds'],20)
        self.assertEqual(result['profiledMinusUnprofiled']['observerSeconds'],1)

    def test_complete_pixel_files_are_not_visual_acceptance_and_stale_poses_fail(self):
        # Deliberately flat synthetic images: valid files and poses must never be
        # promoted automatically into "official scene rendered correctly".
        pixels=png(1920,1080,(b'\0'+b'\x20\x40\x60'*1920)*1080)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder); points=[]; renders=[]
            for frame,key in enumerate(sorted(CHECKPOINTS),1):
                scene=key.split('-',1)[0]; status='Warming' if key.endswith('warmup-1s') else 'Running'
                target=1 if status=='Warming' else int(key.rsplit('-',1)[1])
                path=root/(key+'.png'); path.write_bytes(pixels)
                point=dict(key=key,scene=scene,status=status,camera='original',timelineAsset='original',file=path.name,
                    sha256=sha(path),error=None,requestedFrame=frame,capturedFrame=frame,width=1920,height=1080,
                    targetTime=target,actualTime=target+.01,timelineDuration=100,maximumLatenessSeconds=.25,
                    position={'x':frame,'y':0,'z':0},rotation={'x':0,'y':0,'z':0,'w':1},written=True,withinTimeTolerance=True)
                points.append(point)
                renders.append(dict(frame=frame,stage=scene,status=status,camera='original',timelineAsset='original',
                    timelineTime=point['actualTime'],timelineDuration=100,position=point['position'],rotation=point['rotation']))
            document=dict(version=1,contract='original-route-checkpoints-v1',correctnessOnly=True,normalQuit=True,
                          expectedKeys=sorted(CHECKPOINTS),checkpoints=points)
            receipt=root/'render-checkpoints.json'; receipt.write_text(json.dumps(document))
            result=checkpoint_summary(root,dict(renders=renders))
            self.assertTrue(result['captureStructurallyAccepted']); self.assertFalse(result['visualAccepted'])
            renders[0]['position']={'x':-100,'y':0,'z':0}
            self.assertFalse(checkpoint_summary(root,dict(renders=renders))['captureStructurallyAccepted'])
            points[0]['targetTime']=0; receipt.write_text(json.dumps(document))
            self.assertTrue(any('fixed contract' in f for f in checkpoint_summary(root,dict(renders=renders))['failures']))

    def test_profiler_pair_rejects_workload_binary_device_and_capture_changes(self):
        for change in ('workers','binary','backend','screenshot','incomplete','missing-profiler'):
            on=audit(True)
            if change=='workers': on['command']['arguments'] += ['-job-worker-count','2']
            if change=='binary': on['command']['sha256']='1'*64
            if change=='backend': on['wholeTaskCell']['backend']=NATIVE_BACKEND
            if change=='screenshot': on['contentValidation']['screenshotsEnabled']=True
            if change=='incomplete': on['contentAccepted']=False
            if change=='missing-profiler': on['recorder']=None
            self.assertFalse(profiler_pair(audit(False),on)['pairComparable'],change)


if __name__=='__main__': unittest.main()
