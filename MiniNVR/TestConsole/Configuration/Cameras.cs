using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    public class Cameras : ConfigurableList<Cameras.Camera>
    {

        public class Camera : BaseType, User.IAccessibleDevice
        {
            private string[] _groups;

            public class CredentialInfo
            {
                public string Username;
                public string Password;
            }

            [XmlAttribute]
            public string FriendlyName;
            [XmlAttribute]
            public string Endpoint;
            [XmlElement(IsNullable = false)]
            public CredentialInfo Credentials;
            [XmlElement("Storage")]
            public string StorageIdentifier;
            public string[] Groups
            {
                get
                {
                    return _groups;
                }
                set
                {
                    _groups = value;
                }
            }
        }

        [XmlArray("Cameras")]
        public Camera[] AllCameras {
            get {
                return AllItems;
            }
            set {
                AllItems = value;
            }
        }

    }
}
