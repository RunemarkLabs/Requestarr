using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Discord
{
    // Images Requestarr's messages show, built into Requestarr and sent with the message as attachments:
    // an embed points at "attachment://name.png" and the gateway attaches that file. Nothing is loaded
    // from GitHub or any other host, so a self-hosted install doesn't depend on it.
    public static class DiscordAssets
    {
        public const string Scheme = "attachment://";
        public const string TmdbLogo = "tmdb-logo.png";
        public const string FeedCardWidth = "feed-card-width.png";

        private const string ResourcePrefix = "Requestarr.Assets.";

        public static byte[] Bytes(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + name);

            if (stream == null)
            {
                return null;
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        // The files a message's embeds point to (each once).
        public static IReadOnlyList<(string Name, byte[] Bytes)> For(InteractionReply reply) =>
            (reply?.Embeds ?? new List<InteractionEmbed>())
                .SelectMany(e => new[] { e.ImageUrl, e.ThumbnailUrl, e.FooterIconUrl })
                .Where(u => u != null && u.StartsWith(Scheme, StringComparison.Ordinal))
                .Select(u => u.Substring(Scheme.Length))
                .Distinct()
                .Select(n => (n, Bytes(n)))
                .Where(f => f.Item2 != null)
                .ToList();
    }
}
