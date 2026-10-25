<!-- Pull requests go into `dev` (a fix found in QA or staging: into that branch, then merged back down with ./release.sh merge-back). See docs/development.md#release-flow. -->
## What changed


## Changelog
- [ ] `src/Directory.Build.props`: `<AssemblyVersion>` bumped (patch for fixes, minor for new things people can do), above `dev`'s, `qa`'s and `staging`'s
- [ ] `CHANGELOG.md`: a `## x.y.z (YYYY-MM-DD)` section at the top following the changelog rule:
  - only **For users** and **For admins**; leave a section out when it has nothing, and leave out work neither sees (tests, build, refactoring)
  - each line leads with the thing that changed in bold, then what it means for the reader, in plain words
  - no code, file names, commands, flags, settings keys or jargon; no "minor fixes" without saying what they fix; no placeholders

`python3 .github/changelog_check.py CHANGELOG.md` runs the same check as CI. See `docs/development.md#changelog`.
