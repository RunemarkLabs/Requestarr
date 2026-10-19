# Development

## Local build

Prerequisites: .NET SDK 8 or later, and Node 20 or later. There is no global yarn; `npx yarn@1.22.22` works.

```bash
npx --yes yarn@1.22.22 install --frozen-lockfile
npx --yes yarn@1.22.22 build --env production          # web UI -> _output/UI
dotnet build src/Requestarr.sln -c Debug -p:Platform=Windows -p:TargetLatestRuntimePatch=false
_output/net8.0/Requestarr.Console.exe -nobrowser -data=C:\path\to\data
```

On Linux or macOS, use `-p:Platform=Posix` and run `_output/net8.0/Requestarr`.

`TargetLatestRuntimePatch=false` lets local builds run on whichever .NET 8 runtime is installed. The default pins the newest 8.0 patch the SDK knows, which may not be installed locally. Release and Docker builds keep the default.

The app listens on http://localhost:9797.

## Tests

The Servarr suites run like `test.sh` does, minus integration and automation tests:

```bash
cd _tests/net8.0
dotnet test Requestarr.Api.V1.Test.dll Requestarr.Common.Test.dll Requestarr.Core.Test.dll Requestarr.Host.Test.dll \
  Requestarr.Libraries.Test.dll Requestarr.Update.Test.dll Requestarr.Windows.Test.dll \
  "--filter:Category!=ManualTest&Category!=LINUX&Category!=IntegrationTest&Category!=AutomationTest"
```

On Linux, swap `Requestarr.Windows.Test.dll` for `Requestarr.Mono.Test.dll` and `LINUX` for `WINDOWS`.

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

See `src/NzbDrone.Core.Test/Requestarr/Discord/Harness`. Automating Discord user accounts is not allowed. End-to-end checks use the bot's real effects in the dedicated test server, plus a short manual pass in the Discord app.
