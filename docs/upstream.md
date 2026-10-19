# Upstream: Prowlarr

Requestarr's foundation is copied from [Prowlarr](https://github.com/Prowlarr/Prowlarr), which is licensed under GPL-3.0. Requestarr is licensed under GPL-3.0 too.

| | |
|---|---|
| Source | https://github.com/Prowlarr/Prowlarr, `develop` branch |
| Commit | `26a605e41f6156b959bc8f3ae5db4bec252106c5` (2026-10-18) |
| Imported in | commit `cb5838d` ("Import Prowlarr foundation at 26a605e…"). This is an unmodified copy, so `git diff cb5838d -- <path>` shows every Requestarr change to an upstream file. |
| .NET | net8.0, as Prowlarr targets at that commit. `global.json` pins SDK 8.0.421 with `rollForward: latestMajor`. |

## What was kept

These are shared Servarr infrastructure, kept under their `NzbDrone.*` namespaces so upstream fixes apply cleanly:

- database, migrations framework, Dapper mapping
- configuration (`config.xml` and the `Config` table)
- DI wiring (DryIoc), HTTP API base, SignalR
- authentication (API key, Basic, Forms, External)
- health checks, scheduled tasks, the command and event system, housekeeping, backups
- logging, the provider pattern (ThingiProvider), tags, custom filters
- the React web shell: Settings (General, UI, Tags, Development) and System (Status, Tasks, Backup, Updates, Events, Logs)

## What was removed

The Prowlarr domain:

- indexers (including Cardigann definitions), indexer proxies, indexer search, versions, and stats
- download clients
- applications (app sync) and app sync profiles
- history
- notifications. Requestarr will add its own notification needs later.
- their API controllers, frontend pages, store slices, and tests
- Prowlarr's 44 migrations, replaced by one initial migration (`001_initial_setup.cs`). It holds the Servarr infrastructure tables in their final upstream shape plus the Requestarr data model.
- calls to Servarr-hosted services: Sentry error reporting (backend and frontend), the update and notification service, the system-time check, and Piwik. Requestarr reports no updates until it has its own release feed.

## Renames

- Projects, assemblies, and the solution: `Prowlarr.*` → `Requestarr.*`. The `Prowlarr.Http` and `Prowlarr.Api.V1` namespaces became `Requestarr.Http` and `Requestarr.Api.V1`, mirroring how each *arr names these. `NzbDrone.*` namespaces are unchanged.
- The frontend global `window.Prowlarr` became `window.Requestarr`, and the client header became `X-Requestarr-Client`.
- Default port 9797. Translation values say Requestarr, but translation keys are unchanged, so upstream locale updates still merge.

## Requestarr-specific code

New code uses `Requestarr.*` namespaces in `Requestarr/` folders inside the existing projects, for example `src/NzbDrone.Core/Requestarr/Provisioning`. It lives inside those projects so DI scanning, table mapping, and provider-settings registration work without changes to upstream files.

Small upstream-file changes worth knowing when merging:

- `ConfigFileProvider.SetApiKey`, used by provisioning
- `AppFolderFactory.Register` creates the app data folder before the legacy DB migration (it failed on fresh installs)
- `HealthCheckReason.ProvisioningFailed`
- `TagService` and `CleanupUnusedTags` only consider the `Backends` table

## Pulling an upstream fix

1. Find the upstream commit and check which files it touches.
2. Apply it with path and name translation (`Prowlarr.Http` → `Requestarr.Http`, and so on), for example `git format-patch` from upstream plus `sed` on the patch.
3. Skip any hunk that touches removed domain code.
