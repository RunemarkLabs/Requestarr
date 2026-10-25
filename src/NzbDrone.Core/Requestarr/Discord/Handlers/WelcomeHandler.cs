using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Access;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // The welcome channel's buttons, for anyone in the server, and the private form they open. Whether a click may do
    // anything is decided by the access request service (one open request, cooldowns, the spam guard).
    public class WelcomeHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IAccessRequestService _access;
        private readonly IMediaServerConnections _mediaServers;

        public WelcomeHandler(ICallerResolver callers, IAccessRequestService access, IMediaServerConnections mediaServers)
        {
            _callers = callers;
            _access = access;
            _mediaServers = mediaServers;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, WelcomeRoutes.Request),
            (InteractionKind.Button, WelcomeRoutes.Media),
            (InteractionKind.SelectMenu, WelcomeRoutes.Types),
            (InteractionKind.Button, WelcomeRoutes.Continue),
            (InteractionKind.ModalSubmit, WelcomeRoutes.Form),
            (InteractionKind.ModalSubmit, WelcomeRoutes.MediaForm)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (!caller.RightServer)
            {
                await responder.Ephemeral(DiscordText.WrongServer);
                return;
            }

            var args = interaction.CustomIdArgs;

            switch (interaction.Route)
            {
                case WelcomeRoutes.Request:
                {
                    var answer = await _access.ClickAsync(interaction.UserId, caller.IsAdmin);

                    if (!answer.OpenForm)
                    {
                        await responder.Ephemeral(answer.Message);
                        return;
                    }

                    // Nothing to pick from: straight to the note.
                    if (!_access.OfferedTypes().Any() && !_mediaServers.Connected().Any())
                    {
                        await responder.ShowModalAsync(Form(Array.Empty<ContentType>(), null, fromWelcome: true));
                        return;
                    }

                    await responder.RespondAsync(FormMessage(Array.Empty<ContentType>(), null, true));
                    return;
                }

                case WelcomeRoutes.Types:
                {
                    var types = interaction.Values.Select(HandlerSupport.ParseType).Where(t => t.HasValue).Select(t => t.Value).ToList();
                    var media = interaction.Values.Select(v => v.StartsWith("media:") ? ParseProvider(v.Substring(6)) : null).FirstOrDefault(p => p.HasValue);
                    await responder.UpdateMessageAsync(FormMessage(types, media, true));
                    return;
                }

                case WelcomeRoutes.Continue:
                {
                    var (types, media) = Parse(args);
                    await responder.ShowModalAsync(Form(types, media, fromWelcome: false));
                    return;
                }

                case WelcomeRoutes.Form:
                {
                    var (types, media) = Parse(args);
                    interaction.Fields.TryGetValue(WelcomeRoutes.NoteField, out var note);
                    interaction.Fields.TryGetValue(WelcomeRoutes.UsernameField, out var username);

                    await responder.DeferAsync(ephemeral: true);
                    var result = await _access.SubmitAsync(interaction.UserId, interaction.Username, interaction.DisplayName, types, note, media, username);

                    // From the private form: the form becomes the answer. Opened straight from the welcome message
                    // (nothing to pick): a private answer, and the welcome message stays as it is.
                    if (args.ElementAtOrDefault(2) == "f")
                    {
                        await responder.EditAsync(new InteractionReply { Content = result.Message, Ephemeral = true });
                    }
                    else
                    {
                        await responder.Ephemeral(result.Message);
                    }

                    return;
                }

                case WelcomeRoutes.Media:
                {
                    if (ParseProvider(args.ElementAtOrDefault(0)) is not { } provider || !_mediaServers.IsConnected(provider))
                    {
                        await responder.Ephemeral(DiscordText.MediaServerNotConnected(ParseProvider(args.ElementAtOrDefault(0)) ?? MediaServerProvider.Plex));
                        return;
                    }

                    var answer = await _access.ClickAsync(interaction.UserId, caller.IsAdmin, provider);

                    if (!answer.OpenForm)
                    {
                        await responder.Ephemeral(answer.Message);
                        return;
                    }

                    await responder.ShowModalAsync(new InteractionModal
                    {
                        CustomId = $"{WelcomeRoutes.MediaForm}:{Key(provider)}",
                        Title = DiscordText.MediaFormTitle(provider),
                        Fields = new List<InteractionTextField> { new(WelcomeRoutes.UsernameField, DiscordText.MediaUsernameLabel(provider), Required: true, MaxLength: 100, Paragraph: false) }
                    });
                    return;
                }

                case WelcomeRoutes.MediaForm:
                {
                    interaction.Fields.TryGetValue(WelcomeRoutes.UsernameField, out var username);
                    await responder.DeferAsync(ephemeral: true);

                    var result = ParseProvider(args.ElementAtOrDefault(0)) is { } provider
                        ? await _access.SubmitMediaAsync(interaction.UserId, interaction.Username, interaction.DisplayName, provider, username)
                        : ServiceResult.Fail(DiscordText.MediaServerNotConnected(MediaServerProvider.Plex));

                    await responder.Ephemeral(result.Message);
                    return;
                }
            }
        }

        // The private form: one menu with the types (and connected media servers), and Continue once something's picked.
        private InteractionReply FormMessage(IReadOnlyCollection<ContentType> types, MediaServerProvider? media, bool ephemeral)
        {
            var options = _access.OfferedTypes()
                .Select(t => new InteractionSelectOption(DiscordText.Label(t), Key(t), null, types.Contains(t)))
                .Concat(_mediaServers.Connected().Select(p => new InteractionSelectOption(DiscordText.MediaAccessOption(p), $"media:{Key(p)}", null, media == p)))
                .ToList();

            return new InteractionReply
            {
                Content = DiscordText.AccessFormIntro,
                Ephemeral = ephemeral,
                SelectMenu = new InteractionSelectMenu
                {
                    CustomId = WelcomeRoutes.Types,
                    Placeholder = DiscordText.AccessFormPlaceholder,
                    MinValues = 1,
                    MaxValues = options.Count,
                    Options = options
                },
                Buttons = new List<InteractionButton>
                {
                    new()
                    {
                        CustomId = $"{WelcomeRoutes.Continue}:{string.Join(",", types.Select(Key))}:{(media.HasValue ? Key(media.Value) : string.Empty)}",
                        Label = DiscordText.ContinueLabel,
                        Style = ButtonStyle.Primary,
                        Disabled = types.Count == 0 && media == null
                    }
                }
            };
        }

        private static InteractionModal Form(IReadOnlyCollection<ContentType> types, MediaServerProvider? media, bool fromWelcome)
        {
            var fields = new List<InteractionTextField> { new(WelcomeRoutes.NoteField, DiscordText.AccessNoteLabel, Required: false, MaxLength: 400) };

            if (media is { } provider)
            {
                fields.Add(new InteractionTextField(WelcomeRoutes.UsernameField, DiscordText.MediaUsernameLabel(provider), Required: true, MaxLength: 100, Paragraph: false));
            }

            return new InteractionModal
            {
                CustomId = $"{WelcomeRoutes.Form}:{string.Join(",", types.Select(Key))}:{(media.HasValue ? Key(media.Value) : string.Empty)}:{(fromWelcome ? "d" : "f")}",
                Title = DiscordText.AccessFormTitle,
                Fields = fields
            };
        }

        private static (List<ContentType> Types, MediaServerProvider? Media) Parse(IReadOnlyList<string> args) =>
            ((args.ElementAtOrDefault(0) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(HandlerSupport.ParseType).Where(t => t.HasValue).Select(t => t.Value).ToList(),
             ParseProvider(args.ElementAtOrDefault(1)));

        private static MediaServerProvider? ParseProvider(string value) => value?.ToLowerInvariant() switch
        {
            "plex" => MediaServerProvider.Plex,
            "jellyfin" => MediaServerProvider.Jellyfin,
            _ => null
        };

        private static string Key(MediaServerProvider provider) => provider.ToString().ToLowerInvariant();

        private static string Key(ContentType type) => type switch
        {
            ContentType.Tv => "tv",
            ContentType.Music => "music",
            _ => "movie"
        };
    }
}
