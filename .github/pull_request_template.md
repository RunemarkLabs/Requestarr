<!-- Pull requests go into `dev` (a fix found in QA or staging: into that branch, then merged back down with ./release.sh merge-back). See docs/development.md#release-flow. -->
## What changed


## Changelog
- [ ] `CHANGELOG.md`: this change's lines under `## Unreleased` at the top (no version number: the promotion to staging numbers the build), following the changelog rule:
  - only **For users** and **For admins**; leave a section out when it has nothing, and leave out work neither sees (tests, build, refactoring)
  - each line leads with the thing that changed in bold, then what it means for the reader, in plain words
  - no code, file names, commands, flags, settings keys or jargon; no "minor fixes" without saying what they fix; no placeholders
- [ ] or, when nothing changes for users or admins, no lines and this line in the description: `No changelog: <why>`
- [ ] `QA-CHECKLIST.md`: what an admin can test by hand, under `## Unreleased` (nothing when nobody can)
- [ ] a fix into `staging` only: a new section on top with the next build number instead (`./release.sh next-version`)

`python3 .github/changelog_check.py CHANGELOG.md` runs the same check as CI. See `docs/development.md#changelog`.
