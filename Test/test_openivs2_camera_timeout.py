"""仅检查真实事件生产者及 UI 接线，不代替实机或 GUI 验收。"""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[1]
class CameraTimeoutTests(unittest.TestCase):
    def test_adapter_subscribes_to_published_event(self):
        manager = (ROOT / 'CameraManager/CameraManager.cs').read_text(encoding='utf-8-sig')
        grab = manager.split('private void GrabProcess(', 1)[1].split('public void SetTriggerMode(', 1)[0]
        self.assertIn('ImageUpdated?.Invoke', grab)
        adapter = (ROOT / 'OpenIVS2/Services/OpenIvsCameraHandle.cs').read_text(encoding='utf-8-sig')
        self.assertIn('_camera.ImageUpdated +=', adapter)
        self.assertIn('_camera.ImageUpdated -=', adapter)
    def test_failure_reports_error_without_blocking_cleanup(self):
        source = (ROOT / 'OpenIVS2/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
        failure = source.split('private Task OnTriggerFailedAsync(', 1)[1].split('private static void ReleaseCycleObjects(', 1)[0]
        self.assertIn('QueueCycleError(failure)', failure)
        self.assertIn('ReleaseCycleObjects(failure.Results)', failure)
        prompt = source.split('private void QueueCycleError(', 1)[1].split('private static void ReleaseCycleObjects(', 1)[0]
        for required in ('Dispatcher.BeginInvoke', 'MessageBox.Show', 'MessageBoxImage.Error', '_closing', '_acceptanceMode', 'Interlocked.CompareExchange', 'finally', 'CameraId'):
            self.assertIn(required, prompt)
        self.assertNotIn('Shutdown()', prompt)
if __name__ == '__main__':
    unittest.main()
