# Development

## Local build

Prerequisites: .NET SDK 10 (`global.json` accepts 10.0.100 or any later 10.0 feature band), and Node 20 or later. There is no global yarn; `npx yarn@1.22.22` works.

```bash
npx --yes yarn@1.22.22 install --frozen-lockfile
npx --yes yarn@1.22.22 build --env production          # web UI -> _output/UI
dotnet build src/Requestarr.sln -c Debug -p:Platform=Windows
_output/net10.0/Requestarr.Console.exe -nobrowser -data=C:\path\to\data
```

On Linux or macOS, use `-p:Platform=Posix` and run `_output/net10.0/Requestarr`.

The app listens on http://localhost:9797.

## Tests

The Servarr suites run like `test.sh` does, minus integration and automation tests:

```bash
cd _tests/net10.0
dotnet test Requestarr.Api.V1.Test.dll Requestarr.Common.Test.dll Requestarr.Core.Test.dll Requestarr.Host.Test.dll \
  Requestarr.Libraries.Test.dll Requestarr.Update.Test.dll Requestarr.Windows.Test.dll \
  "--filter:Category!=ManualTest&Category!=LINUX&Category!=IntegrationTest&Category!=AutomationTest"
```

On Linux, swap `Requestarr.Windows.Test.dll` for `Requestarr.Mono.Test.dll` and `LINUX` for `WINDOWS`.

## Continuous integration

`.github/workflows/build.yml` runs on every push and pull request using standard GitHub-hosted runners. The `build` job is the single required status for `main`, which accepts changes only through a pull request with this check passing.

| Job | Runs |
|---|---|
| Backend (Linux) | solution build (`Platform=Posix`); test suites Api.V1, Common, Core, Host, Libraries, Mono, Update, excluding categories ManualTest, IntegrationTest, AutomationTest, WINDOWS |
| Backend (Windows) | solution build (`Platform=Windows`); test suites Api.V1, Common, Core, Host, Libraries, Update, Windows, excluding categories ManualTest, IntegrationTest, AutomationTest, LINUX |
| Frontend | `tsc --noEmit`, ESLint, Stylelint, production webpack build |
| Docker image | full `docker/Dockerfile` build (not pushed) |
| Changelog | pull requests only: `CHANGELOG.md` changed and its top version is higher than the base branch's (see [Changelog](#changelog)) |

Not run in CI:

- `Requestarr.Integration.Test`: needs a packaged app layout started as a separate process.
- `Requestarr.Automation.Test`: needs Selenium and a browser against a running app.
- `Requestarr.Benchmark.Test`: performance measurements, not pass or fail.
- Tests tagged `ManualTest`: these call live third-party services.

## Changelog

`CHANGELOG.md` at the repo root records every change, and Requestarr announces it: when it starts on a new version, the whole entry goes to the admin channel and the **For users** lines go to the members' announcements channel, word for word. So every pull request does this:

1. **Bump the version** in `src/Directory.Build.props` (`<AssemblyVersion>x.y.z.*</AssemblyVersion>`): the patch number for fixes and internal work, the minor number for new things people can do. Only `x.y.z` counts; the last part is the build number.
2. **Add a `## x.y.z` section at the top of `CHANGELOG.md`** with the lines that apply:

   ```markdown
   ## 0.1.1

   ### For users
   - You can now cancel a request after it was approved, until it starts downloading.

   ### For admins
   - The member card shows when someone last made a request.

   ### Internal
   - Faster tests for the request flow.
   ```

   - **For users**: what members can now do or will notice, in plain words a non-technical person understands. No product names of the servers behind Requestarr, no settings, no "fixed a bug in…". Leave the section out if nothing changes for them; then nothing is posted to users.
   - **For admins**: anything an admin sees or sets up.
   - **Internal**: everything else (tests, build, refactoring). Admins see it; users never do.

The checks: the `Changelog` job in CI fails a pull request that doesn't change `CHANGELOG.md` or whose top version isn't higher than `main`'s, and a unit test checks that the top version matches `<AssemblyVersion>`, that the file is embedded in the build, and that **For users** lines avoid technical words. The pull request template has the same checklist.

## Docker stack

One command builds Requestarr, starts it with dedicated Sonarr and Radarr test instances, waits for every health check, and runs the checks. On Windows, from a normal PowerShell tab in the repo folder:

