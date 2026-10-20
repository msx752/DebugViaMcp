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

## Cutting a release

1. Pick the version. Edit it in `Directory.Build.props` and in `source.extension.vsixmanifest`.
2. Add the `CHANGELOG.md` section; move the listing's "What's new" text in `docs/marketplace/listing.md` along with it.
3. Build and test: `powershell -File scripts\release.ps1`. It writes `artifacts\DebugViaMcp.vsix` and
   `artifacts\SHA256SUMS.txt` (`artifacts\` is git-ignored; nothing is published).
4. Install the VSIX in Visual Studio 2022 (17.14+) and in Visual Studio 2026 and run through
   `docs/testing/` once on each.
5. Commit on `develop`, merge `develop` into `main` (`main` is production), then tag the merge commit on `main`: `git tag v<version>` and `git push origin v<version>`.
6. Publish `artifacts\DebugViaMcp.vsix` to the Marketplace: upload it at <https://marketplace.visualstudio.com/manage>
   or run

       VsixPublisher.exe publish -payload artifacts\DebugViaMcp.vsix -publishManifest docs\marketplace\publishManifest.json -personalAccessToken <PAT>

   (`VsixPublisher.exe` is in `<Visual Studio>\VSSDK\VisualStudioIntegration\Tools\Bin\`.)

Two different values, easy to mix up:

- `Identity/@Publisher` in the manifest is the **author name** and must equal the publisher's *display name* in the
  Marketplace (`Mustafa Salih ASLIM`). The upload is refused otherwise.
- `publisher` in `docs/marketplace/publishManifest.json` is the publisher **ID** (`msx752`).

If the display name changes in the Marketplace, change the manifest to match.
