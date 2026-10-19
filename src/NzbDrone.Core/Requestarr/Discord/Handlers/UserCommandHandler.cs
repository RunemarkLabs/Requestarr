using System.Collections.Generic;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /user ... (admin only, admin channel only)
    public class UserCommandHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IUserManagementService _users;

        public UserCommandHandler(ICallerResolver callers, IUserManagementService users)
        {
            _callers = callers;
            _users = users;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "user add"),
            (InteractionKind.SlashCommand, "user remove"),
            (InteractionKind.SlashCommand, "user suspend"),
            (InteractionKind.SlashCommand, "user unsuspend"),
            (InteractionKind.SlashCommand, "user access"),
            (InteractionKind.SlashCommand, "user quota"),
            (InteractionKind.SlashCommand, "user info"),
            (InteractionKind.SlashCommand, "user mediaserver")
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.AdminDenial != null)
            {
                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            var member = interaction.GetSnowflake("member");

            if (member == null)
            {
                await responder.Ephemeral(DiscordText.MemberNotFound);
                return;
            }

            var actor = HandlerSupport.ActorFor(interaction, caller);
            var subcommand = interaction.CommandName.Substring("user ".Length).ToLowerInvariant();

            switch (subcommand)
            {
                case "add":
                {
                    // Several Discord API calls: acknowledge first to stay inside Discord's 3-second window.
                    await responder.DeferAsync();

                    var access = new Dictionary<ContentType, AccessState>();
                    AddLevel(access, ContentType.Movie, interaction.GetOption<string>("movie"));
                    AddLevel(access, ContentType.Tv, interaction.GetOption<string>("tv"));
                    AddLevel(access, ContentType.Music, interaction.GetOption<string>("music"));

                    await responder.Reply(await _users.AddAsync(member.Value, access, actor));
                    break;
                }

                case "remove":
                    await responder.DeferAsync();
                    await responder.Reply(await _users.RemoveAsync(member.Value, actor));
                    break;

                case "suspend":
                    await responder.DeferAsync();
                    await responder.Reply(await _users.SuspendAsync(member.Value, actor));
                    break;

                case "unsuspend":
                    await responder.DeferAsync();
                    await responder.Reply(await _users.UnsuspendAsync(member.Value, actor));
                    break;

                case "access":
                {
                    var type = HandlerSupport.ParseType(interaction.GetOption<string>("type"));
                    var level = HandlerSupport.ParseLevel(interaction.GetOption<string>("level"));

                    await responder.Reply(type == null || level == null
                        ? ServiceResult.Fail(DiscordText.TypeNotOffered)
                        : _users.SetAccess(member.Value, type.Value, level.Value, actor, interaction.GetOption<string>("instance")));
                    break;
                }

                case "quota":
                {
                    var type = HandlerSupport.ParseType(interaction.GetOption<string>("type"));
                    var limit = interaction.GetInteger("limit") ?? 0;
                    var days = interaction.GetInteger("days") ?? 7;

                    await responder.Reply(type == null
                        ? ServiceResult.Fail(DiscordText.TypeNotOffered)
                        : _users.SetQuota(member.Value, type.Value, (int)limit, (int)days, actor));
                    break;
                }

                case "info":
                    await responder.Reply(_users.Info(member.Value));
                    break;

                case "mediaserver":
                    await responder.Reply(_users.LinkMediaServer(member.Value, MediaServerProvider.Plex, interaction.GetOption<string>("username"), actor));
                    break;
            }
        }

        private static void AddLevel(Dictionary<ContentType, AccessState> access, ContentType type, string value)
        {
            var level = HandlerSupport.ParseLevel(value);

            if (level.HasValue)
            {
                access[type] = level.Value;
            }
        }
    }
}
