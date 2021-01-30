using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsole.Configuration
{
    public class Cameras
    {
        private ReaderWriterLockSlim locker = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        public class Camera : IEquatable<Camera>
        {
            public class CredentialInfo
            {
                public string Username;
                public string Password;
            }

            [XmlAttribute]
            public string FriendlyName;
            [XmlAttribute]
            public string Identifier;
            [XmlAttribute]
            public string Endpoint;
            [XmlElement(IsNullable = false)]
            public CredentialInfo Credentials;
            public bool ShouldRecord;
            public Users.Allowance Permissions;

            public bool Equals(Camera other)
            {
                return Identifier == other.Identifier;
            }
        }

        private List<Camera> cameras = new List<Camera>();

        [XmlArray("Cameras")]
        public Camera[] AllCameras
        {
            get {
                locker.EnterReadLock();
                try {
                    return cameras.ToArray();
                }
                finally {
                    locker.ExitReadLock();
                }
            }
            set {
                locker.EnterWriteLock();
                try {
                    cameras = new List<Camera>(value);
                }
                finally {
                    locker.ExitWriteLock();
                }
            }
        }

        public void AddCamera(Camera camera)
        {
            locker.EnterWriteLock();
            try {
                var matches = cameras.IndexOf(camera);
                if (matches == -1)
                    cameras.Add(camera);
                else
                    cameras[matches] = camera;
                AsyncUpdateCamera(camera, true);
            }
            finally {
                locker.ExitWriteLock();
            }
        }

        public void RemoveCamera(string identifier)
        {
            Camera found = null;
            locker.EnterWriteLock();
            try {
                var matches = cameras.IndexOf(new Camera { Identifier = identifier });
                if (matches != -1) {
                    found = cameras[matches];
                    cameras.RemoveAt(matches);
                }
                if (found != null)
                    AsyncUpdateCamera(found, false);
            }
            finally {
                locker.ExitWriteLock();
            }
        }

        public class CameraEvent : EventArgs
        {
            public Camera ChangedCamera { get; set; }
            public bool AddedUpdated { get; set; }
        }

        private void AsyncUpdateCamera(Camera camera, bool addedUpdated)
        {
            CameraEvent ev = new CameraEvent { ChangedCamera = camera, AddedUpdated = addedUpdated };
            OnCameraUpdated?.Invoke(this, ev);
        }

        [XmlIgnore]
        public EventHandler<CameraEvent> OnCameraUpdated;
    }
}
