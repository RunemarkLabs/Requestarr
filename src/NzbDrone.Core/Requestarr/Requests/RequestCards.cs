using System;
using System.Collections.Generic;
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
        public const string Make = "req-make";
        public const string Seasons = "req-seasons";
        public const string Cancel = "req-cancel";
        public const string Approve = "req-approve";
        public const string Deny = "req-deny";
        public const string DenyReason = "req-deny-reason";
        public const string ReasonField = "reason";
    }

    // Discord messages for requesting, kept within Discord's limits (25 options, 100-character labels).
    public static class RequestCards
    {
        public const int OverviewLength = 600;
        public const int MaxSeasonOptions = DiscordLimits.ChoicesPerMenu - 2;

        public static string TypeKey(ContentType type) => type == ContentType.Tv ? "tv" : "movie";

        public static ContentType? ParseTypeKey(string key) => key switch
        {
            "tv" => ContentType.Tv,
            "movie" => ContentType.Movie,
            _ => null
        };

        public static InteractionReply SearchResults(ContentType type, string query, IReadOnlyList<MediaLookupResult> results)
        {
            return new InteractionReply
            {
                Content = DiscordText.SearchResults(type, query, results.Count),
                SelectMenu = new InteractionSelectMenu
                {
                    CustomId = $"{RequestRoutes.Pick}:{TypeKey(type)}",
                    Placeholder = DiscordText.SearchPlaceholder,
                    Options = results.Take(DiscordLimits.ChoicesPerMenu).Select(r => new InteractionSelectOption(
                        DiscordLimits.Truncate(r.Year.HasValue ? $"{r.Title} ({r.Year})" : r.Title, DiscordLimits.OptionLabelLength),
                        r.ExternalId(type).ToString(),
                        r.LibraryId.HasValue ? DiscordText.StateInLibrary : DiscordLimits.Truncate(r.Overview, DiscordLimits.OptionLabelLength))).ToList()
                }
            };
        }

        public static InteractionReply Detail(ContentType type, TitleState state)
        {
            var item = state.Item;
            var externalId = item.ExternalId(type);
            string stateLine = null;
            var buttons = new List<InteractionButton>();
            InteractionSelectMenu seasons = null;

            if (item.LibraryId.HasValue)
            {
                stateLine = DiscordText.StateInLibrary;
            }
            else if (state.Active != null)
            {
                stateLine = state.CallerOwnsActive ? DiscordText.StateYours(state.Active.Status, state.Active.Id)
                    : state.CallerFollows ? DiscordText.StateFollowing(state.Active.Id)
                    : DiscordText.StateRequested(state.ActiveOwner, state.Active.Status, state.Active.Id);

                if (state.CallerOwnsActive && state.Active.Status == RequestStatus.Pending)
                {
                    buttons.Add(CancelButton(state.Active.Id));
                }

                if (!state.CallerOwnsActive && !state.CallerFollows)
                {
                    buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.Make}:{TypeKey(type)}:{externalId}", Label = DiscordText.FollowLabel, Style = ButtonStyle.Primary });
                }
            }
            else
            {
                if (state.LastDenied != null)
                {
                    stateLine = DiscordText.StatePreviouslyDenied(state.LastDenied.DecisionReason);
                }

                if (type == ContentType.Tv)
                {
                    seasons = SeasonPicker(externalId.Value, item);
                }
                else
                {
                    buttons.Add(new InteractionButton { CustomId = $"{RequestRoutes.Make}:{TypeKey(type)}:{externalId}", Label = DiscordText.RequestLabel, Style = ButtonStyle.Success });
                }
            }

            return new InteractionReply
            {
                Content = stateLine,
                Embeds = new List<InteractionEmbed> { Embed(item, state.Instance?.Name) },
                Buttons = buttons,
                SelectMenu = seasons
            };
        }

        // "All seasons", "Future seasons", and the most recent seasons (25-option limit).
        public static InteractionSelectMenu SeasonPicker(int tvdbId, MediaLookupResult item)
        {
            var options = new List<InteractionSelectOption> { new(DiscordText.AllSeasons, "all") };
            options.AddRange(item.Seasons.OrderByDescending(s => s).Take(MaxSeasonOptions).OrderBy(s => s).Select(s => new InteractionSelectOption($"Season {s}", $"s{s}")));
            options.Add(new InteractionSelectOption(DiscordText.FutureSeasons, "future", DiscordText.FutureSeasonsHint));

            return new InteractionSelectMenu
            {
                CustomId = $"{RequestRoutes.Seasons}:{tvdbId}",
                Placeholder = DiscordText.SeasonsPlaceholder,
                MinValues = 1,
                MaxValues = options.Count,
                Options = options
            };
        }

        public static SeasonChoice ParseSeasons(IEnumerable<string> values)
        {
            var list = values.ToList();
            var seasons = list.Where(v => v.StartsWith("s") && int.TryParse(v.AsSpan(1), out _)).Select(v => int.Parse(v.AsSpan(1))).ToList();

            return new SeasonChoice(list.Contains("all"), seasons, list.Contains("future"));
        }

        public static InteractionReply Approval(Request request, string ownerName, string instanceName, MediaLookupResult item)
        {
            return new InteractionReply
            {
                Content = DiscordText.ApprovalCard(request, ownerName, instanceName),
                Embeds = item == null ? new List<InteractionEmbed>() : new List<InteractionEmbed> { Embed(item, null) },
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{RequestRoutes.Approve}:{request.Id}", Label = DiscordText.ApproveLabel, Style = ButtonStyle.Success },
                    new() { CustomId = $"{RequestRoutes.Deny}:{request.Id}", Label = DiscordText.DenyLabel, Style = ButtonStyle.Danger }
                }
            };
        }

        public static InteractionButton CancelButton(int requestId) =>
            new() { CustomId = $"{RequestRoutes.Cancel}:{requestId}", Label = DiscordText.CancelLabel, Style = ButtonStyle.Danger };

        // The reply to a request; pending ones the user owns get a Cancel button.
        public static InteractionReply Outcome(Users.ServiceResult result)
        {
            var reply = new InteractionReply { Content = DiscordLimits.Truncate(result.Message, DiscordLimits.MessageLength), Ephemeral = !result.Success };

            if (result is RequestResult { Request: { Status: RequestStatus.Pending } pending })
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

        private static InteractionEmbed Embed(MediaLookupResult item, string instance)
        {
            var fields = new List<KeyValuePair<string, string>>();

            if (item.Seasons.Any())
            {
                fields.Add(new KeyValuePair<string, string>("Seasons", item.Seasons.Count.ToString()));
            }

            if (instance != null)
            {
                fields.Add(new KeyValuePair<string, string>("Goes to", instance));
            }

            return new InteractionEmbed
            {
                Title = item.Year.HasValue ? $"{item.Title} ({item.Year})" : item.Title,
                Description = DiscordLimits.Truncate(item.Overview, OverviewLength),
                ThumbnailUrl = item.PosterUrl,
                Fields = fields
            };
        }
    }
}
