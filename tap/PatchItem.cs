using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace tap
{
    public class PatchItem
    {
        [XmlAttribute]
        public string PatchFile;
        [XmlAttribute]
        public string PatchFileTarget;
    }
}
