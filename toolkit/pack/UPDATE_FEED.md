# Feed aggiornamenti MMX-Net

Il launcher legge `updateFeedUrl` da `ac_config.json` e confronta la versione remota con `ac_version.json` locale.

## Config locale (`ac_config.json`)

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/",
  "updateFeedToken": ""
}
```

- `updateFeedUrl`: URL del **manifest** (`.json`) **oppure** cartella base che contiene i file sotto.
- `updateFeedToken`: lasciarlo vuoto con feed pubblico `MMX-NET-feed`. (Opzionale solo se usi ancora un raw privato.)
- `updateChannel`: `dev` / `release` / `any`.
- `checkUpdatesOnStart`: popup automatico all’avvio se c’è una versione più nuova.

Template installer: `toolkit/pack/ac_config.user.json`  
Dopo publish, `FeedBaseUrl` in `ac_dev_publish.json` bake `updateFeedUrl` nel template.

### Repo GitHub (feed)

1. Feed su **`MMX-NET-feed` pubblica** (solo zip/manifest). Codice su **`MMX-NET` privata**.
2. Solo owner **LOGANFOREWORD** ha write su entrambe. Amici: nessun PAT. Vedi `FEED_PRIVATO_AMICI.md`.
3. Dopo **CARICA AGGIORNAMENTO**, Logan esegue `toolkit\push_update_feed.ps1` (o auto-push).
4. Setup: `toolkit\setup_update_feed_repo.ps1`.

### Test locale

1. `publish_ac_update.ps1` genera `dist\update\`.
2. In `ac_config.json`: `"updateFeedUrl": "F:\\Anomaly Coop\\dist\\update\\"`
3. Abbassa la versione locale e riavvia → popup.

## Formato manifest (`ac_update_manifest.json`)

```json
{
  "Name": "MMX-Net",
  "Version": "0.1.2",
  "Channel": "dev",
  "Protocol": "89",
  "Engine": "ST",
  "Notes": "…",
  "PackageUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/ac-update.zip",
  "PackageSha256": "",
  "PublishedUtc": "2026-09-08T00:00:00.0000000Z",
  "MinLauncherVersion": "0.1.0"
}
```

## Contenuto dello zip

Solo **parti nostre**:

- `ac_version.json` (non `ac_config.json` negli update)
- bridge Steam + `steam_appid.txt`
- `MMX-Net-Launcher.exe` (solo user)
- file in `toolkit/pack/ac_pack_include.txt` (xrr_* minimi, DLL net se presenti)

Runtime `xrr_*` / DLL `xr*` restano con nomi engine.

## Pubblicare (Logan)

`MMX-Net-Launcher-dev.exe` → **CARICA AGGIORNAMENTO**, poi `push_update_feed.ps1`.

Oppure:

```powershell
cd "F:\Anomaly Coop\toolkit"
.\publish_ac_update.ps1 -Version 0.1.2 -Notes "Descrizione breve"
.\push_update_feed.ps1
```
