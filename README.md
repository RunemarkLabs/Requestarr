# Requestarr

Discord-native media request management for Sonarr, Radarr, and Lidarr.

Requestarr gives every user an isolated, private request channel in Discord. Administrators onboard users directly from Discord, control what each user may request, approve or auto-approve requests, and provision matching media server library access. Content removal is governed by a vote among users with access to that content, so titles still in use are not deleted.

> **Status:** early development. Not yet ready for use.

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
| Movies | Radarr | Planned (first release) |
| TV | Sonarr | Planned (first release) |
| Music | Lidarr | Planned (second release) |
| Comics, books | — | Planned, pending a delivery method |

Multiple instances of each service are supported (for example, a separate 4K Radarr).

## Architecture

- Built on the Servarr foundation, derived from [Prowlarr](https://github.com/Prowlarr/Prowlarr)
- .NET 10 backend, React web portal, SQLite storage
- Discord integration via [Discord.Net](https://github.com/discord-net/Discord.Net)

## Getting started

Follow the [setup guide](docs/setup-guide.md) to create your Discord bot and install Requestarr with Docker Compose. Every Discord command is listed in the [command reference](docs/commands.md).

## Development

- [docs/development.md](docs/development.md): building, tests, the Docker dev stack (`docker/dev.sh up`), the provisioning file, and the Discord interaction test harness
- [docs/upstream.md](docs/upstream.md): which Prowlarr commit the foundation came from, what was removed, and how to pull upstream fixes

## Background

Requestarr is inspired by [Requestrr](https://github.com/thomst08/requestrr) and is a ground-up rebuild on the Servarr foundation, adding per-user channels, access control, and governed removals. It is an independent project and is not affiliated with or endorsed by the Servarr team or the Requestrr maintainers.

## License

Licensed under the GNU General Public License v3.0. Requestarr includes code derived from Servarr projects, which are also licensed under GPL-3.0.
