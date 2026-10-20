# Contributing

Thanks for helping. This is a Visual Studio extension (VS 2022 17.14+ and VS 2026) plus a .NET MCP server.

## Build and test

Requires the .NET 10 SDK and Visual Studio 2022 17.14+ or 2026 (for the VSIX).

    powershell -File scripts\build.ps1

It builds `DebugViaMcp.Core.slnf`, runs all tests, then builds the VSIX with Visual Studio's MSBuild. Use `-SkipVsix` to
skip the VSIX. See the README for `scripts\reinstall-vsix.ps1` while developing.

## Ground rules

- The dependency rule is enforced by `ArchitectureTests`: `Vsix.Core` has no Visual Studio types; `Vsix` holds thin
  adapters only.
- Anything that lets the agent change program state must go through the consent gate (see `docs/security.md`). A change
  that weakens a default needs a strong reason in the pull request.
- Add or update tests with the change; the suite must pass.
- Update `CHANGELOG.md` for user-visible changes. Versions and releases: [docs/releasing.md](docs/releasing.md).
- Commit messages follow Conventional Commits (`feat(vsix): ...`, `fix: ...`, `docs: ...`).

## Branches

- `develop` is the default branch: work and pull requests go here.
- `main` is production and protected: it only changes through a pull request from `develop` (CI must pass). Merging it releases: the pipeline tags `v<version>` and publishes the VSIX (see [docs/releasing.md](docs/releasing.md)).

## Pull requests

Open them against `develop`. Keep them focused, describe what you ran (tests, and which Visual Studio version if the change touches the extension), and
link the issue. Security problems go through [SECURITY.md](SECURITY.md), not a public issue.
