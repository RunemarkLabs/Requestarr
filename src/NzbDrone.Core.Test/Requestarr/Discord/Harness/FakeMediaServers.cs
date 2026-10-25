using System.Collections.Generic;
using Requestarr.Core.MediaServers;

namespace Requestarr.Core.Test.Discord.Harness
{
    // The media servers the admin has connected, set by each test.
    public class FakeMediaServers : IMediaServerConnections
    {
        public List<MediaServerProvider> Servers { get; } = new();

        public IReadOnlyList<MediaServerProvider> Connected() => Servers;

        public bool IsConnected(MediaServerProvider provider) => Servers.Contains(provider);
    }
}
