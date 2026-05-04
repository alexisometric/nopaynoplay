# Contribuer à NoPayNoPlay

Merci d'envisager de contribuer ! 🎉

## Avant de commencer

- Vérifie que ton idée n'est pas déjà discutée dans les [issues](https://github.com/alexisometric/nopaynoplay/issues) ou [discussions](https://github.com/alexisometric/nopaynoplay/discussions).
- Pour une grosse fonctionnalité, ouvre une issue de discussion **avant** de coder, pour valider l'approche.

## Workflow

1. Fork le repo et clone-le localement.
2. Crée une branche descriptive :
   ```bash
   git checkout -b feat/export-csv
   git checkout -b fix/notification-rebound
   ```
3. Code en respectant les conventions (cf. ci-dessous).
4. Lance les tests :
   ```bash
   dotnet test
   ```
5. Commits en [Conventional Commits](https://www.conventionalcommits.org/) :
   - `feat:` nouvelle fonctionnalité
   - `fix:` correction de bug
   - `docs:` documentation uniquement
   - `test:` tests uniquement
   - `chore:` maintenance / build / CI
   - `refactor:` refactoring sans changement fonctionnel
6. Push et ouvre une Pull Request vers `main`.

## Conventions de code

### C#

- 4 espaces, `PascalCase` pour les types et méthodes publiques, `_camelCase` pour les champs privés.
- Préférer `var` quand le type est évident à droite du `=`.
- Logger via `ILogger<T>`, jamais `Console.WriteLine`.
- Pas de `null` qui s'échappe d'une API publique : préférer `Optional`/`Try*` ou des valeurs par défaut.
- Les services métier ne doivent **pas** dépendre directement de `Plugin.Instance` — passer la dépendance par DI quand c'est possible.

### JavaScript (côté client injecté)

- 2 espaces, sans framework, vanilla JS uniquement.
- Le script doit être **idempotent** : il peut être appelé plusieurs fois sans dupliquer le DOM.
- Pas de polyfill, pas de transpilation : ES2020 minimum (Jellyfin web cible des navigateurs récents).

### Tests

Toute logique métier non triviale doit être couverte par un test xUnit dans `tests/`.

```bash
dotnet test tests/Jellyfin.Plugin.NoPayNoPlay.Tests.csproj
```

## Tester localement avec Jellyfin

Le moyen le plus simple est Docker :

```bash
docker run -d --name jellyfin-dev \
  -p 8096:8096 \
  -v ./jellyfin-config:/config \
  -v ./jellyfin-cache:/cache \
  jellyfin/jellyfin:10.11.8

# Puis copier le plugin
./scripts/build.sh 1.0.0.0-dev
mkdir -p jellyfin-config/plugins/NoPayNoPlay_1.0.0.0
unzip -o artifacts/nopaynoplay_1.0.0.0-dev.zip \
  -d jellyfin-config/plugins/NoPayNoPlay_1.0.0.0/

docker restart jellyfin-dev
```

## Releases

Les mainteneurs taguent `v<semver>` sur `main`. Le workflow [`release.yml`](.github/workflows/release.yml) s'occupe du reste. Voir [README — Publier une nouvelle version](README.md#-publier-une-nouvelle-version).

## Code of Conduct

Toute participation au projet est régie par le [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
