using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    public class Cameras : ConfigurableList<Cameras.Camera>
    {

        public class Camera : BaseType
        {
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
            public Users.Allowance Permissions;
        }

        [XmlArray("Cameras")]
        public Camera[] AllCameras => AllItems;


    }
}
