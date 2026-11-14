# requestarr

Repo-shared Claude rules for `requestarr`, part of the requestarr Claude
Code workspace.

## Stack

.NET backend and React web portal on Servarr infrastructure copied from Prowlarr; SQLite; Discord.Net (gateway connection). Scope and design: `../../plans/active/multi-user-discord-admin/plan.md`.

## Conventions

- Branches: every pull request targets `dev` (merged after the `build` check); `qa` moves only when the owner says "promote to QA" (`./release.sh promote-qa`), `staging` only on "QA passed" (`./release.sh promote-staging`, after the full suite and every live check), `main` only on "ship it" (`./release.sh ship-it`, which retags staging's image and never rebuilds). A fix found in QA or staging is a pull request into that branch, merged back down in the same task (`./release.sh merge-back`), and `./release.sh drift` must pass. Never push to `dev`, `qa`, `staging` or `main` directly; the one exception is the fast-forward a promotion makes while its pull request is open and green, which GitHub records as merging that pull request. A push to `staging` publishes the image (`ghcr.io/requestarr/requestarr:staging`). Details: `docs/development.md#release-flow`.
- Stacks: automated and live testing use only the dev stack and the dev Discord server (`.\docker\dev.ps1 -Stack dev ...`, `reset-dev`, `DEV_DISCORD_*`, `LiveDev` in tests). The QA stack and the QA server (the default, `reset-qa`) are the owner's manual QA: touch them only to run `qa` after a promotion or a QA fix, with `up`, which keeps QA's checklist results and bug reports (`reset-qa` only with the owner's yes).
- Branch naming: one branch per task, named after the task slug, from `dev` (from `qa` or `staging` for a fix there).
- Commit style: ...
- Test expectations: ...
- Namespaces: copied Servarr foundation keeps `NzbDrone.*`; all new code is `Requestarr.*`.
- Changelog (every pull request): add its lines under `## Unreleased` at the top of `CHANGELOG.md` with only **For users** and **For admins**; Unreleased is the rollup since the last build testers got, so a change to something already described there rewrites that line to where it ended up (a change to something testers already have is a new line), in plain words with a bold lead per line, no code-style text, no placeholders, empty sections left out (work neither sees is not listed); a pull request that changes nothing either sees adds no lines and says `No changelog: <why>` on a line of its own in its description. Numbered versions are never edited. Requestarr posts it in Discord as a lasting record. CI's Changelog job (`.github/changelog_check.py`, `./release.sh check-changelog`) and a unit test enforce it. Details: `docs/development.md#changelog`.

- Versions (details: `docs/development.md#versions`; `.github/build_numbers.py` is the only thing that takes a number):
  1. MAJOR.MINOR.BUILD, BUILD a running four-digit number: `0.7.0004`. Pull requests take no number and nothing in the source holds one; the build is stamped when it's built.
  2. BUILD is taken only when a build leaves for testers: the promotion to staging (its release commit turns Unreleased into `## 0.7.0004 (date)`) or a fix into staging (its own new section with the next number). Recorded as the git tag `v0.7.0004` on the commit built. Never reused: a bad build keeps its number, the fix takes the next.
  3. Dev and QA builds show the next number and their commit: `0.7.0004-dev.4f2a9c1`, `0.7.0004-qa.4f2a9c1`. No `-beta` label: the beta server is told apart by its BETA strip.
  4. "Ship it" never rebuilds: `./release.sh ship-it` moves `main` to staging's commit and the image staging published gets the `0.7` and `latest` tags. The next staging build is a minor up (0.8.0005).
  5. MAJOR: 1.0 at the version 1 scope on the owner's word (`promote-staging --major "<their words>"`); after that when admins must act on upgrade (a For admins line led by **Before you upgrade:**), and the banner and reply say so. MINOR goes to 0, BUILD starts again (1.0.0001).
  Versions up to 0.6.0 keep their `x.y.z` numbers. `./release.sh next-version` prints the next number.

- Clean code (every change; details and limits: `docs/development.md#clean-code`): no unused code, one thing per function (cognitive complexity 15, nesting 4; C#: 60 lines a method; web: 4 parameters, 50 lines in `.ts`, 80 in `.tsx`/`.js`), clear names, no nested ternaries, no commented-out code, comments that say why, named limits and identifiers. Readable over clever: follow the surrounding style, never split code just to meet a number. Limits are review triggers: simplify, or explain on the line. A suppression has a reason on the same line; never add to `frontend/eslint-suppressions.json` or `frontend/knip-baseline.json`, never turn a rule down. A C# file Requestarr adds outside a `Requestarr` folder joins the list in `.editorconfig`'s "Requestarr additions". CI enforces all of it.

- QA checklist (every pull request someone can test by hand): add its test items under `## Unreleased` at the top of `QA-CHECKLIST.md` (or rewrite the Unreleased item that already tests what changed): `- **Name:** what to do. You should see what a pass looks like.`, in plain words an admin can follow in Discord; a change nobody can test by hand adds none. The staging promotion numbers it with the changelog. CI (`.github/checklist_check.py`) enforces it. Details: `docs/development.md#qa-checklist`.
- QA gate (start of every goal, before the task itself): run `./release.sh qa-gate`. When it's clear (QA's checklist complete, no item failing, no bug report from QA open), promote QA to staging first, through the normal release flow (`./release.sh promote-staging "the QA checklist, completed by <who>"`, after the full suite and every live check), and say so in the reply and banner. When it's blocked, don't promote, and say what's left in one line. Staging moves otherwise only on the owner's explicit word, with `--owner-override "<their words>"`. If the QA stack isn't running or the gate can't be read, say so and don't promote. If the gate says HELD (the owner's hold, `./release.sh hold-staging`), don't promote and quote the hold in one line; it lifts only on the owner's word.

- Dependency updates: Dependabot's pull requests go into `dev` (the default branch); patch and minor merge on their own, major ones are labelled "needs review" and wait for a normal task (`docs/development.md#dependency-updates`). At the end of every task, list security fixes merged into `dev` that `qa` doesn't have yet (`gh pr list --base dev --state merged --label security`) and name them in the banner's "Needs you" line, so the owner can promote sooner; mention open "needs review" updates in the reply comm.

## Gotchas

- License is GPL-3.0 because Servarr code is GPL-3.0. Keep Servarr copyright headers on copied files; do not add code under incompatible licenses.
- Target the .NET version Prowlarr targets at copy time, not .NET 6.
- Never commit personal environment configuration (hardware, ports, image tags, indexer or download client details).

## Subfolder rules

Each subfolder may have its own `CLAUDE.md` with scoped rules. Those
override conventions here when Claude is working inside the subfolder.

<!--
  Deployed from RuneSmith marketplace.
  Copyright 2026 Runemark Labs. Licensed under Apache-2.0.
  Source: https://github.com/runemarklabs/runesmith
-->
