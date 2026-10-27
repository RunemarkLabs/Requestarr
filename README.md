# Requestarr

Discord-native media request management for Sonarr, Radarr, and Lidarr.

Requestarr gives every user an isolated, private request channel in Discord. Administrators onboard users directly from Discord, control what each user may request, approve or auto-approve requests, and provision matching media server library access. Content removal is governed by a vote among users with access to that content, so titles still in use are not deleted.

> **Status:** early access, before version 1.0. Requesting movies and TV in Discord works end to end (private channels, approvals, per-user access, removal votes), and a beta server with early testers is being set up. Version 1.0, the first public release, adds music and the admin web portal. Container images are published only for the beta so far; see the [setup guide](docs/setup-guide.md) to build from source.

## Features

### For users
- A private request channel visible only to the user and administrators.
- Search and request movies and TV from that channel.
- Duplicate requests are merged: users requesting an existing title are attached to it and notified on availability.
- Additional seasons can be added to an existing TV request at any time.
- Removal requests for owned titles, subject to a vote.

### For administrators
- User onboarding, suspension, and removal from Discord. Users who leave the server are suspended automatically.
- Per-user, per-content-type access: off, approval required, or auto-approve, with optional request quotas.
- Approval queue in a dedicated admin channel.
- Media server account linking, with library sharing that follows each user's request access.
- Administrator-initiated removals (original requester consulted first), with override.
- Full management through Discord or an admin-only web portal.

### Removal voting
When a removal is proposed, every user with access to that content type is asked whether they are still watching it. A single "keep" vote cancels the removal. If no one objects within the voting window, the title is removed. Administrators may override.

## Supported services

| Content | Service | Status |
|---|---|---|
| Movies | Radarr | Working, in beta testing |
| TV | Sonarr | Working, in beta testing |
| Music | Lidarr | Planned for version 1.0 |
| Comics, books | — | Planned, pending a delivery method |

Multiple instances of each service are supported (for example, a separate 4K Radarr).

## Architecture

- Built on the Servarr foundation, derived from [Prowlarr](https://github.com/Prowlarr/Prowlarr)
- .NET 10 backend, React web portal, SQLite storage
- Discord integration via [Discord.Net](https://github.com/discord-net/Discord.Net)

## Getting started

Follow the [setup guide](docs/setup-guide.md) to create your Discord bot and install Requestarr with Docker Compose. Every Discord command is listed in the [command reference](docs/commands.md).

## Development

- [CHANGELOG.md](CHANGELOG.md): what changed in each version, the same notes Requestarr posts in Discord
- [docs/development.md](docs/development.md): building, tests, the release flow (`dev`, then QA, then the beta, then a release) and how version numbers follow it, the local QA and dev stacks (`.\docker\dev.ps1 up` in PowerShell, or `docker/dev.sh up`) and their helpers for testing by hand, the provisioning file, and the Discord interaction test harness
- [docs/upstream.md](docs/upstream.md): which Prowlarr commit the foundation came from, what was removed, and how to pull upstream fixes

## Background

Requestarr is inspired by [Requestrr](https://github.com/thomst08/requestrr) and is a ground-up rebuild on the Servarr foundation, adding per-user channels, access control, and governed removals. It is an independent project and is not affiliated with or endorsed by the Servarr team or the Requestrr maintainers.

## License

Licensed under the GNU General Public License v3.0. Requestarr includes code derived from Servarr projects, which are also licensed under GPL-3.0.
