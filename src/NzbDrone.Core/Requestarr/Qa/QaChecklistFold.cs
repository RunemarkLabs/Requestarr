using System.Collections.Generic;

namespace Requestarr.Core.Qa
{
    // The fold of November 14, 2026: the items for 0.4.3 to 0.6.0, which never reached the beta testers, became one
    // Unreleased list (QA-CHECKLIST.md), overlapping items merged and outdated ones rewritten. An item there keeps a pass
    // when every item it came from (keyed by version and name, as results were recorded then) had passed and it tests the
    // same thing. Items rewritten because what they test changed ("Update cards"), and new ones, aren't here: they start
    // untested. Each entry is a folded item's key, its name and wording, so rewording it later starts it untested again.
    public static class QaChecklistFold
    {
        public static readonly IReadOnlyDictionary<string, string[]> EarlierKeys = new Dictionary<string, string[]>
        {
            ["read-changes.412ba92d"] = new[] { "0.6.0/read-changes-unchanged" },
            ["web-app-logo.548f53ad"] = new[] { "0.4.10/dark-theme-icons", "0.4.10/full-height-logo", "0.4.11/logo-on-the-purple-header", "0.4.11/charcoal-arrow" },
            ["sign-in-logo.4e2016a4"] = new[] { "0.4.13/sign-in-logo" },
            ["loading-screen.c897b6a7"] = new[] { "0.4.13/loading-screen" },
            ["bot-picture-and-banner.439f6f9d"] = new[] { "0.4.12/bot-picture-and-banner" },
            ["project-links.8fa2ac72"] = new[] { "0.5.6/project-links" },
            ["the-qa-checklist.9efe75e0"] = new[] { "0.4.15/test-checklist", "0.4.16/qa-group" },
            ["passing-an-item.9f83bbbd"] = new[] { "0.4.15/passing-an-item", "0.4.16/passes-without-errors" },
            ["failing-an-item.0cad33fd"] = new[] { "0.4.15/failing-an-item", "0.4.16/failing-in-a-row" },
            ["report-a-problem.3aa33284"] = new[] { "0.4.15/report-a-problem", "0.4.16/bug-reports-channel-order", "0.5.6/bug-report-at-the-new-home" },
            ["report-limit.13e91dd1"] = new[] { "0.4.15/report-limit", "0.4.16/member-limit-still-holds" },
            ["why-a-report-isn-t-on-github.64e0be4a"] = new[] { "0.4.16/why-a-report-isn-t-on-github" }
        };
    }
}
