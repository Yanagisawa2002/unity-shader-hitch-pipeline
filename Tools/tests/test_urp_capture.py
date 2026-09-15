"""Tiny parser fixtures, not external workload data, native evidence or timings."""
import unittest
from pso_urp_capture import ROUTES, completed_timeline, main_route_renders, parse_native_csv


class UrpCaptureTests(unittest.TestCase):
    def test_real_completion_is_separate_from_time_based_render_sampling(self):
        events=[dict(kind=kind,seconds=seconds,asset='original',duration=85,wrapMode='None',status='Running',state='Paused')
                for kind,seconds in [('stage-CockpitScene-Running',10),('stopped',96),('stage-CockpitScene-Finished',96.1)]]
        self.assertTrue(completed_timeline(events,'CockpitScene','original',85))
        self.assertFalse(completed_timeline(events[:1]+events[2:],'CockpitScene','original',85))
        self.assertFalse(completed_timeline(events,'CockpitScene','different',85))
        events[1]['seconds']=9
        self.assertFalse(completed_timeline(events,'CockpitScene','original',85))

    def test_original_first_load_camera_is_retained_without_duplicating_native_run(self):
        rows=[dict(cameraId=1,status='Warming',frame=1),dict(cameraId=2,status='Warming',frame=1),
              dict(cameraId=1,status='Running',frame=2)]
        main,other=main_route_renders(rows)
        self.assertEqual(main,[rows[0],rows[2]])
        self.assertEqual(other,[rows[1]])
        with self.assertRaises(ValueError): main_route_renders(rows+[dict(cameraId=2,status='Running',frame=2)])

    def fixture(self):
        return 'URP Template Performance Test\n'+''.join(
            f'\nScene,{name}\n\nCaptured frames,2\n,Frame Time,FPS,CPU,Render,GPU\n0,10,100,10,10,10\n1,20,50,20,20,20\n'
            for name,_ in ROUTES)

    def test_all_original_csv_routes_and_positive_samples_are_required(self):
        self.assertEqual([r['count'] for r in parse_native_csv(self.fixture())],[2]*4)
        for text in (self.fixture().replace('Scene,GardenScene','Scene,TerminalScene'),
                     self.fixture().replace('0,10,100,10,10,10','0,0,100,10,10,10'),
                     self.fixture().replace('1,20,50,20,20,20','nan,20,50,20,20,20'),
                     self.fixture().split('\nScene,CockpitScene')[0]):
            with self.assertRaises(ValueError): parse_native_csv(text)


if __name__=='__main__': unittest.main()
