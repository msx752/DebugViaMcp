# Versions and releases

## One version, two places

The extension and the MCP server it carries are released together, with one version number. It is written in two
files and both must say the same thing:

| File | Field | Example |
|---|---|---|
| `Directory.Build.props` | `VersionPrefix` (and `VersionSuffix`) | `0.4.0` |
| `src/DebugViaMcp.Vsix/source.extension.vsixmanifest` | `Identity/@Version` | `0.4.0` |

The Marketplace and Visual Studio read the manifest value. `VersionSuffix` (for example `preview.1`) only labels the
.NET assemblies; the manifest has no suffix, the preview state is the `<Preview>true</Preview>` flag. Remove the flag
for a stable release.

The extension and the server check each other's version at connection time (`VersionMismatch`), so they must not drift.

## Rules

- Semantic Versioning: `MAJOR.MINOR.PATCH`. While the major is `0`, a minor bump may change behaviour.
- **The Marketplace never accepts the same version twice**, not even after a delete. A fix to a published build needs a
  new number (`0.4.0` → `0.4.1`).
- Visual Studio treats an equal version as "already installed". During development use
  `scripts\reinstall-vsix.ps1` (see the README); for a user-visible change bump the version.
- Every released version has a section in `CHANGELOG.md` titled `## <version> — <name>`. The manifest's `ReleaseNotes`
  is the full GitHub URL of `CHANGELOG.md`: the Marketplace reads that field as a URL, and a bare `CHANGELOG.md` is refused as "already in use by another publisher".
- Git tags are `v<version>` (for example `v0.4.0`) and point at the commit on `main` the VSIX was built from. No tag exists
  until the first release.

## Branches and the pipeline

- `develop` is the default branch. `main` is production and takes changes only through a pull request from `develop`
  (direct pushes are blocked, and the `build-test` check must pass).
- **CI** (`.github/workflows/ci.yml`) runs on every pull request (not on plain pushes: a Windows runner counts double against the free minutes, so check a push to `develop` locally with `scripts/build.ps1`): version check, build, all
  tests, and a VSIX build.
- **Release** (`.github/workflows/release.yml`) runs when something is merged into `main`. It checks the version,
  builds and tests, tags `v<version>` on the merge commit, and creates a GitHub Release with `DebugViaMcp.vsix` and
  `SHA256SUMS.txt` (marked pre-release while the manifest has `<Preview>true</Preview>`). The release notes are the
  version's section of `CHANGELOG.md`. If the tag `v<version>` already exists nothing is released: bump the version.
- **Marketplace upload** is the last job of the release workflow. It waits for approval of the `marketplace`
  environment and is skipped unless the repository secret `VS_MARKETPLACE_PAT` exists (Azure DevOps personal access
  token, scope *Marketplace → Manage*, all accessible organizations).
- `scripts\check-version.ps1` fails when `VersionPrefix`, the manifest version and the changelog section disagree.

## Cutting a release

1. Pick the version. Edit it in `Directory.Build.props` and in `source.extension.vsixmanifest`, add the
   `CHANGELOG.md` section (and the listing's "What's new" in `docs/marketplace/listing.md`). Merge that into `develop`.
2. Install a VSIX built from `develop` (`powershell -File scripts/release.ps1`) in Visual Studio 2022 (17.14+) and
   2026 and run through `docs/testing/` once on each.
3. Open a pull request `develop` → `main`, wait for CI, merge it. The release workflow does the rest: tag, GitHub
   Release, and (when enabled) the Marketplace upload after your approval.
4. Without the PAT, upload `DebugViaMcp.vsix` from the GitHub Release at <https://marketplace.visualstudio.com/manage>.
   Manually, `VsixPublisher.exe` is in `<Visual Studio>\VSSDK\VisualStudioIntegration\Tools\Bin\`:

       VsixPublisher.exe publish -payload artifacts\DebugViaMcp.vsix -publishManifest docs\marketplace\publishManifest.json -personalAccessToken <PAT>

**No version numbers in `docs/marketplace/overview.md`.** The overview is pasted by hand into the Marketplace page, so it must 
stay valid across releases: no extension version, no "preview" notice (the Marketplace shows the Preview badge from the manifest 
flag). Version-specific text belongs in `CHANGELOG.md` and in the "What's new" box of `docs/marketplace/listing.md`; paste the 
overview again only when its content (tools, settings, steps) changes.

Two different values, easy to mix up:

- `Identity/@Publisher` in the manifest is the **author name** and must equal the publisher's *display name* in the
  Marketplace (`Mustafa Salih ASLIM`). The upload is refused otherwise.
- `publisher` in `docs/marketplace/publishManifest.json` is the publisher **ID** (`msx752`).

If the display name changes in the Marketplace, change the manifest to match.
