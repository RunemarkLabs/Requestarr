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

`.github/workflows/build.yml` runs on every push and pull request using standard GitHub-hosted runners. The `build` job is the single required status for `dev`, `qa`, `staging` and `main`, which accept changes only through a pull request with this check passing (see [Release flow](#release-flow)).

| Job | Runs |
|---|---|
| Backend (Linux) | solution build (`Platform=Posix`); test suites Api.V1, Common, Core, Host, Libraries, Mono, Update, excluding categories ManualTest, IntegrationTest, AutomationTest, WINDOWS |
| Backend (Windows) | solution build (`Platform=Windows`); test suites Api.V1, Common, Core, Host, Libraries, Update, Windows, excluding categories ManualTest, IntegrationTest, AutomationTest, LINUX |
| Frontend | `tsc --noEmit`, ESLint, Stylelint, production webpack build |
| Docker image | full `docker/Dockerfile` build (not pushed) |
| Changelog | the checker's self-test, then `CHANGELOG.md` against the changelog rule; on pull requests also: `CHANGELOG.md` changed, its top version is higher than the base branch's, and into `dev`, `qa` or `staging` free on the other two (see [Changelog](#changelog)) |
| Promotion record | pull requests that promote `dev`'s commits into `qa`, or `qa`'s into `staging`: the description has a `Requested by:` line (see [Release flow](#release-flow)) |

`.github/workflows/drift.yml` runs on every push to `dev`, `qa` or `staging`, daily, and by hand (Actions → Drift → Run workflow, with any branches): it fails while `qa` has a commit `dev` doesn't, or `staging` one `qa` doesn't. It isn't a required check, because a fix merged into `qa` or `staging` is drift until it is merged back, in the same task.

`.github/workflows/image.yml` publishes the container image (see [Published images](#published-images)).

Not run in CI:

- `Requestarr.Integration.Test`: needs a packaged app layout started as a separate process.
- `Requestarr.Automation.Test`: needs Selenium and a browser against a running app.
- `Requestarr.Benchmark.Test`: performance measurements, not pass or fail.

## Changelog

`CHANGELOG.md` at the repo root records what changed for the people who use Requestarr, and Requestarr posts it in Discord: when a server starts on a new version, the whole entry goes to the admin channel and the **For users** lines go to the announcements channel, word for word (which versions each server shows: [Version posts](#version-posts)). So every pull request does this:

1. **Bump the version** in `src/Directory.Build.props` (`<AssemblyVersion>x.y.z.*</AssemblyVersion>`): the patch number for fixes and internal work, the minor number for new things people can do. Only `x.y.z` counts; the last part is the build number.
2. **Add a `## x.y.z (YYYY-MM-DD)` section at the top of `CHANGELOG.md`**, with the day the version was made (the version cards in Discord show it), following the changelog rule:

   ```markdown
   ## 0.1.1 (2026-10-25)

   ### For users
   - **Cancel later:** you can now cancel a request after it was approved, until it starts downloading.

   ### For admins
   - **Member cards:** each card shows when the member last made a request.
   ```

**The changelog rule** (the same rule as the Claude Code workspace's "Changelog: hard rule"):

- **Two audiences only.** Exactly two sections, **For users** and **For admins**. There is no "Internal" section: work that changes nothing a user or an admin sees or does (tests, build and CI, refactoring, the dev and QA tools) is left out and lives in the pull request. A section with nothing in it is left out, heading included; a version with nothing for either has only its `## x.y.z (YYYY-MM-DD)` heading and posts nothing anywhere.
- **What changed and why it matters, in plain English.** Lead each line with the thing that changed in bold, then one sentence on what it means for the reader: "**Long TV shows:** you can now pick any season, not just the 24 most recent."
- **No code-style text** in either section: no backticks, file names, paths, commands or flags, settings keys, environment variable names, branch names or internal jargon. Name things the way the reader sees them in Discord ("the request message", "the admin panel"). Discord commands such as /request are fine: that's what people type.
- **Summary words only with substance.** "Minor fixes" or "various improvements" may lead a line only if the same line says what changes for the reader. No placeholders ("nothing this time", "n/a").
- **Group related changes**, three to seven lines per section is normal; fixes say what was wrong in the reader's terms ("Fixed: approving a request sometimes left the old Cancel button working.").

**The checks.** CI's `Changelog` job runs `.github/changelog_check.py` (its own self-test first, so a broken rule can't pass silently) and fails a pull request whose changelog has: a section other than For users and For admins, or the same one twice; an empty section heading; loose text that isn't a heading or a `- ` line; code-style text in a line (backticks, a file name, a path, a command flag, a settings key or an environment variable name); a placeholder; a summary phrase with fewer than five words of substance on its line; a version heading without its day; or versions out of order or repeated. Run it yourself with `python3 .github/changelog_check.py CHANGELOG.md`. Then `./release.sh check-changelog` fails a pull request that doesn't change `CHANGELOG.md` or whose top version isn't higher than the base branch's. Into `dev`, `qa` or `staging`, the version must also be higher than the other two branches', unless the pull request already contains that branch, so a fix made in QA or staging never shares a number with `dev`. A merge-back keeps every version from both branches instead of adding one. A unit test checks that the top version matches `<AssemblyVersion>`, that the file is embedded in the build, and that **For users** lines avoid technical words. The pull request template has the same checklist.

## Release flow

Four branches, each with its own environment.

| Branch | What it's for | Where it runs |
|---|---|---|
| `dev` | Every pull request merges here once the `build` check passes. Claude Code's automated and live testing. | The dev stack, on the dev Discord server and bot (`DEV_DISCORD_*`). |
| `qa` | The owner's manual QA of a version picked from `dev`. Moves only on the owner's word ("promote to QA"). | The QA stack, on the QA Discord server and bot (`DISCORD_*`). Never touched by automated testing. |
| `staging` | Early access testers, on a version the owner passed in QA. Moves only on "QA passed". | The beta stack on the owner's server (`deploy/staging`), the Requestarr Beta server and bot, with its own Sonarr and Radarr that really download. Runs the published `staging` image. |
| `main` | Production: only released code. Moves only on the owner's "ship it". | Published as `latest` and version tags. |

`dev` is the repository's default branch, so Dependabot's pull requests (security fixes included) land there and move up like any other change (see [Dependency updates](#dependency-updates)). All four branches have the same rules on GitHub (one ruleset): a pull request is required, `build` must pass, no force push, no deletion, nobody bypasses.

**A change.** Branch from `dev`, open the pull request into `dev`, merge it after `build` passes. Bump the version above `dev`'s, `qa`'s and `staging`'s (the Changelog check says so if it isn't).

**Promote to QA** (only when the owner says "promote to QA", or names a version):

```bash
./release.sh promote-qa 0.2.3 'the owner: "promote 0.2.3 to QA"'
```

It takes a version (the newest `dev` commit with that version on top of `CHANGELOG.md`) or a commit, and refuses anything that isn't on `dev`, or that `qa` can't move to without losing a commit. It opens a pull request into `qa` whose description says who asked, in their words (`Requested by: …`), and lists the versions and commits moving. The Promotion record check fails a promotion without that line, and the pull request stays as the record. Claude Code works with the owner's GitHub account, so GitHub can't tell the owner's approval from Claude Code's; the rule is that Claude Code promotes only on the owner's word, and the record says whose. Once `build` passes, `qa` is fast-forwarded to exactly that commit: the same commits as on `dev`, nothing newer, no merge commit. GitHub marks the pull request merged. Then `.\docker\dev.ps1 reset-qa` (or `docker/dev.sh reset-qa`) so the QA stack runs `qa`. Work on `dev` carries on meanwhile.

**Promote to staging** (only when the owner says "QA passed", or names a version). First run the full suite and every live check, and report them ("Testing scope" in the workspace rules: nothing reaches staging without this). Then:

```bash
./release.sh promote-staging 0.3.0 'the owner: "QA passed"'
```

The same as a promotion to QA, one step up: only `qa`'s commits, a pull request into `staging` with `Requested by:`, then a fast-forward of `staging`. The push to `staging` runs the Image workflow, which publishes `ghcr.io/runemarklabs/requestarr:staging` and `:0.3.0`. Then `.\docker\dev.ps1 up` so the QA stack drops the versions staging now has from its posts, and on the owner's server `./update.sh` in the staging folder (the setup guide's "Running the beta (staging) environment").

**Ship it** (staging to `main`, only on the owner's "ship it"): not scripted yet. It will be the same shape, and the push to `main` publishes the version tags and `latest`.

**A fix found in QA or staging.** Branch from that branch (`qa` or `staging`) and open a pull request into it with only the fix, versioned above all of `dev`, `qa` and `staging`. After it merges, in the same task, merge it back down:

```bash
./release.sh merge-back
```

It merges `staging` into `qa` (if `staging` has anything `qa` doesn't), then `qa` into `dev`, each through a pull request merged once `build` passes, and runs the drift check. If two branches conflict (usually `CHANGELOG.md` and the version, when the branch below has moved on), it says how: on a branch from the lower one named `merge-back/…`, merge the upper one, keep every changelog section with the highest version on top, and keep the higher version. Then `.\docker\dev.ps1 up` updates the QA stack, keeping its data. A fix into `staging` publishes a new `staging` image on its own.

**Drift.** `dev` must always contain every commit on `qa`, and `qa` every commit on `staging`:

```bash
./release.sh drift                                   # origin/dev, origin/qa and origin/staging
./release.sh drift origin/dev origin/qa some-branch
```

It lists any commit a lower branch is missing and fails; the Drift workflow runs the same check on GitHub. A promotion refuses to run while there is drift.

### Published images

`.github/workflows/image.yml` builds `docker/Dockerfile` for **linux/amd64** (most home servers and NAS boxes) and **linux/arm64** (Raspberry Pi 4 and 5, ARM NAS boxes, Apple silicon), the same pair the other Servarr apps publish, and pushes it to GitHub's container registry as `ghcr.io/runemarklabs/requestarr`:

| When | Tags |
|---|---|
| a push to `staging` (a promotion, or a fix into `staging`) | `staging`, and the exact version (`0.3.0`) |
| a push to `main` (a release) | the version (`0.3.0`), `0.3` and `latest` |
| Actions → Image → Run workflow on any branch, with a tag starting `test-` | that tag only, to try the workflow (delete it afterwards from the package's page) |

The web UI and backend are built on the runner's own platform and the backend is published for the target, so only the last image stage runs emulated. Images never contain secrets: the build uses only committed files (`secrets.env` and every `.env` file are in `.gitignore` and `.dockerignore`), and the workflow checks each platform's published image for files that could hold secrets (outside the base image's public CA certificates), secret-looking environment variables and build steps, and that it starts and answers on its port. The image carries the `org.opencontainers.image.*` labels: version, commit, branch and source repository.

### Version posts

The "what's new" posts in `#requestarr-admin` (both sections) and `#requestarr-announcements` (For users) are one-line cards with **Read changes**, which shows the full card privately (task 4e). They're a record that stays, kept in step with each environment. Requestarr reads `REQUESTARR_ENVIRONMENT` (`dev`, `qa`, `staging`; unset means production):

| Environment | Shows | How it knows |
|---|---|---|
| production, staging | every version, each posted once, oldest first, never removed. A new install's first start posts the full history. | It records the versions it has posted. If its data is lost but the channels are kept, it finds its posts in the channels and doesn't repeat them. |
| QA | every version in QA that staging doesn't have yet; versions drop off when they're promoted, and a reset rebuilds the list instead of wiping it | Staging has what was last published for it. When the QA stack is built, `docker/staged-versions.py` asks GitHub (its public record, no login) which commit the newest successful Image workflow run on `staging` published, reads the versions in that commit's changelog, and the scripts pass them as `REQUESTARR_STAGED_VERSIONS`. Before anything is published for staging that's nothing, so QA shows every version; offline, it falls back to the `staging` branch. After a promotion to staging, once its image is published, `dev.ps1 up` refreshes them. |
| dev | only the version running now; a new version replaces the previous post | |

On every start QA and dev make the channels match: cards for versions that no longer apply are deleted, a card whose words changed is edited in place, and from the first card out of order onward everything is posted again, so the list always reads oldest to newest. Requestarr finds its own cards by their title (`Requestarr x.y.z · 6 changes for admins, 4 for users`, `Requestarr x.y.z · 4 new things for you`) among the newest 1,000 messages of each channel. Text posts from before cards (`📋 **Requestarr x.y.z: what's new**`, `✨ **What's new in Requestarr x.y.z**`) are found too and replaced by cards: on QA and dev on every start, on production and staging once, keeping the whole history. Everything else in the channels is left alone.

## Dependency updates

`dev` is the repository's default branch, so Dependabot's pull requests, version updates and security fixes alike, go into `dev` and move up through `qa`, `staging` and `main` like any other change. `main` stays production with the same rules as before.

**What arrives, and when** (`.github/dependabot.yml`). Four kinds of dependency, each checked every Monday:

| Kind | Where | Groups |
|---|---|---|
| Backend packages (NuGet) | every project in `src/Requestarr.sln` | `backend` (patch and minor), `backend-major`, `backend-security` |
| Web app packages (npm, yarn) | `package.json`, `yarn.lock` | `frontend`, `frontend-major`, `frontend-security` |
| GitHub Actions | `.github/workflows` | `actions`, `actions-major`, `actions-security` |
| Base images (Docker) | `docker/Dockerfile` | `images`, `images-major`, `images-security` |

So each kind sends at most one pull request a week for its patch and minor updates and one for its major updates. Security fixes come as soon as GitHub publishes the advisory, also one pull request per kind.

**What happens to them** (`.github/workflows/dependency-updates.yml`, `.github/dependency_updates.py`):

- A new update is sorted. Patch and minor get the **automatic update** label. Major updates, and updates Dependabot can't size, get **needs review** and a comment, and wait. A security fix follows the same rule by size, and also gets **security**. It gets **for admins** too when the fix is in something Requestarr ships: the backend, the base images, or a web app package the built app contains (followed through `yarn.lock` from `dependencies`, not `devDependencies`).
- Automatic updates go one at a time, oldest first. The workflow brings the update up to date with `dev`, adds the next version above `dev`'s, `qa`'s and `staging`'s to `src/Directory.Build.props` and `CHANGELOG.md` (in a commit made through GitHub's API, which GitHub signs: `dev`'s rules ask for an extra approval for changes they can't attribute, and a commit the workflow pushed with git is unsigned), starts `build` on that commit, and turns on GitHub's auto-merge. When that `build` finishes the workflow runs again: it merges the update if `build` passed (the full build: backend tests on Linux and Windows, the web app, the image, the secret scan and the Changelog check) and starts on the next one.
- The changelog section is the version heading only, which the changelog rule allows for a change nobody would notice. A security fix labelled **for admins** gets one line under For admins: "**Security update:** parts Requestarr is built from were updated to fix known security problems, so update when you can."
- If an automatic update conflicts with `dev` (other than the version and changelog, which it rewrites), can't be pushed, or fails `build`, it is moved to **needs review** with a comment.
- The workflow runs when an update opens or changes, when `dev` moves, when a `build` it started finishes, and every hour as a fallback (GitHub can run scheduled workflows late or skip them).
- A package used in more than one project must have its version set in one place, or an update moves only some of them and `build` fails. NUnit, for example, comes only from `Requestarr.Test.Common`.

**Major updates and held updates** are Claude Code's: review the changes, fix what breaks, and merge in a normal task (a pull request into `dev` with its own version and changelog, or the held one finished and merged by hand).

**Security fixes reach the owner** three ways: the pull request carries the **security** label; a fix in something Requestarr ships puts a "Security update" line in the version's For admins section, so it shows in the admin channel's version post; and Claude Code checks for security fixes merged into `dev` that `qa` doesn't have yet at the end of every task and names them in the completion banner's "Needs you" line, so the owner can promote sooner than usual.

Repository settings this relies on: default branch `dev`, auto-merge allowed, Dependabot security updates on.

## Docker stack

Two local stacks run side by side, each with its own Requestarr and dedicated Sonarr and Radarr test instances:

| Stack | Runs | Discord | Who uses it |
|---|---|---|---|
| **QA** (the default) | the `qa` branch | the QA server and bot: `DISCORD_*` in `docker/secrets.env` | the owner's manual QA |
| **dev** (`-Stack dev`) | the `dev` branch | the dev server and bot: `DEV_DISCORD_*` | Claude Code's automated and live testing |

One command builds Requestarr from the stack's branch, starts it, waits for every health check, and runs the checks. On Windows, from a normal PowerShell tab in the repo folder:

```powershell
.\docker\dev.ps1 up               # the QA stack, from qa
.\docker\dev.ps1 -Stack dev up    # the dev stack, from dev
.\docker\dev.ps1 stacks           # both, side by side
```

With Git Bash, Linux or macOS, `docker/dev.sh up` does the same (`--stack dev` for the dev stack). Both scripts have the same commands; the examples below use `dev.ps1`, and every command acts on the QA stack unless you add `-Stack dev`.

The image is built from the branch as it is on GitHub (`origin/qa` or `origin/dev`, fetched first), from committed files only, so a stack runs exactly that commit whatever is checked out or changed locally. `--ref <branch or commit>` builds another one (for trying a branch before it merges). `check` and `stacks` show which branch and commit each stack was built from.

**How they keep apart.** Each stack is its own Compose project (`requestarr-qa`, `requestarr-dev`), so container names, volumes and the network are separate. Each has its own image (`requestarr:qa`, `requestarr:dev`) and its own ports, and its Requestarr gets only its own bot's token and server: the compose file passes nothing else from `docker/secrets.env`.

**What it needs.** Docker Desktop, running, and Git. Python 3 for `check`'s Discord lines, the resets and `fake-download` (from https://www.python.org/downloads/, with "Add python.exe to PATH" ticked); `dev.ps1` checks for both and says what's missing. Don't run `dev.sh` by typing `bash` in PowerShell: that can start WSL's bash, which may not reach Docker Desktop or Python the same way. Use `dev.ps1`, or Git Bash.

**If PowerShell says running scripts is disabled.** That message comes from PowerShell before the script starts. Run this once, then `dev.ps1` works in every new tab:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

If you can't change the policy, `.\docker\dev.cmd` takes the same commands and runs `dev.ps1` for that one run whatever the policy is.

| Service | QA stack (localhost only) | Dev stack (localhost only) | Notes |
|---|---|---|---|
| Requestarr | http://localhost:9797 | http://localhost:19797 | provisioned from `docker/provisioning.yml` |
| Sonarr (test) | http://localhost:18989 | http://localhost:28989 | root folder `/data/tv`, no login (localhost-bound) |
| Radarr (test) | http://localhost:17878 | http://localhost:27878 | root folder `/data/movies`, no login (localhost-bound) |

Other commands:

- `check`: health and status checks: which branch and commit the stack runs, Requestarr, whether fast votes are on, its Discord bot (online, which server, registered commands, the latest what's new posts; read only), and the test backends.
- `stacks`: both stacks side by side: branch and commit, version, address, and whether each bot is online.
- `reset-test-backends`: wipes the stack's test Sonarr and Radarr back to empty and reconfigures them.
- `reset-qa`: a clean slate for QA. It deletes Requestarr's channels, categories, the members role and per-user roles in the QA Discord server. It keeps the bot's role, the Requestarr Admin role and everything else. It also wipes the QA stack's Requestarr database and test backends, then starts Requestarr again from `qa`. The bot recreates `#requestarr-admin` and its pinned panel, so the server is ready for testing. Fast votes are always off after it. Add `--stopped` to leave Requestarr stopped. The old name, `reset-uat`, still works.
- `reset-dev`: the same for the dev stack and the dev server. Live tests run after `reset-dev --stopped`, and `reset-dev` afterwards brings the dev stack back.
- `down`: stops the stack; data is kept. `up` starts it again.
- `logs requestarr`: follows the Requestarr logs (Ctrl+C to stop).
- `init`: creates `docker/secrets.env` (see Secrets below); `up` runs it.

### Helpers for testing by hand

The test Sonarr and Radarr have no indexer or download client, so nothing ever downloads, and removal votes take days. These helpers reach those states by hand. They only ever touch the chosen stack's own containers (Compose project `requestarr-qa`, or `requestarr-dev` with `-Stack dev`).

| Command | What it does | What to expect in Discord |
|---|---|---|
| `stop-backend sonarr` (or `radarr`) | Stops only that test instance. | Within a minute Requestarr notices; after 3 minutes it posts a down alert in `#requestarr-admin`, new requests of that type say they're paused, and the instance waits in `/admin requests`. After 30 minutes the alert offers **Announce to users**. |
| `start-backend sonarr` (or `radarr`) | Starts it again. | Within a minute, "… is back after N minutes" in `#requestarr-admin` (if it had alerted), and requests work again. |
| `fake-download movie "Dune"` | Makes a movie that is already in the test Radarr look downloaded: a placeholder file (it takes no disk space) is handed to Radarr's own Manual Import, owned by its app user, exactly as a real download is imported. Radarr then sends its normal import webhook to Requestarr (the backup poll would find it too). Request the title in Discord first. | The request message is reposted as **Available**, mentioning the requester and followers; its feed card moves to the bottom showing Available, with **Delete**. |
| `fake-download tv "Severance" 1` | The same for every aired episode of that season in the test Sonarr. | **Partly available** while other requested seasons are missing; **Available** once every requested season is there. |
| `fast-votes on` | Local test stacks only: new removal votes close after **5 minutes**, requesters have **5 minutes** to answer, reminded at **minute 3** (instead of 7 days, 7 days and day 5). Removals already open keep their times. | Within a minute, a **🧪 Dev: fast votes are on** notice in `#requestarr-admin`, naming the real settings, so nobody takes the minute timings for them. |
| `fast-votes off` / `fast-votes status` | Back to the real settings / says which is on. | **🧪 Dev: fast votes are off** in `#requestarr-admin`. |

Fast votes can't be on anywhere but a local test stack: they need a marker file in Requestarr's data folder (what `fast-votes on` creates) *and* the `REQUESTARR_DEV_STACK` variable that only `docker/compose.yaml` sets; any other install ignores the file. They stay on across `up`/`down` until you turn them off, and `reset-qa` and `reset-dev` always start with them off.

Host ports can be changed in `docker/secrets.env`: `REQUESTARR_PORT`, `SONARR_TEST_PORT` and `RADARR_TEST_PORT` for the QA stack, the same names starting `DEV_` for the dev stack. Neither stack touches another deployment.

### Secrets

`init`, which `up` runs, creates `docker/secrets.env` from `docker/secrets.env.example`. It generates random local test keys and leaves the Discord and Plex values for you to fill in: `DISCORD_BOT_TOKEN`, `DISCORD_APPLICATION_ID` and `DISCORD_TEST_GUILD_ID` for the QA server's bot, and `DEV_DISCORD_BOT_TOKEN`, `DEV_DISCORD_APPLICATION_ID` and `DEV_DISCORD_GUILD_ID` for the dev server's bot (a separate bot application, invited only to the dev server). `docker/secrets.env` is gitignored. Never commit real values, and never put them in code, docs, or comms.

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

Live checks are tagged `ManualTest`, so they never run in CI. They read their keys from `docker/secrets.env`, and they only ever use the dev environment: the dev Discord server and bot (`DEV_DISCORD_*`) and the dev stack's test Sonarr and Radarr (`LiveDev` in the test harness). Nothing in them reads the QA server's or the QA stack's values, and a unit test keeps it that way, so automated testing never interrupts the owner's QA.

They only ever onboard, suspend or request as the members named in `DISCORD_TEST_MEMBERS` (comma-separated Discord usernames of your test accounts) who are in the dev server. Everyone else is left alone. Set it in your shell, or in `docker/secrets.env`; with no named member in the dev server, the checks that need one are skipped. `LiveAnnouncementsFixture` shortens the outage thresholds to 15 and 30 seconds; set `LIVE_REAL_THRESHOLDS=1` to wait the real 3 and 30 minutes.

```bash
set -a; . docker/secrets.env; set +a
export DISCORD_TEST_MEMBERS=yourtestaccount
cd _tests/net10.0
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveDiscordFixture" --logger "console;verbosity=detailed"
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveBackendFixture" --logger "console;verbosity=detailed"
dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveReadBackFixture" --logger "console;verbosity=detailed"
```

For each change, test in this order, all on the dev stack:

1. `.\docker\dev.ps1 reset-dev --stopped` (or `docker/dev.sh reset-dev --stopped`)
2. Run the Discord live check. It runs its own bot, so the dev stack's Requestarr must be stopped.
3. Merge into `dev`, then run `.\docker\dev.ps1 reset-dev` (or `docker/dev.sh reset-dev`). It runs `dev` on a clean dev server with the admin channel and panel.
4. Run the read-back.

- **Discord:** checks the admin role, the registered commands, onboarding, permission read-back and the pinned panels in the dev server. It then deletes what it created. It never onboards a member who already has a request channel.
- **Read-back:** reads the running dev stack's registered commands and pinned panels over REST. It changes nothing.
- **Backends:** requests a movie and a show into the dev stack's test Radarr and Sonarr only, and reads back what they stored. The titles are left there. Automating Discord user accounts is not allowed. End-to-end checks use the bot's real effects in the dev server, plus a short manual pass in the Discord app.
