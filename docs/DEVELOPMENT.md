# 🛠️ Development Guide

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Jellyfin Server 10.11.x](https://jellyfin.org/downloads) (Docker or native, for local testing)
- A code editor (VS Code, Rider, or any C# IDE)

---

## Getting Started

```bash
git clone https://github.com/alexisometric/nopaynoplay.git
cd nopaynoplay
dotnet restore
```

---

## Build

### Debug

```bash
dotnet build src/Jellyfin.Plugin.NoPayNoPlay.csproj
```

Output: `src/bin/Debug/net9.0/Jellyfin.Plugin.NoPayNoPlay.dll`

### Release

```bash
dotnet build src/Jellyfin.Plugin.NoPayNoPlay.csproj -c Release
```

Output: `src/bin/Release/net9.0/Jellyfin.Plugin.NoPayNoPlay.dll`

---

## Test

```bash
# Run all tests
dotnet test tests/Jellyfin.Plugin.NoPayNoPlay.Tests.csproj

# Run with verbose output
dotnet test tests/Jellyfin.Plugin.NoPayNoPlay.Tests.csproj -v n

# Run a specific test class
dotnet test tests/Jellyfin.Plugin.NoPayNoPlay.Tests.csproj --filter "FullyQualifiedName~SubscriptionDateTests"
```

> Tests cover: subscription lifecycle, date edge cases (end-of-month, leap year), promo redemption, rate limiting, WebTransformer edge cases, enforcement safety, and localization.

---

## Package

### ZIP archive (Jellyfin format)

```bash
./scripts/build.sh <version>
```

Example:

```bash
./scripts/build.sh 1.4.0.0
```

This creates:
- `artifacts/nopaynoplay_<version>.zip`
- `artifacts/nopaynoplay_<version>.zip.md5`

The ZIP contains the DLL and `meta.json`.

> **Cache-busting** : `build.sh` bakes a **content hash** (12 hex chars of `sha256sum`) of `src/Web/config.js` into the `configurationpage?name=NoPayNoPlayJs&v=` script tag in `config.html` before compiling (the file is restored on exit). The user-facing `client.js`/`qrcode.js` get the same treatment at runtime via `WebAssetVersion` (content hash of the embedded scripts), injected by `WebTransformer`. Using hashes instead of the plugin version means a hotfix that keeps the same version still invalidates stale browser caches.

### Update manifest

```bash
./scripts/update-manifest.sh <version> <zip-url> <md5>
```

Example:

```bash
./scripts/update-manifest.sh 1.4.0.0 https://example.com/nopaynoplay_1.4.0.0.zip abc123...
```

This updates `manifest.json` at the repository root.

---

## Project Structure

```
src/
├── Plugin.cs                    # Plugin entry point, seeding, backups
├── PluginEntryPoint.cs          # File Transformation registration
├── AuthenticationConsumer.cs    # Tracks user authentication events
├── PluginServiceRegistrator.cs  # DI container registration
│
├── Configuration/               # Data models (XML-serialized)
├── Services/                    # Business logic
├── Api/                         # REST API controllers
├── ScheduledTasks/              # Background tasks
├── Localization/                # i18n engine + translation bundles
└── Web/                         # Front-end assets
    ├── client.js                # User UI (injected via FT)
    ├── config.html              # Admin dashboard
    ├── config.js                # Dashboard logic
    ├── qrcode.js                # QR code generator (vendored)
    └── WebTransformer.cs        # FT callback
```

---

## Local Testing with Docker

```bash
# Start a Jellyfin instance with the plugin mounted
docker run -d \
  --name jellyfin-test \
  -p 8096:8096 \
  -v /path/to/your/plugins:/jellyfin/plugins \
  jellyfin/jellyfin:10.11.0

# Or mount a single DLL for quick iteration
docker run -d \
  --name jellyfin-dev \
  -p 8096:8096 \
  -v $(pwd)/src/bin/Debug/net9.0/Jellyfin.Plugin.NoPayNoPlay.dll:/jellyfin/plugins/NoPayNoPlay/Jellyfin.Plugin.NoPayNoPlay.dll \
  jellyfin/jellyfin:10.11.0
```

> 💡 **User UI note:** the plugin's user-facing UI (💳 button, banner, payment modal) is injected into the web app by the external **File Transformation** plugin. To test it locally, install File Transformation from its [releases page](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases), restart Jellyfin, and check **Dashboard → Plugins → NoPayNoPlay** for the green "File Transformation OK" badge. The admin dashboard works regardless of File Transformation.

---

## Adding a New Language

1. Create `src/Localization/strings.{code}.json` (copy `strings.en.json` as a template)
2. Add an `EmbeddedResource` entry in `Jellyfin.Plugin.NoPayNoPlay.csproj`:

```xml
<EmbeddedResource Include="Localization/strings.{code}.json" />
```

3. The plugin auto-discovers the new bundle — no code changes needed.

---

## Code Style

- Use **file-scoped namespaces** (C# 10+)
- Follow **Microsoft's .NET coding conventions**
- XML doc comments on all public APIs
- Use `var` when the type is obvious
- Prefer `is` pattern matching over `==` for null checks

---

## CI/CD

The repository ships with a full CI/CD setup. All workflows are **pinned by commit SHA** and run under **Harden-Runner** for supply-chain security:

| Workflow | Trigger | What it does |
|---|---|---|
| **CI** (`ci.yml`) | Push / PR to `main` (+ manual `workflow_dispatch`) | `restore` → `build -c Release -warnaserror` → `test -c Release --no-build`, plus `actionlint` on all workflows |
| **Release** (`release.yml`) | Tag push `v*.*.*.*` **or** manual `workflow_dispatch` (version + `targetAbi`) | Builds and zips the plugin, computes checksums (MD5/SHA256/size), generates **human release notes** (version-diff link + one commit reference per change), creates the GitHub Release, then updates + validates `manifest.json` and commits it to `main` |
| **Weekly release** (`weekly-release.yml`) | Cron (Mon 05:00 UTC) + manual `workflow_dispatch` | Bumps the version (patch/minor, keeping `<Version>`/`<FileVersion>`/`<AssemblyVersion>` aligned), then triggers the release pipeline automatically when there are new commits |
| **CodeQL** (`codeql.yml`) | Push / PR on `src/**` + weekly cron | Static security analysis |
| **Validate manifest** (`validate-manifest.yml`) | PR touching `manifest.json` + nightly cron | Re-checks `manifest.json` structure, GUID, and the real MD5 of published releases |
| **Scorecard** (`scorecard.yml`) | Weekly cron + push to `main` | OSSF supply-chain score |
| **Dependency audit** (`dependency-audit.yml`) | Weekly cron (Tue 06:00 UTC) + manual | Fails on CVE'd / deprecated NuGet packages, reports outdated ones |
| **Stale** (`stale.yml`) | Daily cron (01:30 UTC) + manual | Flags and closes inactive issues/PRs (Dependabot & `auto-merge` PRs exempt) |
| **Version consistency** (`version-check.yml`) | Push/PR on version files + weekly cron + manual | Enforces csproj / meta.json / manifest.json version alignment |
| **i18n parity** (`i18n-check.yml`) | Push/PR on `src/Localization` + manual | Fails on orphan keys, warns on untranslated keys |
| **Labeler** (`labeler.yml`) | PR events | Auto-labels PRs by changed paths (`ci`, `tests`, `documentation`, `github_actions`) |
| **PR title lint** (`pr-title-lint.yml`) | PR events | Enforces conventional-commit PR titles (Dependabot exempt) |
| **Secret scan** (`secret-scan.yml`) | Push / PR + manual | Gitleaks secret scan |
| **Link check** (`link-check.yml`) | Push on docs + weekly cron + manual | Lychee broken-link check on docs/README |
| **Auto-merge** (`auto-merge.yml`) | Dependabot PR events + daily sweep (safety net, 00:00 UTC) | Merges green Dependabot PRs automatically |

> **Note:** CI builds with `-warnaserror`, so any compiler warning fails the build. Run `dotnet build -c Release -warnaserror` locally before pushing.

### Release process (maintainers)

1. A tag `vX.Y.Z.0` is pushed, **or** `release.yml` is triggered manually with a version and `targetAbi` (the weekly workflow does this automatically).
2. The pipeline builds and zips the plugin, generates the release notes from Conventional Commits (version-diff link + commit references), and creates the GitHub Release.
3. `manifest.json` is updated with the new entry, validated, and committed back to `main` as `chore: publish <ver> in manifest [skip ci]`.

---

## Known Gotchas

### XML Serializer Duplication

`System.Xml.Serialization.XmlSerializer` **appends** to collections during deserialization. Never initialize lists inline in `PluginConfiguration` — use `DefaultsSeeded` flag + `SeedDefaultsAndDedupe()`.

### File Transformation Key Format

Use the **literal** string `"index.html"` (not regex `"index\\.html"`) for the `fileNamePattern`, or FT won't find the exact key match.

### Webpack Chunk Safety

`WebTransformer.LooksLikeHtmlDocument()` prevents corrupting JS chunks that contain `</body>` in inline HTML templates. Only payloads starting with `<!doctype html` or `<html` are transformed.

### CSV Injection

Usernames are escaped with a `'` prefix if they start with `=`, `+`, `-`, `@`, tab, or CR.

### Date Kind

XML deserialization returns `DateTimeKind.Unspecified`. Always normalize with `DateTime.SpecifyKind()` when comparing against `DateTime.UtcNow`.

### Administrators

Are always exempt from enforcement. The `/Me` endpoint returns sample data for admin preview (flagged with `isAdminPreview`).
