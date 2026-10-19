using System.Collections.Generic;

namespace Requestarr.Http
{
    public class ApiInfoResource
    {
        public string Current { get; set; }
        public List<string> Deprecated { get; set; }
    }
}
