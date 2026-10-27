# Upstream: Prowlarr

Requestarr's foundation is copied from [Prowlarr](https://github.com/Prowlarr/Prowlarr), which is licensed under GPL-3.0. Requestarr is licensed under GPL-3.0 too.

| | |
|---|---|
| Source | https://github.com/Prowlarr/Prowlarr, `develop` branch |
| Commit | `26a605e41f6156b959bc8f3ae5db4bec252106c5` (2026-10-18) |
| Imported in | commit `cb5838d` ("Import Prowlarr foundation at 26a605e…"). This is an unmodified copy, so `git diff cb5838d -- <path>` shows every Requestarr change to an upstream file. |
| .NET | Imported on net8.0 (Prowlarr's target at that commit). Requestarr moved to **net10.0** on 2026-10-19 and now diverges from upstream's target framework (see below). |

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
- the root packaging scripts `build.sh`, `test.sh` and `docs.sh` (2026-10-27). Requestarr builds with `dotnet` and its Dockerfile, runs tests with `dotnet test`, and publishes Docker images only, so nothing used them; they still targeted older SDKs and a macOS package folder Requestarr doesn't have. An upstream change to them doesn't apply: keep them deleted.

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
- Requestarr's backend analyzer checks are additions only: a block at the end of `.editorconfig` ("Requestarr additions"), the Sonar analyzer and two files (`src/Sonar.globalconfig`, `src/SonarLint.xml`) in `src/Directory.Build.props`. Upstream's `.editorconfig` sections are unchanged; merge upstream changes above the additions.
- The web app's lint configuration moved from `frontend/.eslintrc.js` to `frontend/eslint.config.js` (2026-10-27), because ESLint 10 dropped the old format and ESLint 8 gets no updates. Upstream's rules are in it unchanged (less `valid-jsdoc` and `require-jsdoc`, which ESLint 9 removed and upstream had off). Apply an upstream change to `.eslintrc.js` to the matching rule in `eslint.config.js` by hand; Requestarr's own checks are in the block at its end.

## Target framework divergence (.NET 10)

On 2026-10-19, Requestarr moved from net8.0 to net10.0 because .NET 8 support ends 2026-11-10. Upstream Prowlarr was still on net8.0 at the import commit. Expect these differences when merging upstream changes:

- Every `TargetFrameworks` is `net10.0` (`net10.0-windows` for the tray app). Keep Requestarr's value when an upstream hunk touches these lines.
- `global.json` targets SDK 10.0.100 with `rollForward: latestFeature`.
- Microsoft.* and System.* packages are on 10.0.x. These references were removed because .NET 10 includes them in the framework (NU1510): System.Text.Json, System.Text.Encoding.CodePages, System.Resources.Extensions, System.Private.Uri, System.Runtime.Loader, System.Security.Principal.Windows. Drop them again if an upstream change re-adds them.
- APIs obsolete in .NET 10 were replaced:
  - SSL certificate loading uses `X509CertificateLoader.LoadPkcs12FromFile` (Bootstrap and CertificateValidator).
  - Trusted proxies use `KnownIPNetworks` with `System.Net.IPNetwork` (ForwardedHeadersConfigurator).
  - The tray app overrides `OnFormClosing`.
- Third-party packages were not changed. All of them restore and build for net10.0 with no compatibility warnings (no NU1701), so none are held back.

## Pulling an upstream fix

1. Find the upstream commit and check which files it touches.
2. Apply it with path and name translation (`Prowlarr.Http` → `Requestarr.Http`, and so on), for example `git format-patch` from upstream plus `sed` on the patch.
3. Skip any hunk that touches removed domain code.
