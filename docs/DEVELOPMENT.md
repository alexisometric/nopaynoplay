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
./scripts/build.sh 1.2.7.0
```

This creates:
- `artifacts/nopaynoplay_<version>.zip`
- `artifacts/nopaynoplay_<version>.zip.md5`

The ZIP contains the DLL and `meta.json`.

### Update manifest

```bash
./scripts/update-manifest.sh <version> <zip-url> <md5>
```

Example:

```bash
./scripts/update-manifest.sh 1.2.7.0 https://example.com/nopaynoplay_1.2.7.0.zip abc123...
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

| Workflow | File | Trigger |
|---|---|---|
| **CI** | `.github/workflows/ci.yml` | Push/PR to `main` |
| **Release** | `.github/workflows/release.yml` | Tag push (v*) |

CI runs:
- `dotnet restore`
- `dotnet build -c Release`
- `dotnet test -c Release --no-build`

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
