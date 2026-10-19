namespace Requestarr.Core.Discord
{
    public static class DiscordLimits
    {
        public const int MessageLength = 2000;
        public const int ChoicesPerMenu = 25;
        public const int OptionLabelLength = 100;
        public const int ModalTitleLength = 45;
        public const int ChannelNameLength = 100;
        public const int RoleNameLength = 100;

        // Discord allows 50 channels per category and 250 roles per server. Requestarr stops
        // adding users a few roles short so the admin can still create roles by hand.
        public const int ChannelsPerCategory = 50;
        public const int RolesPerServer = 250;
        public const int RoleHeadroom = 5;

        public static string Truncate(string value, int max)
        {
            if (value == null || value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max - 1) + "…";
        }
    }
}