```powershell
.\docker\dev.ps1 up
```

With Git Bash, Linux or macOS, `docker/dev.sh up` does the same. Both scripts have the same commands; the examples below use `dev.ps1`.

**What it needs.** Docker Desktop, running. Python 3 for `reset-uat` and `fake-download` (from https://www.python.org/downloads/, with "Add python.exe to PATH" ticked); `dev.ps1` checks for both and says what's missing. Don't run `dev.sh` by typing `bash` in PowerShell: that can start WSL's bash, which may not reach Docker Desktop or Python the same way. Use `dev.ps1`, or Git Bash.

**If PowerShell says running scripts is disabled.** That message comes from PowerShell before the script starts. Run this once, then `dev.ps1` works in every new tab:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

If you can't change the policy, `.\docker\dev.cmd` takes the same commands and runs `dev.ps1` for that one run whatever the policy is.

| Service | URL (localhost only) | Notes |
|---|---|---|
| Requestarr | http://localhost:9797 | provisioned from `docker/provisioning.yml` |
| Sonarr (test) | http://localhost:18989 | root folder `/data/tv`, no login (localhost-bound) |
| Radarr (test) | http://localhost:17878 | root folder `/data/movies`, no login (localhost-bound) |

Other commands:

- `check`: health and status checks, including whether dev fast votes are on.
- `reset-test-backends`: wipes the test Sonarr and Radarr back to empty and reconfigures them.
- `reset-uat`: a clean slate for testing a change. It deletes Requestarr's channels, categories, the members role and per-user roles in the Discord test server. It keeps the bot's role, the Requestarr Admin role and everything else. It also wipes the Requestarr database and the test backends, then starts Requestarr again. The bot recreates `#requestarr-admin` and its pinned panel, so the server is ready for testing. Fast votes are always off after it. Add `--stopped` to leave Requestarr stopped, for the live Discord test.
- `down`: stops the stack; data is kept. `up` starts it again.
- `logs requestarr`: follows the Requestarr logs (Ctrl+C to stop).
- `init`: creates `docker/secrets.env` (see Secrets below); `up` runs it.

### Helpers for testing by hand

The test Sonarr and Radarr have no indexer or download client, so nothing ever downloads, and removal votes take days. These helpers reach those states by hand. They only ever touch the dev stack's own containers (Compose project `requestarr-dev`).

| Command | What it does | What to expect in Discord |
|---|---|---|
| `stop-backend sonarr` (or `radarr`) | Stops only that test instance. | Within a minute Requestarr notices; after 3 minutes it posts a down alert in `#requestarr-admin`, new requests of that type say they're paused, and the instance waits in `/admin requests`. After 30 minutes the alert offers **Announce to users**. |
| `start-backend sonarr` (or `radarr`) | Starts it again. | Within a minute, "… is back after N minutes" in `#requestarr-admin` (if it had alerted), and requests work again. |
| `fake-download movie "Dune"` | Makes a movie that is already in the test Radarr look downloaded: a placeholder file (it takes no disk space) is handed to Radarr's own Manual Import, owned by its app user, exactly as a real download is imported. Radarr then sends its normal import webhook to Requestarr (the backup poll would find it too). Request the title in Discord first. | The request message is reposted as **Available**, mentioning the requester and followers; its feed card moves to the bottom showing Available, with **Delete**. |
| `fake-download tv "Severance" 1` | The same for every aired episode of that season in the test Sonarr. | **Partly available** while other requested seasons are missing; **Available** once every requested season is there. |
| `fast-votes on` | Dev only: new removal votes close after **5 minutes**, requesters have **5 minutes** to answer, reminded at **minute 3** (instead of 7 days, 7 days and day 5). Removals already open keep their times. | Within a minute, a **🧪 Dev: fast votes are on** notice in `#requestarr-admin`, naming the real settings, so nobody takes the minute timings for them. |
| `fast-votes off` / `fast-votes status` | Back to the real settings / says which is on. | **🧪 Dev: fast votes are off** in `#requestarr-admin`. |

Fast votes can't be on anywhere but the dev stack: they need a marker file in Requestarr's data folder (what `fast-votes on` creates) *and* the `REQUESTARR_DEV_STACK` variable that only `docker/compose.yaml` sets; any other install ignores the file. They stay on across `up`/`down` until you turn them off, and `reset-uat` always starts with them off.

Host ports can be changed with `REQUESTARR_PORT`, `SONARR_TEST_PORT`, and `RADARR_TEST_PORT` in `docker/secrets.env`. The stack is its own Compose project (`requestarr-dev`) with its own volumes. It never touches another deployment.

### Secrets

`init`, which `up` runs, creates `docker/secrets.env` from `docker/secrets.env.example`. It generates random local test keys and leaves Discord and Plex values for you to fill in. `docker/secrets.env` is gitignored. Never commit real values, and never put them in code, docs, or comms.

## Provisioning file

Requestarr reads a YAML provisioning file at every start. It uses `$REQUESTARR_PROVISIONING_FILE` if set, otherwise `provisioning.yml` in the data folder. Keys in the file win over UI changes, and keys left out are not touched. Secret values are `${VAR}` references to environment variables; `${VAR:-default}` supplies a default and `$${VAR}` is a literal. If the file is invalid, the app logs the error and reports a health-check error.

| Section | Status |
|---|---|
| `general` (instanceName, logLevel, urlBase, analyticsEnabled, launchBrowser, theme) | applied |
| `auth` (method, required, username, password, apiKey) | applied |
| `discord` (botToken, applicationId, guildId, admin role and channel IDs) | validated; applied in Phase 1 |
| `backends.sonarr` / `radarr` / `lidarr` (lists, any number of instances) | validated; applied in Phase 1 |
| `mediaServers.plex` / `jellyfin` | validated; applied in a later phase |

See `docker/provisioning.yml` for a complete example.

## Discord interaction harness

Handlers implement `IInteractionHandler` (`src/NzbDrone.Core/Requestarr/Discord/Interactions`). They see only `DiscordInteraction` and `IInteractionResponder`, never Discord.Net types. Tests drive them through the real dispatcher with no Discord connection:

```csharp
var harness = new InteractionHarness(handlerUnderTest);
var run = await harness.SendAsync(FakeInteraction.SlashCommand("request movie").WithOption("query", "Dune").By(userId).InChannel(channelId));
run.LastReply.Content.Should().Contain("Dune");

await harness.SendAsync(FakeInteraction.Button("request-approve:42").By(adminId).WithRoles(adminRoleId));
```

See `src/NzbDrone.Core.Test/Requestarr/Discord/Harness`. `FakeRadarr` and `FakeSonarr` stand in for the backends in the same scenarios.

Live checks are tagged `ManualTest`, so they never run in CI. They read their keys from `docker/secrets.env`.

They only ever onboard, suspend or request as the members named in `DISCORD_TEST_MEMBERS` (comma-separated Discord usernames of your test accounts). Everyone else in the test server is left alone, so people who join it to try Requestarr are never touched. Set it in your shell, or in `docker/secrets.env`; with no named member in the server, the checks that need one are skipped. `LiveAnnouncementsFixture` shortens the outage thresholds to 15 and 30 seconds; set `LIVE_REAL_THRESHOLDS=1` to wait the real 3 and 30 minutes.

```bash
set -a; . docker/secrets.env; set +a
export DISCORD_TEST_MEMBERS=yourtestaccount
cd _tests/net10.0
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveDiscordFixture" --logger "console;verbosity=detailed"
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveBackendFixture" --logger "console;verbosity=detailed"
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveUatReadBackFixture" --logger "console;verbosity=detailed"
```

For each change, test in this order:

1. `.\docker\dev.ps1 reset-uat --stopped` (or `docker/dev.sh reset-uat --stopped`)
2. Run the Discord live check. It needs the dev stack's Requestarr stopped.
3. Merge, then run `.\docker\dev.ps1 reset-uat` (or `docker/dev.sh reset-uat`) from `main`. The server is clean and has the admin channel and panel.
4. Run the UAT read-back.

- **Discord:** checks the admin role, the registered commands, onboarding, permission read-back and the pinned panels in the test server. It then deletes what it created. It never onboards a member who already has a request channel.
- **UAT read-back:** reads the running stack's registered commands and pinned panels over REST. It changes nothing.
- **Backends:** requests a movie and a show into the dev stack's test Radarr and Sonarr only, and reads back what they stored. The titles are left there. Automating Discord user accounts is not allowed. End-to-end checks use the bot's real effects in the dedicated test server, plus a short manual pass in the Discord app.
