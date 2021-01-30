using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    public class Storage
    {
        public class Container
        {
            [XmlAttribute]
            public string Identifier { get; set; }

            public string FriendlyName { get; set; }

            public string LocalFileName { get; set; }

            public UInt64 MaximumSize { get; set; }
        }

        public Container[] Containers { get; set; }
    }
}
