# Feed aggiornamenti MMX-Net

Il launcher legge `updateFeedUrl` da `ac_config.json` e confronta la versione remota con `ac_version.json` locale.

## Config locale (`ac_config.json`)

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/",
  "updateFeedToken": ""
}
```

- `updateFeedUrl`: URL del **manifest** (`.json`) **oppure** cartella base che contiene i file sotto.
- `updateFeedToken`: **PAT GitHub read-only** obbligatorio se il feed è su **repo privata**. Non committare il token. Alternativa: env `MMX_NET_UPDATE_FEED_TOKEN` (legacy: `AC_UPDATE_FEED_TOKEN`).
- `updateChannel`: `dev` / `release` / `any`.
- `checkUpdatesOnStart`: popup automatico all’avvio se c’è una versione più nuova.

Template installer: `toolkit/pack/ac_config.user.json`  
Dopo publish, `FeedBaseUrl` in `ac_dev_publish.json` bake `updateFeedUrl` nel template.

### Repo GitHub privata

1. Usa la repo esistente **`MMX-NET`** (prodotto + feed: source overlay, toolkit, manifest + zip).
2. Logan invita gli amici come **Collaborators** (Read).
3. Ogni amico crea un PAT fine-grained (Contents: Read) → `updateFeedToken`.
4. Dopo **CARICA AGGIORNAMENTO**, Logan esegue `toolkit\push_update_feed.ps1`.

Vedi `toolkit/pack/FEED_GITHUB_PRIVATO.md`.

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
  "PackageUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/ac-update.zip",
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
