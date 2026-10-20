using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    public static class RequestRoutes
    {
        public const string Pick = "req-pick";
        public const string Results = "req-res";
        public const string Make = "req-make";
        public const string Follow = "req-follow";
        public const string TvPage = "req-tv-page";
        public const string TvReview = "req-tv-review";
        public const string TvAll = "req-tv-all";
        public const string TvChoose = "req-tv-choose";
        public const string TvPick = "req-tv-pick";
        public const string TvConfirm = "req-tv-ok";
        public const string TvBack = "req-tv-back";
        public const string Cancel = "req-cancel";
        public const string Approve = "req-approve";
        public const string Deny = "req-deny";
        public const string DenyReason = "req-deny-reason";
        public const string ReasonField = "reason";

        // Season picker on cards posted before the two-path season flow; still handled.
        public const string Seasons = "req-seasons";
    }

    // One request is one Discord message that changes in place: results, details, seasons,
    // confirmation, then a final status. Kept within Discord's limits (25 options, 100-character
    // labels and custom IDs).
    public static class RequestCards
    {
        public const int OverviewLength = 700;

        public const uint DetailColor = 0x5865F2;
        public const uint SentColor = 0x2ECC71;
        public const uint PendingColor = 0xF1C40F;
        public const uint FollowingColor = 0x3498DB;
        public const uint RefusedColor = 0xE74C3C;
        public const uint CancelledColor = 0x95A5A6;

        // Choose seasons shows this many regular seasons per page; specials share page 1.
        public const int SeasonsPerPage = DiscordLimits.ChoicesPerMenu - 1;

        public static string TypeKey(ContentType type) => type == ContentType.Tv ? "tv" : "movie";

        public static ContentType? ParseTypeKey(string key) => key switch
        {
            "tv" => ContentType.Tv,
            "movie" => ContentType.Movie,
            _ => null
        };

        public static InteractionReply SearchResults(ContentType type, string query, IReadOnlyList<MediaLookupResult> results, bool castFromTmdb)
        {
            var hasCast = castFromTmdb && results.Any(r => r.Cast.Any());
            var content = DiscordText.SearchResults(type, query, results.Count) + (hasCast ? "\n" + DiscordText.TmdbSubtext : string.Empty);

            return new InteractionReply
            {
                Content = content,
                SelectMenu = new InteractionSelectMenu
                {
                    CustomId = PickId(type, query),
                    Placeholder = DiscordText.SearchPlaceholder,
                    Options = results.Take(DiscordLimits.ChoicesPerMenu).Select(r => new InteractionSelectOption(
                        DiscordLimits.Truncate(Titles.WithYear(r.Title, r.Year), DiscordLimits.OptionLabelLength),
                        r.ExternalId(type).ToString(),
                        ResultLine(r))).ToList()
                }
            };
        }

        // The query rides along (when it fits) so the details step can offer "Back to results".
        public static string PickId(ContentType type, string query)
        {
            var withQuery = $"{RequestRoutes.Pick}:{TypeKey(type)}:{query}";
            return query != null && withQuery.Length <= DiscordLimits.CustomIdLength ? withQuery : $"{RequestRoutes.Pick}:{TypeKey(type)}";
        }

        // "2024 · Apple TV+ · Joel Edgerton, Jennifer Connelly, Alice Braga", or with no cast
        // "2024 · Apple TV+ · Drama". Cast names are dropped from the end to fit 100 characters.
        public static string ResultLine(MediaLookupResult item)
        {
            var lead = new List<string>();

            if (item.LibraryId.HasValue)
            {
                lead.Add(DiscordText.InLibraryShort);
            }

            if (item.Year.HasValue)
            {
                lead.Add(item.Year.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (item.Maker != null)
            {
                lead.Add(item.Maker);
            }

            for (var names = item.Cast.Count; names > 0; names--)
            {
                var line = string.Join(DiscordText.Dot, lead.Append(string.Join(", ", item.Cast.Take(names))));

                if (line.Length <= DiscordLimits.OptionLabelLength)
                {
                    return line;
                }
            }

            var withGenre = item.Genres.Any() ? lead.Append(item.Genres.First()) : lead;

            return DiscordLimits.Truncate(string.Join(DiscordText.Dot, withGenre), DiscordLimits.OptionLabelLength);
        }

        // What the card says about a title's current state, or null when it can be requested.
        public static string StateLine(TitleState state)
        {
            if (state.Item.LibraryId.HasValue)
            {
                return DiscordText.StateInLibrary;
            }

            if (state.Active == null)
            {
                return null;
            }

            return state.CallerOwnsActive ? DiscordText.StateYours(state.Active.Status, state.Active.Id)
                : state.CallerFollows ? DiscordText.StateFollowing(state.Active.Id)
                : DiscordText.StateRequested(state.ActiveOwner, state.Active.Status, state.Active.Id);
        }

        public static InteractionReply Detail(ContentType type, TitleState state, string query = null)
        {
            var item = state.Item;
            var externalId = item.ExternalId(type).Value;
            var lines = new List<string> { DiscordText.BlockHeader(type, DiscordText.StepDetails) };
            var buttons = new List<InteractionButton>();

            if (item.LibraryId.HasValue)
            {
                lines.Add(DiscordText.StateInLibrary);
            }
            else if (state.Active != null)
            {
                lines.Add(state.CallerOwnsActive ? DiscordText.StateYours(state.Active.Status, state.Active.Id)
                    : state.CallerFollows ? DiscordText.StateFollowing(state.Active.Id)
                    : DiscordText.StateRequested(state.ActiveOwner, state.Active.Status, state.Active.Id));

                if (state.CallerOwnsActive && state.Active.Status == RequestStatus.Pending)
                {
                    buttons.Add(CancelButton(state.Active.Id));
                }

                if (!state.CallerOwnsActive && !state.CallerFollows)
                {
                    buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.Follow}:{TypeKey(type)}:{externalId}", Label = DiscordText.FollowLabel, Style = ButtonStyle.Primary });
                }
            }
            else
            {
                if (state.LastDenied != null)
                {
                    lines.Add(DiscordText.StatePreviouslyDenied(state.LastDenied.DecisionReason));
                }

                if (type == ContentType.Tv)
                {
                    buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.TvAll}:{externalId}", Label = DiscordText.AllSeasons, Style = ButtonStyle.Success });
                    buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.TvChoose}:{externalId}", Label = DiscordText.ChooseSeasonsLabel, Style = ButtonStyle.Primary });
                }
                else
                {
                    buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.Make}:{TypeKey(type)}:{externalId}", Label = DiscordText.RequestLabel, Style = ButtonStyle.Success });
                }
            }

            var back = ResultsButton(type, query);

            if (back != null)
            {
                buttons.Add(back);
            }

            return new InteractionReply
            {
                Content = string.Join("\n", lines),
                Embeds = new List<InteractionEmbed> { Card(type, item, state.Instance?.Name, DetailColor) },
                Buttons = buttons
            };
        }

        public static int PageCount(MediaLookupResult item) => Math.Max(1, (item.Seasons.Count + SeasonsPerPage - 1) / SeasonsPerPage);

        // The seasons on one page of "Choose seasons": specials first on page 1, then regular seasons.
        public static IReadOnlyList<int> PageSeasons(MediaLookupResult item, int page)
        {
            var regular = item.Seasons.OrderBy(s => s).Skip(page * SeasonsPerPage).Take(SeasonsPerPage);
            return (page == 0 && item.HasSpecials ? new[] { 0 } : Array.Empty<int>()).Concat(regular).ToList();
        }

        // "Choose seasons": every season of any show, a page at a time. Picks are kept in the
        // buttons and menu (Discord keeps no state), shown as already selected, and summed up.
        public static InteractionReply SeasonPicker(TitleState state, int page, SeasonChoice picks)
        {
            var item = state.Item;
            var tvdbId = item.TvdbId.Value;
            var pages = PageCount(item);
            page = Math.Clamp(page, 0, pages - 1);
            picks ??= new SeasonChoice(false, Array.Empty<int>(), false);
            var spec = EncodeSeasons(picks);
            var onPage = PageSeasons(item, page);

            var lines = new List<string> { DiscordText.BlockHeader(ContentType.Tv, DiscordText.StepSeasons), DiscordText.ChooseSeasonsPrompt };

            if (pages > 1)
            {
                lines.Add(DiscordText.SeasonPageHint(Range(item, page), item.Seasons.Count));
            }

            lines.Add(picks.Seasons.Any() || picks.Future ? DiscordText.PickedSoFar(DiscordText.SeasonList(picks.Seasons, picks.Future)) : DiscordText.NothingPickedYet);

            var buttons = new List<InteractionButton>();

            if (page > 0)
            {
                buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.TvPage}:{tvdbId}:{page - 1}:{spec}", Label = DiscordText.PreviousSeasons(Range(item, page - 1)) });
            }

            if (page < pages - 1)
            {
                buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.TvPage}:{tvdbId}:{page + 1}:{spec}", Label = DiscordText.NextSeasons(Range(item, page + 1)) });
            }

            var toggled = EncodeSeasons(picks with { Future = !picks.Future });
            buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.TvPage}:{tvdbId}:{page}:{toggled}", Label = DiscordText.FutureToggle(picks.Future), Style = picks.Future ? ButtonStyle.Primary : ButtonStyle.Secondary });
            buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.TvReview}:{tvdbId}:{spec}", Label = DiscordText.ContinueLabel, Style = ButtonStyle.Success, Disabled = !picks.Seasons.Any() && !picks.Future });
            buttons.Add(BackButton(tvdbId));

            var menu = new InteractionSelectMenu
            {
                CustomId = $"{RequestRoutes.TvPick}:{tvdbId}:{page}:{spec}",
                Placeholder = DiscordText.SeasonsPlaceholder,
                MinValues = 0,
                MaxValues = onPage.Count,
                Options = onPage.Select(s => new InteractionSelectOption(
                    s == 0 ? DiscordText.Specials : DiscordText.SeasonLabel(s),
                    $"s{s}",
                    s == 0 ? DiscordText.SpecialsHint : null,
                    picks.Seasons.Contains(s))).ToList()
            };

            return new InteractionReply
            {
                Content = string.Join("\n", lines),
                Embeds = new List<InteractionEmbed> { Card(ContentType.Tv, item, state.Instance?.Name, DetailColor) },
                SelectMenu = onPage.Any() ? menu : null,
                Buttons = buttons
            };
        }

        // A pick on one page replaces that page's picks and keeps the others.
        public static SeasonChoice WithPagePicks(SeasonChoice picks, IReadOnlyCollection<int> pageSeasons, IEnumerable<string> values)
        {
            var chosen = FromMenu(values).Seasons;
            var kept = (picks?.Seasons ?? Array.Empty<int>()).Where(s => !pageSeasons.Contains(s));

            return new SeasonChoice(false, kept.Concat(chosen.Where(pageSeasons.Contains)).Distinct().OrderBy(s => s).ToList(), picks?.Future ?? false);
        }

        private static (int From, int To) Range(MediaLookupResult item, int page)
        {
            var seasons = item.Seasons.OrderBy(s => s).Skip(page * SeasonsPerPage).Take(SeasonsPerPage).ToList();
            return seasons.Any() ? (seasons.First(), seasons.Last()) : (0, 0);
        }

        // The choice restated in plain words, with Request and Back.
        public static InteractionReply Confirm(TitleState state, SeasonChoice choice)
        {
            var tvdbId = state.Item.TvdbId.Value;

            return new InteractionReply
            {
                Content = DiscordText.BlockHeader(ContentType.Tv, DiscordText.StepConfirm) + "\n" + DiscordText.YouChose(DiscordText.SeasonChoiceText(choice, state.Item.Seasons)),
                Embeds = new List<InteractionEmbed> { Card(ContentType.Tv, state.Item, state.Instance?.Name, DetailColor) },
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{RequestRoutes.TvConfirm}:{tvdbId}:{EncodeSeasons(choice)}", Label = DiscordText.RequestLabel, Style = ButtonStyle.Success },
                    new() { CustomId = $"{RequestRoutes.TvPage}:{tvdbId}:0:{EncodeSeasons(choice)}", Label = DiscordText.BackLabel, Style = ButtonStyle.Secondary }
                }
            };
        }

        // The end of a request block: an unmistakable status, the card kept for context.
        public static InteractionReply Final(ContentType type, RequestResult result, string seasonsText = null)
        {
            var (status, color) = result.Outcome switch
            {
                RequestOutcome.Sent => (DiscordText.FinalSent, SentColor),
                RequestOutcome.Pending => (DiscordText.FinalPending, PendingColor),
                RequestOutcome.Following => (DiscordText.FinalFollowing, FollowingColor),
                RequestOutcome.AlreadyAvailable => (DiscordText.FinalAvailable, SentColor),
                RequestOutcome.Failed => (DiscordText.FinalFailed, RefusedColor),
                _ => (DiscordText.FinalRefused, RefusedColor)
            };

            var lines = new List<string> { $"{status}{DiscordText.Dot}{result.Message}" };

            if (type == ContentType.Tv && seasonsText != null && (result.Outcome == RequestOutcome.Sent || result.Outcome == RequestOutcome.Pending))
            {
                lines.Add(DiscordText.SeasonsLine(seasonsText));
            }

            var reply = new InteractionReply
            {
                Content = DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength),
                Embeds = result.Item == null ? new List<InteractionEmbed>() : new List<InteractionEmbed> { Card(type, result.Item, null, color) }
            };

            if (result.Outcome == RequestOutcome.Pending && result.Request != null)
            {
                reply.Buttons.Add(CancelButton(result.Request.Id));
            }

            return reply;
        }

        // A request message showing the request's real state; Cancel only while it is pending.
        public static InteractionReply Status(Request request, MediaLookupResult item, string instance)
        {
            var (status, color, message) = request.Status switch
            {
                RequestStatus.Pending => (DiscordText.FinalPending, PendingColor, DiscordText.RequestPending(request.Label, request.Id)),
                RequestStatus.Approved => (DiscordText.FinalApproved, SentColor, DiscordText.RequestApprovedStatus(request.Label, request.Id, instance)),
                RequestStatus.Available => (DiscordText.FinalAvailable, SentColor, DiscordText.RequestAvailableStatus(request.Label, request.Id)),
                RequestStatus.Denied => (DiscordText.FinalDenied, RefusedColor, DiscordText.RequestDeniedStatus(request.Label, request.Id, request.DecisionReason)),
                RequestStatus.Cancelled => (DiscordText.FinalCancelled, CancelledColor, request.DecidedByUserId.HasValue
                    ? DiscordText.RequestCancelledByAdmin(request.Label, request.Id, request.DecisionReason)
                    : DiscordText.RequestCancelled(request.Label, request.Id)),
                _ => (DiscordText.FinalFailed, RefusedColor, DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason))
            };

            var lines = new List<string> { $"{status}{DiscordText.Dot}{message}" };

            if (request.ContentType == ContentType.Tv)
            {
                lines.Add(DiscordText.SeasonsLine(DiscordText.Seasons(request)));
            }

            var reply = new InteractionReply
            {
                Content = DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength),
                Embeds = item == null ? new List<InteractionEmbed>() : new List<InteractionEmbed> { Card(request.ContentType, item, instance, color) },
                KeepEmbeds = item == null
            };

            if (request.Status == RequestStatus.Pending)
            {
                reply.Buttons.Add(CancelButton(request.Id));
            }

            return reply;
        }

        // A step that ends the block early (nothing found, backend down, access off).
        public static InteractionReply Stopped(ContentType? type, string message) => new()
        {
            Content = $"{(type.HasValue ? DiscordText.BlockHeader(type.Value, DiscordText.StepStopped) + "\n" : string.Empty)}{DiscordText.FinalRefused}{DiscordText.Dot}{message}"
        };

        public static string EncodeSeasons(SeasonChoice choice)
        {
            if (choice.All)
            {
                return "all";
            }

            var ranges = new List<string>();
            var sorted = choice.Seasons.Distinct().OrderBy(s => s).ToList();

            for (var i = 0; i < sorted.Count; i++)
            {
                var start = sorted[i];

                while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1)
                {
                    i++;
                }

                ranges.Add(start == sorted[i] ? $"{start}" : $"{start}-{sorted[i]}");
            }

            var text = string.Join(".", ranges);

            // Scattered picks on very long shows: a bitset (base64url) is shorter than the list.
            if (text.Length > 40)
            {
                var bits = new byte[(sorted.Last() / 8) + 1];

                foreach (var season in sorted)
                {
                    bits[season / 8] |= (byte)(1 << (season % 8));
                }

                text = "b" + Convert.ToBase64String(bits).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }

            return text + (choice.Future ? "f" : string.Empty);
        }

        public static SeasonChoice DecodeSeasons(string spec)
        {
            if (string.IsNullOrEmpty(spec))
            {
                return null;
            }

            if (spec == "all")
            {
                return SeasonChoice.Everything;
            }

            var future = spec.EndsWith("f");
            var seasons = new List<int>();

            if (spec.StartsWith("b"))
            {
                try
                {
                    var data = spec.Substring(1).TrimEnd('f').Replace('-', '+').Replace('_', '/');
                    var bits = Convert.FromBase64String(data.PadRight(data.Length + ((4 - (data.Length % 4)) % 4), '='));

                    for (var i = 0; i < bits.Length * 8 && i <= 2000; i++)
                    {
                        if ((bits[i / 8] & (1 << (i % 8))) != 0)
                        {
                            seasons.Add(i);
                        }
                    }
                }
                catch (FormatException)
                {
                    return null;
                }

                return seasons.Any() || future ? new SeasonChoice(false, seasons, future) : null;
            }

            foreach (var part in spec.TrimEnd('f').Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var bounds = part.Split('-');

                if (!int.TryParse(bounds[0], out var from) || !int.TryParse(bounds[^1], out var to) || from < 0 || to < from || to - from > 500)
                {
                    return null;
                }

                seasons.AddRange(Enumerable.Range(from, to - from + 1));
            }

            return seasons.Any() || future ? new SeasonChoice(false, seasons, future) : null;
        }

        // Values from the "Choose seasons" menu: s{n} and "future". "All" can't be picked there.
        public static SeasonChoice FromMenu(IEnumerable<string> values)
        {
            var list = values.ToList();
            var seasons = list.Where(v => v.StartsWith("s") && int.TryParse(v.AsSpan(1), out _)).Select(v => int.Parse(v.AsSpan(1))).ToList();

            return new SeasonChoice(false, seasons, list.Contains("future"));
        }

        // Values from the old single season picker ("all", s{n}, "future"); "all" wins over picks.
        public static SeasonChoice ParseSeasons(IEnumerable<string> values)
        {
            var list = values.ToList();
            var seasons = list.Where(v => v.StartsWith("s") && int.TryParse(v.AsSpan(1), out _)).Select(v => int.Parse(v.AsSpan(1))).ToList();

            return new SeasonChoice(list.Contains("all"), list.Contains("all") ? Array.Empty<int>() : seasons, list.Contains("future"));
        }

        public static InteractionReply Approval(Request request, string ownerName, string instanceName, MediaLookupResult item)
        {
            return new InteractionReply
            {
                Content = DiscordText.ApprovalCard(request, ownerName, instanceName),
                Embeds = item == null ? new List<InteractionEmbed>() : new List<InteractionEmbed> { Card(request.ContentType, item, null, PendingColor, compact: true) },
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{RequestRoutes.Approve}:{request.Id}", Label = DiscordText.ApproveLabel, Style = ButtonStyle.Success },
                    new() { CustomId = $"{RequestRoutes.Deny}:{request.Id}", Label = DiscordText.DenyLabel, Style = ButtonStyle.Danger }
                }
            };
        }

        public static InteractionButton CancelButton(int requestId) =>
            new() { CustomId = $"{RequestRoutes.Cancel}:{requestId}", Label = DiscordText.CancelLabel, Style = ButtonStyle.Danger };

        // The reply to a request outside a block (old cards); pending ones the user owns get a Cancel button.
        public static InteractionReply Outcome(Users.ServiceResult result)
        {
            var reply = new InteractionReply { Content = DiscordLimits.Truncate(result.Message, DiscordLimits.MessageLength), Ephemeral = !result.Success };

            if (result is RequestResult { Request: { Status: RequestStatus.Pending } pending, Outcome: RequestOutcome.Pending })
            {
                reply.Buttons.Add(CancelButton(pending.Id));
            }

            return reply;
        }

        public static InteractionModal LinkPlex() => new()
        {
            CustomId = PanelRoutes.LinkPlexForm,
            Title = DiscordText.LinkPlexTitle,
            Fields = new List<InteractionTextField> { new(PanelRoutes.PlexUsernameField, DiscordText.LinkPlexLabel, Required: true, MaxLength: 100, Paragraph: false) }
        };

        public static InteractionModal DenyReason(int requestId)
        {
            return new InteractionModal
            {
                CustomId = $"{RequestRoutes.DenyReason}:{requestId}",
                Title = DiscordText.DenyReasonTitle,
                Fields = new List<InteractionTextField> { new(RequestRoutes.ReasonField, DiscordText.DenyReasonLabel, Required: false) }
            };
        }

        public static InteractionButton ResultsButton(ContentType type, string query)
        {
            var customId = query == null ? null : $"{RequestRoutes.Results}:{TypeKey(type)}:{query}";

            return customId == null || customId.Length > DiscordLimits.CustomIdLength
                ? null
                : new InteractionButton { CustomId = customId, Label = DiscordText.BackToResults, Style = ButtonStyle.Secondary };
        }

        private static InteractionButton BackButton(int tvdbId) =>
            new() { CustomId = $"{RequestRoutes.TvBack}:{tvdbId}", Label = DiscordText.BackLabel, Style = ButtonStyle.Secondary };

        // The detail card: large poster, every field the backend returns, cast when available.
        public static InteractionEmbed Card(ContentType type, MediaLookupResult item, string instance, uint color, bool compact = false)
        {
            var fields = new List<KeyValuePair<string, string>>();

            void Add(string name, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fields.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            Add(DiscordText.FieldGenres, item.Genres.Any() ? string.Join(", ", item.Genres.Take(4)) : null);
            Add(DiscordText.FieldRating, DiscordText.Rating(item));
            Add(DiscordText.FieldCertification, item.Certification);
            Add(DiscordText.FieldRuntime, item.Runtime.HasValue ? DiscordText.Runtime(item.Runtime.Value, type == ContentType.Tv) : null);

            if (type == ContentType.Tv)
            {
                Add(DiscordText.FieldNetwork, item.Network);
                Add(DiscordText.FieldStatus, DiscordText.Capitalize(item.Status));
                Add(DiscordText.FieldSeasons, DiscordText.SeasonCount(item.Seasons.Count, item.HasSpecials));
                Add(DiscordText.FieldEpisodes, item.EpisodeCount?.ToString(CultureInfo.InvariantCulture));
                Add(DiscordText.FieldAired, DiscordText.Aired(item.FirstAired, item.LastAired, item.Continuing));
            }
            else
            {
                Add(DiscordText.FieldStudio, item.Studio);
                Add(DiscordText.FieldStatus, DiscordText.Capitalize(item.Status));
                Add(DiscordText.FieldReleased, DiscordText.Released(item.InCinemas, item.DigitalRelease, item.PhysicalRelease));
            }

            Add(DiscordText.FieldCast, item.Cast.Any() ? string.Join(", ", item.Cast) : null);
            Add(DiscordText.FieldGoesTo, instance);

            return new InteractionEmbed
            {
                Title = Titles.WithYear(item.Title, item.Year),
                Url = item.ImdbId != null ? $"https://www.imdb.com/title/{item.ImdbId}/" : null,
                Description = DiscordLimits.Truncate(item.Overview, compact ? 300 : OverviewLength),
                ImageUrl = compact ? null : item.PosterUrl,
                ThumbnailUrl = compact ? item.PosterUrl : null,
                Color = color,
                Fields = compact ? fields.Take(4).ToList() : fields,
                Footer = item.Cast.Any() ? DiscordText.TmdbAttribution : null,
                FooterIconUrl = item.Cast.Any() ? DiscordText.TmdbLogoUrl : null
            };
        }
    }
}
