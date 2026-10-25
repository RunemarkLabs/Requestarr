using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Announcements
{
    // Who a version card is for: the admin channel's cards count both sections, the announcements channel's only the
    // users' lines.
    public enum VersionAudience
    {
        Admins,
        Users
    }

    public static class VersionCardRoutes
    {
        public const string ReadChanges = "whatsnew";   // whatsnew:{a|u}:{version}
    }

    // One version's card as posted: its title and description are what a server compares to find and update it.
    public record VersionCard(Version Version, string Title, string Description, InteractionReply Reply);

    // What's new, as cards. In the channels each version is one line with a Read changes button; pressing it shows the
    // full card, only to whoever pressed it: the version and its date as the title, then "For users" and "For admins"
    // (only those with something in them), one line per change.
    public static class VersionCards
    {
        // Discord's limits: 4,096 characters in an embed's text, 6,000 in all of a message's embeds, ten embeds.
        public const int EmbedTextLength = 4096;
        public const int MessageEmbedsLength = 6000;
        public const int EmbedsPerMessage = 10;

        public const uint UsersColor = 0x2BB3A3;
        public const uint AdminsColor = 0xE0A030;

        private static readonly Regex AdminTitle = new(@"^Requestarr (?<version>\d+\.\d+\.\d+) · \d+ changes? for (admins|users)");
        private static readonly Regex UsersTitle = new(@"^Requestarr (?<version>\d+\.\d+\.\d+) · \d+ new things? for you");
        private static readonly Regex BoldLead = new(@"^\*\*(?<lead>[^*]+?):?\*\*");

        public static VersionCard Collapsed(ChangelogVersion version, VersionAudience audience)
        {
            var title = audience == VersionAudience.Admins
                ? DiscordText.VersionCardAdminTitle(version.Version, version.Admins.Count, version.Users.Count)
                : DiscordText.VersionCardUsersTitle(version.Version, version.Users.Count);

            // Members see the main change in a few words, so they know whether to open it.
            var description = audience == VersionAudience.Users ? MainChange(version.Users[0]) : null;

            var reply = new InteractionReply
            {
                Embeds = new List<InteractionEmbed> { new() { Title = title, Description = description, Color = audience == VersionAudience.Admins ? AdminsColor : UsersColor } },
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{VersionCardRoutes.ReadChanges}:{(audience == VersionAudience.Admins ? "a" : "u")}:{version.Version}", Label = DiscordText.ReadChangesLabel, Style = ButtonStyle.Secondary }
                }
            };

            return new VersionCard(version.Version, title, description, reply);
        }

        // The version a card's title is for, or null when the title isn't one of this audience's cards.
        public static Version Parse(string title, VersionAudience audience)
        {
            var match = (audience == VersionAudience.Admins ? AdminTitle : UsersTitle).Match(title ?? string.Empty);
            return match.Success ? Version.Parse(match.Groups["version"].Value) : null;
        }

        // The full card, private: one embed per section (a long section carries on in more embeds, split between
        // lines), as many messages as Discord's limits need.
        public static IReadOnlyList<InteractionReply> Full(ChangelogVersion version, VersionAudience audience)
        {
            var sections = new List<(string Heading, IReadOnlyList<string> Lines, uint Color)>();

            if (version.Users.Count > 0)
            {
                sections.Add((Changelog.UsersHeading, version.Users, UsersColor));
            }

            if (audience == VersionAudience.Admins && version.Admins.Count > 0)
            {
                sections.Add((Changelog.AdminsHeading, version.Admins, AdminsColor));
            }

            var embeds = new List<InteractionEmbed>();

            foreach (var (heading, lines, color) in sections)
            {
                var first = true;

                foreach (var chunk in Chunks(lines.Select(l => DiscordLimits.Truncate($"• {l}", EmbedTextLength - 100)), EmbedTextLength - 100))
                {
                    var header = $"**{heading}**{(first ? string.Empty : DiscordText.VersionNotesContinued)}";
                    embeds.Add(new InteractionEmbed { Description = header + "\n" + chunk, Color = color });
                    first = false;
                }
            }

            embeds[0] = new InteractionEmbed { Title = DiscordText.VersionCardFullTitle(version.Version, version.Date), Description = embeds[0].Description, Color = embeds[0].Color };

            var replies = new List<InteractionReply>();
            var current = new List<InteractionEmbed>();
            var length = 0;

            foreach (var embed in embeds)
            {
                var size = (embed.Title?.Length ?? 0) + embed.Description.Length;

                if (current.Count > 0 && (current.Count == EmbedsPerMessage || length + size > MessageEmbedsLength))
                {
                    replies.Add(new InteractionReply { Embeds = current, Ephemeral = true });
                    current = new List<InteractionEmbed>();
                    length = 0;
                }

                current.Add(embed);
                length += size;
            }

            replies.Add(new InteractionReply { Embeds = current, Ephemeral = true });
            return replies;
        }

        // A change's bold lead ("**Long TV shows:** you can now…" is "Long TV shows"), or its first few words.
        public static string MainChange(string line)
        {
            var match = BoldLead.Match(line);

            if (match.Success)
            {
                return match.Groups["lead"].Value.Trim().TrimEnd(':');
            }

            var words = line.Replace("*", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length <= 8 ? string.Join(" ", words) : string.Join(" ", words.Take(8)) + "…";
        }

        // Whole lines, joined, each chunk within `max` characters.
        private static IEnumerable<string> Chunks(IEnumerable<string> lines, int max)
        {
            var chunk = new List<string>();
            var length = 0;

            foreach (var line in lines)
            {
                if (chunk.Count > 0 && length + line.Length + 1 > max)
                {
                    yield return string.Join("\n", chunk);
                    chunk = new List<string>();
                    length = 0;
                }

                chunk.Add(line);
                length += line.Length + 1;
            }

            if (chunk.Count > 0)
            {
                yield return string.Join("\n", chunk);
            }
        }
    }
}
