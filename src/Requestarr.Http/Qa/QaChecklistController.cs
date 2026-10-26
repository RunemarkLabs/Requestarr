using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Qa;

namespace Requestarr.Http.Qa
{
    // What Claude Code reads from the QA stack at the start of every goal (docker/qa-gate.py): the QA checklist's state
    // and the bug reports this server sent. Needs the API key, like the rest of the API.
    public class QaChecklistController : Controller
    {
        private readonly IQaChecklistService _checklist;
        private readonly IBugReportRepository _reports;

        public QaChecklistController(IQaChecklistService checklist, IBugReportRepository reports)
        {
            _checklist = checklist;
            _reports = reports;
        }

        [HttpGet("/api/v1/qa/checklist")]
        [Produces("application/json")]
        public IActionResult Get()
        {
            var state = _checklist.State();
            var all = _reports.All().OrderBy(r => r.Id).ToList();
            var reports = all
                .Where(r => state.Versions.Contains(r.Version))
                .Select(r => new { r.Id, r.Component, r.Version, r.Environment, r.Created, r.ChecklistItemKey, Filing = r.Filing.ToString(), r.FilingProblem, r.IssueNumber, r.IssueUrl })
                .ToList();

            // Every report from this server that isn't on GitHub, whatever its version: each is an open bug the gate
            // can't see on GitHub, so it blocks until it's filed (and then closed there).
            var unfiled = all
                .Where(r => r.Filing != BugReportFiling.Filed)
                .Select(r => new { r.Id, r.Component, r.Version, r.Created, Filing = r.Filing.ToString(), r.FilingProblem, What = r.WhatHappened })
                .ToList();

            return Json(new { checklist = state, bugReports = reports, unfiledReports = unfiled });
        }
    }
}
