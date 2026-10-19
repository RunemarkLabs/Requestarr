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

Not run in CI:

- `Requestarr.Integration.Test`: needs a packaged app layout started as a separate process.
- `Requestarr.Automation.Test`: needs Selenium and a browser against a running app.
- `Requestarr.Benchmark.Test`: performance measurements, not pass or fail.
- Tests tagged `ManualTest`: these call live third-party services.

## Docker stack

One command builds Requestarr, starts it with dedicated Sonarr and Radarr test instances, waits for every health check, and runs the checks:

```bash
docker/dev.sh up
```

| Service | URL (localhost only) | Notes |
|---|---|---|
| Requestarr | http://localhost:9797 | provisioned from `docker/provisioning.yml` |
| Sonarr (test) | http://localhost:18989 | root folder `/data/tv`, no login (localhost-bound) |
| Radarr (test) | http://localhost:17878 | root folder `/data/movies`, no login (localhost-bound) |

Other commands:

- `docker/dev.sh check`: health and status checks.
- `docker/dev.sh reset-test-backends`: wipes the test Sonarr and Radarr back to empty and reconfigures them.
- `docker/dev.sh reset-uat`: a clean slate for testing a change. It deletes Requestarr's channels, categories and per-user roles in the Discord test server. It keeps the bot's role, the Requestarr Admin role and everything else. It also wipes the Requestarr database and the test backends, then starts Requestarr again. The bot recreates `#requestarr-admin` and its pinned panel, so the server is ready for testing. Add `--stopped` to leave Requestarr stopped, for the live Discord test.
- `docker/dev.sh down`: stops the stack; data is kept.
- `docker/dev.sh logs requestarr`: follows the Requestarr logs.

Host ports can be changed with `REQUESTARR_PORT`, `SONARR_TEST_PORT`, and `RADARR_TEST_PORT` in `docker/secrets.env`. The stack is its own Compose project (`requestarr-dev`) with its own volumes. It never touches another deployment.

### Secrets

`docker/dev.sh init`, which `up` runs, creates `docker/secrets.env` from `docker/secrets.env.example`. It generates random local test keys and leaves Discord and Plex values for you to fill in. `docker/secrets.env` is gitignored. Never commit real values, and never put them in code, docs, or comms.

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

Three live checks are tagged `ManualTest`, so they never run in CI. Both read their keys from `docker/secrets.env`:

```bash
set -a; . docker/secrets.env; set +a
cd _tests/net10.0
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveDiscordFixture" --logger "console;verbosity=detailed"
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveBackendFixture" --logger "console;verbosity=detailed"
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveUatReadBackFixture" --logger "console;verbosity=detailed"
```

For each change, test in this order:

1. `docker/dev.sh reset-uat --stopped`
2. Run the Discord live check. It needs the dev stack's Requestarr stopped.
3. Merge, then run `docker/dev.sh reset-uat` from `main`. The server is clean and has the admin channel and panel.
4. Run the UAT read-back.

- **Discord:** checks the admin role, the registered commands, onboarding, permission read-back and the pinned panels in the test server. It then deletes what it created. It never onboards a member who already has a request channel.
- **UAT read-back:** reads the running stack's registered commands and pinned panels over REST. It changes nothing.
- **Backends:** requests a movie and a show into the dev stack's test Radarr and Sonarr only, and reads back what they stored. The titles are left there. Automating Discord user accounts is not allowed. End-to-end checks use the bot's real effects in the dedicated test server, plus a short manual pass in the Discord app.
