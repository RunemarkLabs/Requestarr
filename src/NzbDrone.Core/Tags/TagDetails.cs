using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags
{
    public class TagDetails : ModelBase
    {
        public string Label { get; set; }

        // Usage by Requestarr providers (backends) is added when they exist.
        public bool InUse => false;
    }
}
