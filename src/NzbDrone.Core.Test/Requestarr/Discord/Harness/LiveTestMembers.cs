using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using NUnit.Framework;

namespace Requestarr.Core.Test.Discord.Harness
{
    // Live fixtures only ever onboard, suspend or request as the Discord members named in
    // DISCORD_TEST_MEMBERS (comma-separated usernames, set where the live tests are run, not in the repo)
    // who are in the dev server (LiveDev). Everyone else is left alone.
    public static class LiveTestMembers
    {
        public const string Variable = "DISCORD_TEST_MEMBERS";

        public static IReadOnlyList<string> Names =>
            (Environment.GetEnvironmentVariable(Variable) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // The named members found in the server, in the order named; ignores the test when there are none.
        public static List<T> Pick<T>(IEnumerable<T> members)
            where T : IUser
        {
            var all = members.Where(m => !m.IsBot).ToList();
            var picked = Names.Select(n => all.FirstOrDefault(m => string.Equals(m.Username, n, StringComparison.OrdinalIgnoreCase))).Where(m => m != null).ToList();

            if (picked.Count == 0)
            {
                Assert.Ignore($"{Variable} names no member of the test server");
            }

            return picked;
        }
    }
}
