# Contributing to NoPayNoPlay

Thanks for considering a contribution! This document keeps things lightweight.

## Reporting bugs / requesting features

Open an [issue](https://github.com/alexisometric/nopaynoplay/issues/new/choose) using the appropriate template. For bugs, include the Jellyfin version, the plugin version, the relevant log lines and the steps to reproduce.

## Local development

```bash
git clone https://github.com/alexisometric/nopaynoplay.git
cd nopaynoplay
dotnet restore
dotnet build src/Jellyfin.Plugin.NoPayNoPlay.csproj -c Release
dotnet test tests/Jellyfin.Plugin.NoPayNoPlay.Tests.csproj
```

You will need:

- .NET SDK 9.0+
- A local Jellyfin Server 10.11.x (Docker is the easiest)

## Branches & commits

- Branch off `main`: `feat/<topic>`, `fix/<topic>`, `docs/<topic>`, `chore/<topic>`.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/) — `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`.

## Code style

- **C#**: Microsoft conventions (4 spaces, `PascalCase`, `var` when type is obvious).
- **JS / HTML / JSON / YAML**: 2 spaces.
- Run `dotnet build -warnaserror` before pushing.

## Tests

- New business logic must come with an xUnit test in `tests/`.
- The public static `SubscriptionService.ComputeNextExpiry` is part of the test surface — keep it stable.
- Run the full suite with `dotnet test`.

## Localization (i18n)

- The English bundle (`src/Localization/strings.en.json`) is the **source of truth**.
- When adding a new user-facing string:
  1. add the key to `strings.en.json`,
  2. mirror it in every other bundle (`strings.fr.json`, …) — even if you copy the English value,
  3. consume it through the `Localizer` (server) or `data.strings[key]` / `data-i18n` (client).
- New language? Add `strings.<code>.json` and register it as an `EmbeddedResource` in the csproj.

## Pull requests

- Keep PRs small and focused on a single concern.
- Include a short description of *what* changes and *why*.
- Make sure CI is green before requesting review.
- By contributing, you agree to release your work under the [MIT license](LICENSE).

## Code of Conduct

Participation in this project is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).
