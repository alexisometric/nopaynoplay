# NoPayNoPlay — plugin Jellyfin

Suivi automatisé d'un abonnement mensuel à validation manuelle pour Jellyfin **10.11.x** (.NET 8).

- Tarif et liens de paiement configurables (PayPal.me, Lydia, RIB).
- Date anniversaire (jour du mois) respectée à chaque renouvellement.
- À l'expiration (+ délai de grâce) : la `UserPolicy` est modifiée pour désactiver
  la lecture audio/vidéo. Le compte n'est jamais supprimé.
- Tâche planifiée toutes les 12 h.
- Page admin colorée (vert/orange/rouge/gris exempté) avec boutons « Valider paiement »,
  « Exempter », « Reset essai ».
- UI utilisateur (bouton header + modal + bannière à J-3 / grâce / blocage) injectée
  via le plugin tiers [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation).
- Backup horodaté du fichier de configuration avant chaque écriture (rétention 10).
- Notifications cloche Jellyfin à J-3 et au moment du blocage.
- Essai gratuit (7 jours par défaut) à la première connexion.
- Exemption manuelle par utilisateur (accès gratuit illimité).

## Prérequis

- Jellyfin Server **10.11.8** ou plus récent en 10.11.x.
- .NET SDK **8.0+** pour la compilation.
- Plugin [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
  installé sur le serveur (sinon la partie API/admin fonctionne mais l'UI utilisateur ne s'affiche pas).

## Compilation

```bash
cd Jellyfin.Plugin.NoPayNoPlay
dotnet restore
dotnet build src/Jellyfin.Plugin.NoPayNoPlay.csproj -c Release
```

Le binaire est produit dans `src/bin/Release/net8.0/Jellyfin.Plugin.NoPayNoPlay.dll`.
### Tests

```bash
dotnet test tests/Jellyfin.Plugin.NoPayNoPlay.Tests.csproj
```

### Packaging local

```bash
./scripts/build.sh 1.0.0.0
# -> artifacts/nopaynoplay_1.0.0.0.zip + .md5
```

## Installation via dépôt Jellyfin (recommandé)

Une fois le repo GitHub publié et une release créée, l'installation depuis Jellyfin se fait en quelques clics :

1. Ouvrir **Tableau de bord → Plugins → Dépôts → +**.
2. Renseigner :
   - **Nom du dépôt** : `NoPayNoPlay`
   - **URL du dépôt** : `https://raw.githubusercontent.com/alexisometric/nopaynoplay/main/manifest.json`
3. Aller dans **Catalogue**, retrouver « NoPayNoPlay » et cliquer sur **Installer**.
4. Redémarrer Jellyfin.
5. Configurer le plugin dans **Tableau de bord → Plugins → NoPayNoPlay**.

> Le dépôt est public et hébergé sur GitHub : https://github.com/alexisometric/nopaynoplay

### Publier une nouvelle version

```bash
git tag v1.0.1.0
git push origin v1.0.1.0
```

Le workflow GitHub Actions [`release.yml`](.github/workflows/release.yml) :

1. exécute les tests xUnit ;
2. compile la DLL et empaquette le zip Jellyfin ;
3. crée la release GitHub avec l'asset ;
4. met à jour `manifest.json` (checksum md5, URL, timestamp) et le commit sur `main`.

Jellyfin détectera automatiquement la nouvelle version au prochain rafraîchissement (toutes les heures, ou via le bouton « Actualiser » du dépôt).

## Installation manuelle

1. Créer le dossier `<jellyfin-data>/plugins/NoPayNoPlay_1.0.0.0/`.
2. Y copier `Jellyfin.Plugin.NoPayNoPlay.dll` et `meta.json`.
3. Redémarrer Jellyfin.
4. Ouvrir **Tableau de bord → Plugins → NoPayNoPlay** pour configurer les tarifs et liens.
5. (Optionnel) Forcer un cycle immédiat depuis **Tableau de bord → Tâches planifiées →
   « NoPayNoPlay - Vérification des abonnements »**.

## Endpoints REST

| Méthode | URL                                    | Auth   | Description                              |
|---------|----------------------------------------|--------|------------------------------------------|
| GET     | `/NoPayNoPlay/Me`                      | user   | État + infos paiement de l'utilisateur   |
| GET     | `/NoPayNoPlay/Users`                   | admin  | Liste enrichie des souscriptions         |
| POST    | `/NoPayNoPlay/Users/{id}/Pay`          | admin  | Enregistre un paiement, étend l'échéance |
| POST    | `/NoPayNoPlay/Users/{id}/Exempt`       | admin  | Active/retire l'exemption                |
| POST    | `/NoPayNoPlay/Users/{id}/Reset`        | admin  | Réinitialise à un essai                  |
| GET     | `/NoPayNoPlay/Settings`                | admin  | Paramètres globaux                       |
| POST    | `/NoPayNoPlay/Settings`                | admin  | Mise à jour des paramètres               |
| GET     | `/NoPayNoPlay/Web/client.js`           | public | Script client injecté                    |

## Stockage

La configuration est persistée par Jellyfin dans
`<jellyfin-data>/plugins/configurations/f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f.xml`.
Les backups sont dans `<jellyfin-data>/plugins/configurations/NoPayNoPlay.backups/`.
