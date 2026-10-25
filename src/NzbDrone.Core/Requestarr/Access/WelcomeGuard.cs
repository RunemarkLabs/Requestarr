using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Access
{
    // One row per person who has clicked a welcome button in a way that couldn't do anything (a request already
    // open, a cooldown, being told their status again). Rapid clicking only rewrites this row: the recent click
    // times are capped at the long cooldown's count, so the row never grows.
    public class WelcomeGuard : ModelBase
    {
        public string DiscordUserId { get; set; }

        // Counted clicks inside the counting window, as UTC ticks: "638...,638...".
        public string RecentClicks { get; set; }
        public DateTime? CooldownUntil { get; set; }

        // The click that started the long cooldown, and the counted clicks since; enough of those within the
        // block window blocks the person.
        public DateTime? LongCooldownStarted { get; set; }
        public int ClicksSinceLong { get; set; }

        // Set while the person is blocked; a block lasts until the admin lifts it.
        public DateTime? Blocked { get; set; }

        // The admins' notice about this person's block, in the admin channel: there's only ever one per person.
        public string NoticeMessageId { get; set; }

        public List<DateTime> Clicks() =>
            (RecentClicks ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : (DateTime?)null)
                .Where(t => t.HasValue)
                .Select(t => t.Value)
                .ToList();

        public void SetClicks(IEnumerable<DateTime> clicks) =>
            RecentClicks = string.Join(",", clicks.Select(c => c.Ticks.ToString(CultureInfo.InvariantCulture)));
    }

    public interface IWelcomeGuardRepository : IBasicRepository<WelcomeGuard>
    {
        WelcomeGuard Find(string discordUserId);
        List<WelcomeGuard> AllBlocked();
    }

    public class WelcomeGuardRepository : BasicRepository<WelcomeGuard>, IWelcomeGuardRepository
    {
        public WelcomeGuardRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public WelcomeGuard Find(string discordUserId) => Query(g => g.DiscordUserId == discordUserId).SingleOrDefault();

        public List<WelcomeGuard> AllBlocked() => Query(g => g.Blocked != null);
    }

    public enum GuardOutcome
    {
        // Nothing changed: answer with the status.
        Counted,

        // This click started the short or the long cooldown.
        ShortCooldown,
        LongCooldown,

        // This click blocked the person.
        Blocked
    }

    // The escalation, kept apart from Discord and the database so it can be checked click by click.
    public static class SpamRules
    {
        // Counts one click that couldn't do anything, at `now`, and says what it led to.
        public static GuardOutcome Count(WelcomeGuard guard, DateTime now, IAccessSettings settings)
        {
            var window = TimeSpan.FromMinutes(settings.SpamWindowMinutes);
            var clicks = guard.Clicks().Where(c => now - c < window).ToList();
            clicks.Add(now);
            guard.SetClicks(clicks.TakeLast(Math.Max(settings.LongCooldownClicks, 1)));

            // After the long cooldown started, enough further clicks within the block window block the person.
            if (guard.LongCooldownStarted is { } started && now - started <= TimeSpan.FromHours(settings.BlockWithinHours))
            {
                guard.ClicksSinceLong++;

                if (guard.ClicksSinceLong >= settings.BlockClicks)
                {
                    guard.Blocked = now;
                    return GuardOutcome.Blocked;
                }

                return GuardOutcome.Counted;
            }

            guard.LongCooldownStarted = null;
            guard.ClicksSinceLong = 0;

            if (clicks.Count >= settings.LongCooldownClicks)
            {
                guard.LongCooldownStarted = now;
                guard.CooldownUntil = now + TimeSpan.FromHours(settings.LongCooldownHours);
                guard.SetClicks(Array.Empty<DateTime>());
                return GuardOutcome.LongCooldown;
            }

            if (clicks.Count == settings.ShortCooldownClicks)
            {
                guard.CooldownUntil = now + TimeSpan.FromMinutes(settings.ShortCooldownMinutes);
                return GuardOutcome.ShortCooldown;
            }

            return GuardOutcome.Counted;
        }

        public static TimeSpan? CooldownLeft(WelcomeGuard guard, DateTime now) =>
            guard?.CooldownUntil is { } until && until > now ? until - now : null;
    }
}
