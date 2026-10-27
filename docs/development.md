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

The Servarr suites, minus integration and automation tests (the same set CI runs):

```bash
cd _tests/net10.0
dotnet test Requestarr.Api.V1.Test.dll Requestarr.Common.Test.dll Requestarr.Core.Test.dll Requestarr.Host.Test.dll \
  Requestarr.Libraries.Test.dll Requestarr.Update.Test.dll Requestarr.Windows.Test.dll \
  "--filter:Category!=ManualTest&Category!=LINUX&Category!=IntegrationTest&Category!=AutomationTest"
```

On Linux, swap `Requestarr.Windows.Test.dll` for `Requestarr.Mono.Test.dll` and `LINUX` for `WINDOWS`.

## Clean code

Requestarr's code is written to be read and reviewed by people. These standards hold for every change; checks in the build enforce what a tool can see, and review covers the rest. Follow the surrounding code's style (Servarr's conventions in the backend, the web app's existing patterns) rather than a textbook, and don't split code into layers or tiny functions just to meet a number.

| Standard | Limit | Checked by |
|---|---|---|
| **No unused code**: functions, classes, fields, parameters, locals, imports, exports, files, packages | none | Web: ESLint's unused-variable rules and TypeScript's unused locals and parameters; knip for files, exports and packages |
| **One thing per function** | cognitive complexity 15; nesting 4 deep; in the web app at most 4 parameters and 50 lines a function in `.ts` (80 in `.tsx` and `.js`, which hold markup) | Web: `sonarjs/cognitive-complexity`, `complexity` (15, a whole `switch` counted once), `max-depth`, `max-params`, `max-lines-per-function` |
| **Clear names**: full words, verbs for functions, names a reader understands without the code around them | C#: `_camelCase` private fields, PascalCase constants, `I` before interfaces, `Id` not `ID`. TypeScript: no `I` prefix, acronyms as words (`loadHttpUrl`) | review |
| **No nested ternaries**, no copied branches or methods | | Web: `no-nested-ternary` (upstream) |
| **No commented-out code; comments say why**, never what | | review |
| **No unnamed numbers or strings** for limits, timeouts, identifiers or paths | named constants or settings | review: the tools for this flag every `0`, `1` and color in markup, so it stays a review rule |
| **React**: derived values computed during render, logic for a user's action in its handler, effects only to sync with something outside | | the react-hooks plugin's 17 recommended rules |

**Limits are review triggers, not proof.** A limit hit means look again: simplify (early returns, fewer conditions, a clearer data shape), or leave it and say why in a comment on the same line. Never split code just to get under a number: cognitive complexity ignores method calls, so moving a nested block into a helper lowers the score without making anything simpler. A good split extracts a piece with a purpose you can name that hides detail from the caller; a bad one leaves the reader jumping between helpers that only make sense together.

```ts
// Bad: three helpers that must be read together to follow one decision
const isDue = checkDue(item); const isOwned = checkOwner(item, user); return decide(isDue, isOwned);

// Good: one function with early returns
if (!item.dueAt) return false;
if (item.ownerId !== user.id) return false;
return item.dueAt <= now;
```

**Suppressions need a reason on the same line**: `// eslint-disable-next-line max-params -- mirrors upstream's API` in the web app, a `//` comment after `#pragma warning disable` or a `Justification` on `[SuppressMessage]` in C#. CI's Changelog job runs `.github/suppression_check.py` on every pull request: it fails on a suppression without a reason, on a baseline that grows, and on a rule turned down or a limit raised.

**Inherited code and the baselines.** The checks were added after most of the code existed. The web app is almost all inherited, so its existing violations are recorded and only new ones fail:

- **ESLint**: `frontend/eslint-suppressions.json` (ESLint's bulk suppressions) holds the violations that existed when a rule was added, counted per file and rule. `yarn lint` fails on any new one, and on a suppression that is no longer needed, so the file can only shrink: fix a violation, then `npx eslint --config frontend/eslint.config.js --suppressions-location frontend/eslint-suppressions.json --prune-suppressions frontend/`. Never add to it by hand.
- **knip**: `frontend/knip-baseline.json` holds the unused files, exports and packages knip found when the check was added. `python3 .github/knip_check.py` (CI's Frontend job) fails on anything new and on an entry that is fixed but still listed; `--update` rewrites the file after a fix. knip's package findings include build plugins it can't see being loaded by name (babel, postcss and webpack loaders), so check before removing a package.
- **Inherited files Requestarr still uses** are not edited to meet these limits, so upstream fixes keep applying (`docs/upstream.md`); only whole files nothing uses are removed.

The web app's lint configuration is `frontend/eslint.config.js` (ESLint 9's format: ESLint 10 removed the old one, and ESLint 10 itself waits for the React and file-name plugins to support it). Upstream's rules are in it unchanged, and Requestarr's own checks are in a marked block at the end.

## Continuous integration

`.github/workflows/build.yml` runs on every push and pull request using standard GitHub-hosted runners. The `build` job is the single required status for `dev`, `qa`, `staging` and `main`, which accept changes only through a pull request with this check passing (see [Release flow](#release-flow)).

| Job | Runs |
|---|---|
| Backend (Linux) | solution build (`Platform=Posix`); test suites Api.V1, Common, Core, Host, Libraries, Mono, Update, excluding categories ManualTest, IntegrationTest, AutomationTest, WINDOWS |
| Backend (Windows) | solution build (`Platform=Windows`); test suites Api.V1, Common, Core, Host, Libraries, Update, Windows, excluding categories ManualTest, IntegrationTest, AutomationTest, LINUX |
| Frontend | `tsc --noEmit`, ESLint (with its suppressions baseline), knip's unused-code check against its baseline, Stylelint, production webpack build (see [Clean code](#clean-code)) |
| Docker image | full `docker/Dockerfile` build (not pushed) |
| Changelog | the checkers' self-tests (changelog, QA checklist, QA gate, version rules, knip, suppressions), on pull requests the suppression check ([Clean code](#clean-code)), then `CHANGELOG.md` against the changelog rule; on pull requests also: `CHANGELOG.md` changed, its top version is higher than the base branch's, and into `dev`, `qa` or `staging` it follows the version rules and no other branch has it (see [Changelog](#changelog) and [Versions](#versions)) |
| Promotion record | pull requests that promote `dev`'s commits into `qa`, or `qa`'s into `staging`: the description has a `Requested by:` line (see [Release flow](#release-flow)) |

`.github/workflows/drift.yml` runs on every push to `dev`, `qa` or `staging`, daily, and by hand (Actions → Drift → Run workflow, with any branches): it fails while `qa` has a commit `dev` doesn't, or `staging` one `qa` doesn't. It isn't a required check, because a fix merged into `qa` or `staging` is drift until it is merged back, in the same task.

`.github/workflows/image.yml` publishes the container image (see [Published images](#published-images)).

Not run in CI:

- `Requestarr.Integration.Test`: needs a packaged app layout started as a separate process.
- `Requestarr.Automation.Test`: needs Selenium and a browser against a running app.
- `Requestarr.Benchmark.Test`: performance measurements, not pass or fail.

## Changelog

`CHANGELOG.md` at the repo root records what changed for the people who use Requestarr, and Requestarr posts it in Discord: when a server starts on a new version, the whole entry goes to the admin channel and the **For users** lines go to the announcements channel, word for word (which versions each server shows: [Version posts](#version-posts)). So every pull request does this:

1. **Bump the version** in `src/Directory.Build.props` (`<AssemblyVersion>x.y.z.*</AssemblyVersion>`) to the one the [version rules](#versions) give the branch the pull request goes into; `./release.sh next-version` (or `next-version qa`, `next-version staging` for a fix there) prints it. Only `x.y.z` counts; the last part is the build number.
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

**The checks.** CI's `Changelog` job runs `.github/changelog_check.py` (its own self-test first, so a broken rule can't pass silently) and fails a pull request whose changelog has: a section other than For users and For admins, or the same one twice; an empty section heading; loose text that isn't a heading or a `- ` line; code-style text in a line (backticks, a file name, a path, a command flag, a settings key or an environment variable name); a placeholder; a summary phrase with fewer than five words of substance on its line; a version heading without its day; or versions out of order or repeated. Run it yourself with `python3 .github/changelog_check.py CHANGELOG.md`. Then `./release.sh check-changelog` fails a pull request that doesn't change `CHANGELOG.md` or whose top version isn't higher than the base branch's. Into `dev`, `qa` or `staging`, the version must also follow the [version rules](#versions) (`.github/version_rules.py`, which has its own self-test): the next patch on `dev`'s line, or the next minor once QA has that line; a fix into `qa` or `staging` the next free patch on that branch's own line; never a version another of the three has. A promotion's versions were checked on the branch below. A merge-back keeps every version from both branches instead of adding one. The QA checklist has the same versions as the changelog, so a wrong number fails its check too. A unit test checks that the top version matches `<AssemblyVersion>`, that the file is embedded in the build, and that **For users** lines avoid technical words. The pull request template has the same checklist.

## Versions

Version numbers follow the release flow, so a number says how far a build has travelled:

1. **Patch = dev builds.** Every pull request into `dev` bumps the patch number on `dev`'s current line: 0.5.0, 0.5.1, 0.5.2. Dependency updates too.
2. **Minor = QA cycles.** A promotion to QA takes `dev`'s current version as it is. The next pull request into `dev` after it starts the next minor at .0: QA has 0.4.17, so `dev`'s next is 0.5.0 (no zero padding).
3. **Staging adds a beta pre-release.** A promotion to staging publishes QA's version as `0.4.17-beta.1`. The count goes up each time the same line (0.4) is published to staging again, by a fix into staging or a newer QA version of that line: `-beta.2`, `-beta.3`. A new line starts at `-beta.1`.
4. **Production removes it.** "Ship it" releases staging's exact code as `0.4.17`: no new number, the suffix dropped.
5. **Majors.** 1.0.0 is the first public release with the version 1 scope (movies, TV, music and the admin web portal). After that, a major only when admins must act on upgrade (a settings or provisioning format change, a manual migration, dropped support): the pull request that brings such a change bumps the major on `dev`, says what to do in a For admins line led by **Before you upgrade:**, and the banner and reply say so. Until 1.0 nothing triggers a major, and from 1.0 on a "Before you upgrade" line in anything but a major fails the check.

**Fixes into QA or staging** take the next patch on that branch's own line that none of `dev`, `qa` and `staging` has (QA on 0.4.17 while `dev` is on 0.5.x: 0.4.18). A merge-back keeps every version of both branches, in version order, highest first, so each line's versions stay together and the branch keeps its own line's number on top: `dev` reads 0.5.2, 0.5.1, 0.5.0, 0.4.18, 0.4.17.

**Where the beta suffix lives.** The build's own version is numeric (`<AssemblyVersion>` can't carry letters), so `CHANGELOG.md`, `QA-CHECKLIST.md` and the build say 0.4.17. The suffix is on the published `staging` image's version tag, in the version the beta server shows and posts (its version card for the version it runs, System → Status, and the logo's hover text) and in the promotion pull request. The Image workflow builds staging's image with it (`REQUESTARR_RELEASE_VERSION`) and records each publish as a git tag on the commit, `v0.4.17-beta.1`. The next number is one more than the highest tag on the line; a commit that already has one (a re-run) keeps it. `promote-staging` reads the same tags (`git ls-remote --tags`, no login) to say which beta version it will publish. Staging's earlier plain version tags stay as they are.

**Worked example.**

| Step | dev | qa | staging | production |
|---|---|---|---|---|
| Today | 0.4.17 | 0.4.17 | 0.3.4 | |
| Three pull requests into `dev` | 0.5.0, 0.5.1, 0.5.2 | 0.4.17 | 0.3.4 | |
| "QA passed": promote 0.4.17 to staging | 0.5.2 | 0.4.17 | 0.4.17-beta.1 | |
| A fix found in QA, merged back | 0.5.2 (with 0.4.18 below) | 0.4.18 | 0.4.17-beta.1 | |
| "QA passed": promote 0.4.18 to staging | 0.5.2 | 0.4.18 | 0.4.18-beta.2 | |
| A fix found in staging, merged back | 0.5.2 (with 0.4.19 below) | 0.4.19 | 0.4.19-beta.3 | |
| "Ship it" | 0.5.2 | 0.4.19 | 0.4.19-beta.3 | 0.4.19 |
| "Promote to QA" (dev's 0.5.2), then a pull request into `dev` | 0.6.0 | 0.5.2 | 0.4.19-beta.3 | 0.4.19 |

## QA checklist

`QA-CHECKLIST.md` at the repo root says what admins on the QA server test, by hand, before a version goes to staging. Requestarr is built with it and posts it in the QA server's `#requestarr-qa-checklist` channel, in an admin-only group of its own (Requestarr QA, so testing stays apart from running the server): a summary card, then one card per item with **Passes** and **Failing**, for every version QA has that staging doesn't. The dev server shows the running version's items, so the checklist can be tried out there; other servers have none.

**Every pull request adds its version's items**, the way it adds its changelog entry, at the top of `QA-CHECKLIST.md`, with the same `## x.y.z (YYYY-MM-DD)` heading as the changelog:

```markdown
## 0.4.15 (2026-10-25)
- **Report a problem:** In the bug reports channel, press Report a problem, pick a part and send a short test report. You should see a card for your report in the channel.
```

Each item says what to do and, after "You should see", what a pass looks like, in plain words an admin can follow in Discord: no code-style text, the same as the changelog. A version nobody can test by hand (build and CI work, tooling, dependency updates) has **only its heading**: that's how "nothing to test here" is marked, the same escape as a heading-only changelog entry. The automatic dependency updates add their heading themselves.

**The checks.** `./release.sh check-changelog` (CI's Changelog job) fails a pull request that doesn't change `QA-CHECKLIST.md`, or whose checklist doesn't match the changelog from 0.4.3 on (same versions, dates and order), or has an item without a bold name, without "You should see", named twice in a version, or with code-style text. `.github/checklist_check.py` does the checking (with a self-test); run it with `python3 .github/checklist_check.py QA-CHECKLIST.md CHANGELOG.md`. A unit test checks the file is embedded and its top version matches the build.

**In Discord.** Pressing **Passes** records who and when, and the card shows it (the press has no reply of its own: Discord can't turn a card into a plain message, which is why every press used to answer "Something went wrong"); **Failing** opens a bug report (below) linked to the item, and the card shows it. An admin failing items isn't held to the members' report limit, so several can fail in a row. The record is Requestarr's (the portal will show it later); the cards show it. The checklist is complete when every item has passed. A new promotion to QA while a checklist is half done keeps every pass and failure already recorded, adds the new versions' items as not tested yet, and drops the versions staging now has.

**The gate before staging.** `./release.sh qa-gate` (or `.\docker\dev.ps1 qa-gate`) reads the QA stack's checklist (`GET /api/v1/qa/checklist` with the API key) and lists the open GitHub issues labelled `env: qa` with one of the checklist's versions. It's clear when the checklist is complete, nothing is failing, no such issue is open (whether or not it came from a checklist item) and every bug report from QA is on GitHub: one kept on the server (filing off, no access key, GitHub refused it) is an open QA bug the issue list can't show, so it blocks too, whatever its version, and the gate names it with the reason. `python3 docker/qa-gate.py --self-test` checks these rules (CI runs it). `promote-staging` refuses otherwise, unless the owner overrides it in so many words (`--owner-override "<their words>"`), which the promotion pull request records. At the start of every goal, Claude Code runs the gate; when it's clear, it promotes QA to staging first (repo `CLAUDE.md`).

**Bug reports.** Every server has `#requestarr-bug-reports` in the Requestarr Users group: members and admins read it, nobody types in it, and its **Report a problem** button opens a menu of Requestarr's parts (one list, `BugReportComponents`, so it grows with the app) and a short form. Requestarr adds the environment, version, time and the reporter's Discord name, keeps the report, posts it as a card and, when the install files reports, opens a GitHub issue labelled `bug report`, `env: …`, `version: …` and `component: …` (and `test` from the dev server). Each person can send a few a day, a couple of minutes apart. The dev, QA and beta servers always file (the key comes from `REQUESTARR_GITHUB_ISSUES_TOKEN`: `DEV_GITHUB_ISSUES_TOKEN` and `GITHUB_ISSUES_TOKEN` in `docker/secrets.env`, `GITHUB_ISSUES_TOKEN` in beta's `.env`). A report that isn't filed says exactly why on its card and is filed each time the bot connects, until it is, without its reporter sending it again; the card then shows the link. Settings: `bugReports:` in the provisioning file (setup guide, "Bug reports").

**How the bug reports channel reads.** Reports in the order they came in, the **Report a problem** panel last. The panel moves below each new card as a new message (the old one is removed, never edited first, so nothing is marked "edited" for nothing). The reporter's private menu becomes "Sending your report…" and is removed once the card is posted: it replied to the panel, which moves, and would otherwise be left pointing at a deleted message. A report sent from a failing checklist item answers in the checklist channel instead.

## Release flow

Four branches, each with its own environment.

| Branch | What it's for | Where it runs |
|---|---|---|
| `dev` | Every pull request merges here once the `build` check passes. Claude Code's automated and live testing. | The dev stack, on the dev Discord server and bot (`DEV_DISCORD_*`). |
| `qa` | The owner's manual QA of a version picked from `dev`. Moves only on the owner's word ("promote to QA"). | The QA stack, on the QA Discord server and bot (`DISCORD_*`). Never touched by automated testing. |
| `staging` | Early access testers, on a version the owner passed in QA. Moves only on "QA passed". | The beta stack on the owner's server (`deploy/staging`), the Requestarr Beta server and bot, with its own Sonarr and Radarr that really download. Runs the published `staging` image. |
| `main` | Production: only released code. Moves only on the owner's "ship it". | Published as `latest` and version tags. |

`dev` is the repository's default branch, so Dependabot's pull requests (security fixes included) land there and move up like any other change (see [Dependency updates](#dependency-updates)). All four branches have the same rules on GitHub (one ruleset): a pull request is required, `build` must pass, no force push, no deletion, nobody bypasses.

**A change.** Branch from `dev`, open the pull request into `dev`, merge it after `build` passes. Take `dev`'s next version ([Versions](#versions); `./release.sh next-version` prints it, and the Changelog check says so if it's wrong).

**Promote to QA** (only when the owner says "promote to QA", or names a version):

```bash
./release.sh promote-qa 0.2.3 'the owner: "promote 0.2.3 to QA"'
./release.sh promote-qa 0.2.3 'a dry run' --dry-run
```

QA takes `dev`'s current version as it is, so "promote to QA" means `dev`'s top version; the script says what the next pull request into `dev` will be (the next minor). `--dry-run` prints the commits and versions that would move, and the version `dev` goes on to, and changes nothing. It takes a version (the newest `dev` commit with that version on top of `CHANGELOG.md`) or a commit, and refuses anything that isn't on `dev`, or that `qa` can't move to without losing a commit. It opens a pull request into `qa` whose description says who asked, in their words (`Requested by: …`), and lists the versions and commits moving. The Promotion record check fails a promotion without that line, and the pull request stays as the record. Claude Code works with the owner's GitHub account, so GitHub can't tell the owner's approval from Claude Code's; the rule is that Claude Code promotes only on the owner's word, and the record says whose. Once `build` passes, `qa` is fast-forwarded to exactly that commit: the same commits as on `dev`, nothing newer, no merge commit. GitHub marks the pull request merged. Then `.\docker\dev.ps1 reset-qa` (or `docker/dev.sh reset-qa`) so the QA stack runs `qa`. Work on `dev` carries on meanwhile.

**Promote to staging** (when the QA gate is clear, checked at the start of every goal, or when the owner says so). First run the full suite and every live check, and report them ("Testing scope" in the workspace rules: nothing reaches staging without this). Then:

```bash
./release.sh promote-staging 0.3.0 'the QA checklist, completed by <admin>'
./release.sh promote-staging 0.3.0 'the owner: "promote it anyway"' --owner-override 'the owner: "promote it anyway"'
./release.sh promote-staging 0.3.0 'a dry run' --dry-run
```

Without `--owner-override`, it runs the QA gate ([QA checklist](#qa-checklist)) and refuses unless it's clear; the pull request records the checklist and who completed it, or the owner's override.

The same as a promotion to QA, one step up: only `qa`'s commits, a pull request into `staging` with `Requested by:` and the beta version it publishes (`Published to staging as: 0.3.0-beta.1`), then a fast-forward of `staging`. The push to `staging` runs the Image workflow, which publishes `ghcr.io/runemarklabs/requestarr:staging` and `:0.3.0-beta.1` and tags the commit `v0.3.0-beta.1` ([Versions](#versions)). `--dry-run` prints the versions that would move, the beta version and the QA gate's verdict, and changes nothing. Then `.\docker\dev.ps1 up` so the QA stack drops the versions staging now has from its posts, and on the owner's server `./update.sh` in the staging folder (the setup guide's "Running the beta (staging) environment").

**Ship it** (staging to `main`, only on the owner's "ship it"): not scripted yet. It will be the same shape: a pull request into `main` with `Requested by:`, then a fast-forward of `main` to exactly `staging`'s commit. No new number and no changelog change: `main`'s top version is staging's (0.3.0), the beta suffix dropped. The push to `main` publishes `:0.3.0`, `:0.3` and `latest`, built from that same commit without the beta version, so production shows 0.3.0; the commit is tagged `v0.3.0`.

**A fix found in QA or staging.** Branch from that branch (`qa` or `staging`) and open a pull request into it with only the fix, versioned as the next free patch on that branch's own line (`./release.sh next-version qa` or `next-version staging`; [Versions](#versions)). After it merges, in the same task, merge it back down:

```bash
./release.sh merge-back
```

It merges `staging` into `qa` (if `staging` has anything `qa` doesn't), then `qa` into `dev`, each through a pull request merged once `build` passes, and runs the drift check. When the branch below has moved on, `CHANGELOG.md`, `QA-CHECKLIST.md` and the version conflict; it merges them itself on a branch from the lower one named `merge-back/…`: every version of both, in version order, highest first, and the build version is the changelog's top (the lower branch's own line). Any other conflict it leaves to you, saying how. Then `.\docker\dev.ps1 up` updates the QA stack, keeping its data. A fix into `staging` publishes a new `staging` image on its own.

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
| a push to `staging` (a promotion, or a fix into `staging`) | `staging`, and the beta version (`0.3.0-beta.1`, [Versions](#versions)); the commit is tagged `v0.3.0-beta.1`. Before 0.5.0, staging was tagged with the plain version, and those tags stay |
| a push to `main` (a release) | the version (`0.3.0`), `0.3` and `latest` |
| Actions → Image → Run workflow on any branch, with a tag starting `test-` | that tag only, to try the workflow (delete it afterwards from the package's page) |

The web UI and backend are built on the runner's own platform and the backend is published for the target, so only the last image stage runs emulated. Images never contain secrets: the build uses only committed files (`secrets.env` and every `.env` file are in `.gitignore` and `.dockerignore`), and the workflow checks each platform's published image for files that could hold secrets (outside the base image's public CA certificates), secret-looking environment variables and build steps, and that it starts and answers on its port. The image carries the `org.opencontainers.image.*` labels: version, commit, branch and source repository.

### Version posts

The "what's new" posts in `#requestarr-admin` (both sections) and `#requestarr-announcements` (For users) are one-line cards with **Read changes**, which shows the full card privately (task 4e). They're a record that stays, kept in step with each environment. Requestarr reads `REQUESTARR_ENVIRONMENT` (`dev`, `qa`, `staging`; unset means production):

| Environment | Shows | How it knows |
|---|---|---|
| production, staging | every version, each posted once, oldest first, never removed. A new install's first start posts the full history. Staging's card for the version it runs shows its beta version (`Requestarr 0.3.0-beta.1 · …`); cards already posted keep their words. | It records the versions it has posted. If its data is lost but the channels are kept, it finds its posts in the channels and doesn't repeat them. |
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
- Automatic updates go one at a time, oldest first. The workflow brings the update up to date with `dev`, adds `dev`'s next version ([Versions](#versions): the next patch, or the next minor after a promotion to QA) to `src/Directory.Build.props` and `CHANGELOG.md` (in a commit made through GitHub's API, which GitHub signs: `dev`'s rules ask for an extra approval for changes they can't attribute, and a commit the workflow pushed with git is unsigned), starts `build` on that commit, and turns on GitHub's auto-merge. The workflow waits for that `build`, then merges the update if it passed (the full build: backend tests on Linux and Windows, the web app, the image, the secret scan and the Changelog check) and starts on the next one.
- The changelog section is the version heading only, which the changelog rule allows for a change nobody would notice. A security fix labelled **for admins** gets one line under For admins: "**Security update:** parts Requestarr is built from were updated to fix known security problems, so update when you can."
- If an automatic update conflicts with `dev` (other than the version and changelog, which it rewrites), can't be pushed, or fails `build`, it is moved to **needs review** with a comment.
- The workflow runs when an update opens or changes, when `dev` moves, and every hour as a fallback (GitHub can run scheduled workflows late or skip them). It waits for the builds it starts itself, because GitHub starts no workflow when they finish or when auto-merge it turned on merges.
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

Ports come in one band per environment, so the first digit says which one you're on, counting down to the standard ports: dev 3xxxx, QA 2xxxx, beta 1xxxx (19797, 18989, 17878 on the owner's server), production the standard 9797, 8989 and 7878. Inside Docker every container keeps its standard port, so Sonarr and Radarr reach Requestarr's webhook as `http://requestarr:9797` whatever the band.

| Service | QA stack (localhost only) | Dev stack (localhost only) | Notes |
|---|---|---|---|
| Requestarr | http://localhost:29797 | http://localhost:39797 | provisioned from `docker/provisioning.yml` |
| Sonarr (test) | http://localhost:28989 | http://localhost:38989 | root folder `/data/tv`, no login (localhost-bound) |
| Radarr (test) | http://localhost:27878 | http://localhost:37878 | root folder `/data/movies`, no login (localhost-bound) |

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
