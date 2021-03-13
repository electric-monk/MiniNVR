using System.Collections.Generic;

namespace TestConsole.Streamer
{
    public class CameraManager
    {
        private readonly StorageManager storage;
        private List<Camera> cameras = new List<Camera>();
        private readonly Dictionary<string, Recorder.DataFile> recordings = new Dictionary<string, Recorder.DataFile>();

        public CameraManager(StorageManager storage)
        {
            this.storage = storage;

            Configuration.Database.Instance.Cameras.OnUpdated += OnCamera;
            foreach (var camera in Configuration.Database.Instance.Cameras.AllCameras)
                AddCamera(camera);
        }

        private void AddCamera(Configuration.Cameras.Camera camera)
        {
            Camera solidCamera = new Camera(camera);
            cameras.Add(solidCamera);
            if (camera.StorageIdentifier != null) {
                Recorder.DataFile dataFile = storage.GetStorage(camera.StorageIdentifier);
                if (dataFile != null) {
                    recordings.Add(camera.Identifier, dataFile);
                    solidCamera.OnFrames += RecordFrames;
                }
            }
        }

        private void RemoveCamera(Configuration.Cameras.Camera camera)
        {
            List<int> toRemove = new List<int>();
            int i = 0;
            foreach (var cam in cameras) {
                if (camera.Identifier == cam.Identifier) {
                    toRemove.Add(i);
                    cam.Stop();
                }
                i++;
            }
            toRemove.Reverse();
            foreach (var j in toRemove) {
                if (recordings.ContainsKey(cameras[j].Identifier)) {
                    cameras[j].OnFrames -= RecordFrames;
                    recordings.Remove(cameras[j].Identifier);
                }
                cameras.RemoveAt(j);
            }
        }

        private void RecordFrames(object sender, Utils.StreamWatcher.FrameSetEvent frames)
        {
            Camera camera = (Camera)sender;
            string camId = camera.Identifier;
            recordings[camId].FinishFrameSet(camId, frames);
        }

        public Camera GetCamera(string identifier)
        {
            foreach (var cam in cameras)
                if (cam.Identifier == identifier)
                    return cam;
            return null;
        }

        private void OnCamera(object sender, Configuration.ConfigurableList<Configuration.Cameras.Camera>.UpdateEvent e) {
            if (e.AddedUpdated)
                AddCamera(e.ChangedItem);
            else
                RemoveCamera(e.ChangedItem);
        }
    }
}
