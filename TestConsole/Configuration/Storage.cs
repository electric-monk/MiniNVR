using System;
using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    public class Storage : ConfigurableList<Storage.Container>
    {
        public class Container : BaseType
        {
            public string FriendlyName { get; set; }

            public string LocalFileName { get; set; }

            public UInt64 MaximumSize { get; set; }
        }

        [XmlArray("Containers")]
        public Container[] AllContainers
        {
            get {
                return AllItems;
            }
            set {
                AllItems = value;
            }
        }
    }
}
