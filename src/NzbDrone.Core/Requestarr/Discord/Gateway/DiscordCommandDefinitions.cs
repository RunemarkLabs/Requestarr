using System.Collections.Generic;
using System.Linq;
using Discord;

namespace Requestarr.Core.Discord.Gateway
{
    // Exactly three commands, registered on the one configured server (registering replaces
    // everything, so older commands disappear):
    //   /request {type} {query}  for everyone; type is autocompleted from the caller's access.
    //   /help                    user help, for everyone.
    //   /admin user|request|help Manage Server default permission, so non-admins never see it.
    // Visibility is cosmetic: Requestarr re-checks admin and channel on every use.
    public static class DiscordCommandDefinitions
    {
        public static readonly IReadOnlyList<string> AdminOnly = new[] { "admin" };

        public static IReadOnlyList<ApplicationCommandProperties> Build()
        {
            var request = new SlashCommandBuilder()
                .WithName("request")
                .WithDescription(DiscordText.Commands.RequestOne)
                .WithContextTypes(InteractionContextType.Guild)
                .AddOption(new SlashCommandOptionBuilder().WithName("type").WithDescription(DiscordText.Commands.OptionRequestType)
                    .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithAutocomplete(true))
                .AddOption(new SlashCommandOptionBuilder().WithName("query").WithDescription(DiscordText.Commands.OptionRequestQuery)
                    .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithMaxLength(100));

            var help = new SlashCommandBuilder()
                .WithName("help")
                .WithDescription(DiscordText.Commands.Help)
                .WithContextTypes(InteractionContextType.Guild);

            // Three admin commands; everything else is a button, menu or form on the cards they open
            // and on the admin panel, feed and approval cards.
            var admin = new SlashCommandBuilder()
                .WithName("admin")
                .WithDescription(DiscordText.Commands.Admin)
                .WithContextTypes(InteractionContextType.Guild)
                .WithDefaultMemberPermissions(GuildPermission.ManageGuild)
                .AddOption(Sub("user", DiscordText.Commands.AdminUser).AddOption(Member()))
                .AddOption(Sub("request", DiscordText.Commands.AdminRequest)
                    .AddOption(new SlashCommandOptionBuilder().WithName("request").WithDescription(DiscordText.Commands.OptionRequestSearch)
                        .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithAutocomplete(true).WithMaxLength(100)))
                .AddOption(Sub("help", DiscordText.Commands.AdminHelp));

            return new ApplicationCommandProperties[] { request.Build(), help.Build(), admin.Build() };
        }

        public record CommandLeaf(string Path, string Description, IReadOnlyList<(string Name, bool Required)> Options)
        {
            // "/admin user quota member type limit [days]"
            public string Usage => "/" + Path + string.Concat(Options.Select(o => o.Required ? $" {o.Name}" : $" [{o.Name}]"));
        }

        // Every command and subcommand a user can run, as "request", "admin user add", ...
        public static IReadOnlyList<CommandLeaf> Leaves()
        {
            var leaves = new List<CommandLeaf>();

            foreach (var command in Build().OfType<SlashCommandProperties>())
            {
                Walk(command.Name.Value, command.Description.Value, command.Options.IsSpecified ? command.Options.Value : null, leaves);
            }

            return leaves;
        }

        private static void Walk(string path, string description, IEnumerable<ApplicationCommandOptionProperties> options, List<CommandLeaf> leaves)
        {
            var list = options?.ToList() ?? new List<ApplicationCommandOptionProperties>();
            var nested = list.Where(o => o.Type == ApplicationCommandOptionType.SubCommand || o.Type == ApplicationCommandOptionType.SubCommandGroup).ToList();

            if (!nested.Any())
            {
                leaves.Add(new CommandLeaf(path, description, list.Select(o => (o.Name, o.IsRequired ?? false)).ToList()));
                return;
            }

            foreach (var option in nested)
            {
                Walk($"{path} {option.Name}", option.Description, option.Options, leaves);
            }
        }

        private static SlashCommandOptionBuilder Sub(string name, string description) =>
            new SlashCommandOptionBuilder().WithName(name).WithDescription(description).WithType(ApplicationCommandOptionType.SubCommand);

        private static SlashCommandOptionBuilder Member() =>
            new SlashCommandOptionBuilder().WithName("member").WithDescription(DiscordText.Commands.OptionMember).WithType(ApplicationCommandOptionType.User).WithRequired(true);
    }
}
