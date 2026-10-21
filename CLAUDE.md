# requestarr

Repo-shared Claude rules for `requestarr`, part of the requestarr Claude
Code workspace.

## Stack

.NET backend and React web portal on Servarr infrastructure copied from Prowlarr; SQLite; Discord.Net (gateway connection). Scope and design: `../../plans/active/multi-user-discord-admin/plan.md`.

## Conventions

- Branch naming: ...
- Commit style: ...
- Test expectations: ...
- Namespaces: copied Servarr foundation keeps `NzbDrone.*`; all new code is `Requestarr.*`.
- Changelog (every pull request): bump `<AssemblyVersion>` in `src/Directory.Build.props` and add a `## x.y.z` section at the top of `CHANGELOG.md` with **For users** (plain words, only what members notice; posted to them verbatim on upgrade), **For admins** and **Internal**. CI's Changelog job and a unit test enforce it. Details: `docs/development.md#changelog`.

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
