using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestConsole.Streamer
{
    public class CameraManager
    {
        private List<Camera> cameras = new List<Camera>();

        public CameraManager()
        {
            Configuration.Database.Instance.Cameras.OnCameraUpdated += OnCamera;
            foreach (var camera in Configuration.Database.Instance.Cameras.AllCameras)
                AddCamera(camera);
        }

        private void AddCamera(Configuration.Cameras.Camera camera)
        {
            cameras.Add(new Camera(camera));
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
            foreach (var j in toRemove)
                cameras.RemoveAt(j);
        }

        public Camera GetCamera(string identifier)
        {
            foreach (var cam in cameras)
                if (cam.Identifier == identifier)
                    return cam;
            return null;
        }

        private void OnCamera(object sender, Configuration.Cameras.CameraEvent e) {
            if (e.AddedUpdated)
                AddCamera(e.ChangedCamera);
            else
                RemoveCamera(e.ChangedCamera);
        }
    }
}
