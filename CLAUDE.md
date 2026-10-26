# requestarr

Repo-shared Claude rules for `requestarr`, part of the requestarr Claude
Code workspace.

## Stack

.NET backend and React web portal on Servarr infrastructure copied from Prowlarr; SQLite; Discord.Net (gateway connection). Scope and design: `../../plans/active/multi-user-discord-admin/plan.md`.

## Conventions

- Branches: every pull request targets `dev` (merged after the `build` check); `qa` moves only when the owner says "promote to QA" (`./release.sh promote-qa`), `staging` only on "QA passed" (`./release.sh promote-staging`, after the full suite and every live check), `main` only on "ship it". A fix found in QA or staging is a pull request into that branch, merged back down in the same task (`./release.sh merge-back`), and `./release.sh drift` must pass. Never push to `dev`, `qa`, `staging` or `main` directly; the one exception is the fast-forward a promotion makes while its pull request is open and green, which GitHub records as merging that pull request. A push to `staging` publishes the image (`ghcr.io/runemarklabs/requestarr:staging`). Details: `docs/development.md#release-flow`.
- Stacks: automated and live testing use only the dev stack and the dev Discord server (`.\docker\dev.ps1 -Stack dev ...`, `reset-dev`, `DEV_DISCORD_*`, `LiveDev` in tests). The QA stack and the QA server (the default, `reset-qa`) are the owner's manual QA: touch them only to run `qa` after a promotion or a QA fix.
- Branch naming: one branch per task, named after the task slug, from `dev` (from `qa` or `staging` for a fix there).
- Commit style: ...
- Test expectations: ...
- Namespaces: copied Servarr foundation keeps `NzbDrone.*`; all new code is `Requestarr.*`.
- Changelog (every pull request): bump `<AssemblyVersion>` in `src/Directory.Build.props` and add a `## x.y.z (YYYY-MM-DD)` section (the day it was made) at the top of `CHANGELOG.md` with only **For users** and **For admins**, in plain words with a bold lead per line, no code-style text, no placeholders, empty sections left out (work neither sees is not listed). Requestarr posts it in Discord as a lasting record. CI's Changelog job (`.github/changelog_check.py`) and a unit test enforce it. Details: `docs/development.md#changelog`.

- Versions (every pull request; details: `docs/development.md#versions`, enforced by `.github/version_rules.py` in the Changelog check):
  1. Patch = dev builds: every pull request into `dev` (dependency updates too) takes the next patch on `dev`'s line.
  2. Minor = QA cycles: a promotion to QA takes `dev`'s version as it is; the next pull request into `dev` starts the next minor at .0.
  3. Staging adds a beta pre-release: published as `x.y.z-beta.N`, N counting the line's publishes to staging (git tags `vx.y.z-beta.N`).
  4. Production removes it: "ship it" releases staging's code as `x.y.z`.
  5. Majors: 1.0.0 is the first public release; after that a major only when admins must act on upgrade (a For admins line led by **Before you upgrade:**), and the banner and reply say so. Until 1.0, never.
  A fix into `qa` or `staging` takes the next free patch on that branch's own line. `./release.sh next-version [dev|qa|staging]` prints the number.

- QA checklist (every pull request): add this version's test items at the top of `QA-CHECKLIST.md` under the same `## x.y.z (YYYY-MM-DD)` heading as the changelog: `- **Name:** what to do. You should see what a pass looks like.`, in plain words an admin can follow in Discord; a version nobody can test by hand has the heading only. CI (`.github/checklist_check.py`) enforces it. Details: `docs/development.md#qa-checklist`.
- QA gate (start of every goal, before the task itself): run `./release.sh qa-gate`. When it's clear (QA's checklist complete, no item failing, no bug report from QA open), promote QA to staging first, through the normal release flow (`./release.sh promote-staging <qa's version> "the QA checklist, completed by <who>"`, after the full suite and every live check), and say so in the reply and banner. When it's blocked, don't promote, and say what's left in one line. Staging moves otherwise only on the owner's explicit word, with `--owner-override "<their words>"`. If the QA stack isn't running or the gate can't be read, say so and don't promote.

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
