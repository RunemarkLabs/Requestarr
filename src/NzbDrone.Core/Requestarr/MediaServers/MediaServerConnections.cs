using System;
using System.Collections.Generic;

namespace Requestarr.Core.MediaServers
{
    // The media servers the admin has actually connected, one entry per kind (Plex, Jellyfin). Every media server
    // button (the welcome channel's, Link Plex on member panels) shows only for these. Connecting a media server
    // arrives with media server sharing; until then none is connected, and no media server button shows.
    public interface IMediaServerConnections
    {
        IReadOnlyList<MediaServerProvider> Connected();

        bool IsConnected(MediaServerProvider provider);
    }

    public class MediaServerConnections : IMediaServerConnections
    {
        public IReadOnlyList<MediaServerProvider> Connected() => Array.Empty<MediaServerProvider>();

        public bool IsConnected(MediaServerProvider provider) => ((IList<MediaServerProvider>)Connected()).Contains(provider);
    }
}
