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

    // One version's card as posted: a server finds it by its button and updates it when its title or description changed.
    // On staging the version it runs is shown as its beta version (0.4.17-beta.1); every other version as its number.
    public record VersionCard(Version Version, string Title, string Description, InteractionReply Reply);

    // What's new, as cards. In the channels each version is one line with a Read changes button; pressing it shows the
    // full card, only to whoever pressed it: the version and its date as the title, then "For users" and "For admins"
    // (only those with something in them), one line per change.
    public static class VersionCards
    {
        // Discord's limits for cards: 4,000 characters of text in all of a message's cards (a heading and a little room
        // kept aside), and no more than forty parts to a message (a card is two).
        public const int MessageTextLength = 4000;
        public const int CardTextLength = 3700;
        public const int CardsPerMessage = 10;

        public const uint UsersColor = 0x2BB3A3;
        public const uint AdminsColor = 0xE0A030;

        private static readonly Regex BetaTitle = new(@"^Requestarr (?<shown>(?<version>\d+\.\d+\.\d+)-beta\.\d+)(?: ·|$)");
        private static readonly Regex BoldLead = new(@"^\*\*(?<lead>[^*]+?):?\*\*");

        // shown: how the version reads in the title (its beta version on staging), its number when null. Readers see the
        // main change in a few words, so they know whether to open it: the admin channel's card covers both sections,
        // admins' lines first.
        public static VersionCard Collapsed(ChangelogVersion version, VersionAudience audience, int thisYear, string shown = null)
        {
            var title = DiscordText.VersionCardTitle(shown ?? version.Version.ToString(), version.Date, thisYear);
            var lines = audience == VersionAudience.Admins ? version.Admins.Concat(version.Users).ToList() : version.Users.ToList();
            var description = DiscordText.VersionCardSummary(MainChange(lines[0]), lines.Count - 1);

            var reply = new InteractionReply
            {
                Embeds = new List<InteractionEmbed> { new() { Title = title, Description = description, Color = audience == VersionAudience.Admins ? AdminsColor : UsersColor } },
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = ReadChangesPrefix(audience) + version.Version, Label = DiscordText.ReadChangesLabel, Style = ButtonStyle.Secondary }
                }
            };

            return new VersionCard(version.Version, title, description, reply);
        }

        // The version a posted card is for, read from its Read changes button, or null when the message isn't one of this
        // audience's cards. The button, unlike the title, has never changed since the first cards.
        public static Version Parse(IEnumerable<string> customIds, VersionAudience audience)
        {
            var prefix = ReadChangesPrefix(audience);
            var id = customIds?.FirstOrDefault(i => i.StartsWith(prefix, StringComparison.Ordinal));
            return id != null && Version.TryParse(id.AsSpan(prefix.Length), out var version) ? version : null;
        }

        // The beta version a posted card's title shows for its version (staging's cards keep the beta they were
        // published as), or null.
        public static string BetaShown(string title, Version version)
        {
            var match = BetaTitle.Match(title ?? string.Empty);
            return match.Success && Version.Parse(match.Groups["version"].Value) == version ? match.Groups["shown"].Value : null;
        }

        private static string ReadChangesPrefix(VersionAudience audience) =>
            $"{VersionCardRoutes.ReadChanges}:{(audience == VersionAudience.Admins ? "a" : "u")}:";

        // The full card, private: one coloured card per section (a long section carries on in more cards, split between
        // lines), in as many messages as Discord's limits need. Cards show text at a message's size, with headings.
        public static IReadOnlyList<InteractionReply> Full(ChangelogVersion version, VersionAudience audience, string shown = null)
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

            var title = $"## {DiscordText.VersionCardFullTitle(shown ?? version.Version.ToString(), version.Date)}\n";
            var cards = new List<InteractionCard>();

            foreach (var (heading, lines, color) in sections)
            {
                var first = true;

                foreach (var chunk in Chunks(lines.Select(l => DiscordLimits.Truncate($"- {l}", CardTextLength)), CardTextLength))
                {
                    var header = $"{(cards.Count == 0 ? title : string.Empty)}### {heading}{(first ? string.Empty : DiscordText.VersionNotesContinued)}\n";
                    cards.Add(new InteractionCard { Text = header + chunk, Color = color });
                    first = false;
                }
            }

            var replies = new List<InteractionReply>();
            var current = new List<InteractionCard>();
            var length = 0;

            foreach (var card in cards)
            {
                if (current.Count > 0 && (current.Count == CardsPerMessage || length + card.Text.Length > MessageTextLength))
                {
                    replies.Add(new InteractionReply { Cards = current, Ephemeral = true });
                    current = new List<InteractionCard>();
                    length = 0;
                }

                current.Add(card);
                length += card.Text.Length;
            }

            replies.Add(new InteractionReply { Cards = current, Ephemeral = true });
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
