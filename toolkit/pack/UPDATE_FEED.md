# Feed aggiornamenti MMX-Net

Il launcher legge `updateFeedUrl` da `ac_config.json` e confronta la versione remota con `ac_version.json` locale.

## Config locale (`ac_config.json`)

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/OWNER/mmx-net-updates/main/",
  "updateFeedToken": ""
}
```

- `updateFeedUrl`: URL del **manifest** (`.json`) **oppure** cartella base che contiene i file sotto.
- `updateFeedToken`: **PAT GitHub read-only** obbligatorio se il feed è su **repo privata** (`raw.githubusercontent.com` senza auth risponde 404). Non committare il token. Alternativa: env `MMX_NET_UPDATE_FEED_TOKEN` (legacy: `AC_UPDATE_FEED_TOKEN`).
- `updateChannel`: `dev` / `release` / `any` (se diverso dal channel del feed, l’update viene ignorato salvo `any`).
- `checkUpdatesOnStart`: popup automatico all’avvio se c’è una versione più nuova.

Template per pack installer: `toolkit/pack/ac_config.user.json`  
Esempio con feed già valorizzato: `toolkit/pack/ac_config.example_feed.json`  
Dopo publish, Logan mette l’URL del feed in `updateFeedUrl` (o in `ac_dev_publish.json` → `FeedBaseUrl` così l’installer lo precompila).

### Repo GitHub privata (consigliato per amici)

1. Repo dedicata **privata** (solo feed: manifest + zip), non tutto Anomaly.
2. Logan invita gli amici come **Collaborators** (permesso Read).
3. Ogni amico crea un PAT fine-grained con **Contents: Read** solo su quella repo e lo mette in `updateFeedToken`.
4. Il launcher invia `Authorization: Bearer <token>` su download manifest e zip.
5. Dopo **CARICA AGGIORNAMENTO**, Logan esegue `toolkit\push_update_feed.ps1` (commit+push della cartella `PublishTarget`).

Vedi anche `toolkit/pack/FEED_GITHUB_PRIVATO.md`.

### Test locale (senza hosting)

1. `publish_ac_update.ps1` (o Devkit → Pubblica) genera `dist\update\`.
2. Nell’`ac_config.json` di test metti un path assoluto, es.:
   `"updateFeedUrl": "F:\\Anomaly Coop\\dist\\update\\"`
3. Abbassa la versione locale in `ac_version.json` (es. `0.1.0`) e riavvia il launcher → popup.
4. Per gli amici serve un URL **https** pubblico della stessa cartella (non file:// in produzione).

## Formato manifest (`ac_update_manifest.json`)

```json
{
  "Name": "MMX-Net",
  "Version": "0.1.1",
  "Channel": "dev",
  "Protocol": "89",
  "Engine": "ST",
  "Notes": "Fix bridge Steam + health.",
  "PackageUrl": "https://ESEMPIO/raw/main/dist/update/ac-update.zip",
  "PackageSha256": "",
  "PublishedUtc": "2026-09-07T20:00:00.0000000Z",
  "MinLauncherVersion": "0.1.0"
}
```

| Campo | Obbligatorio | Note |
|-------|--------------|------|
| `Version` | sì | Semver `major.minor.patch` — deve essere **maggiore** della locale |
| `Channel` | consigliato | Allineato a `updateChannel` client |
| `PackageUrl` | sì* | URL diretto dello zip overlay |
| `PackageSha256` | no | Se valorizzato, verifica SHA-256 (hex) |
| `Notes` | no | Mostrate nel popup |

\*Se `updateFeedUrl` punta a una **cartella** (senza `.json`) e manca `PackageUrl`, il client prova `…/ac-update.zip`.

## Layout cartella feed (stile MMX)

```
dist/update/
  ac_update_manifest.json
  ac_version.json          (opzionale, utile in fallback)
  ac-update.zip            (overlay: launcher utente + ac_* + gamedata elencati)
```

`updateFeedUrl` può essere:
1. `https://…/ac_update_manifest.json`
2. `https://…/dist/update/` (il client aggiunge `ac_update_manifest.json`)

Fallback: se il manifest non c’è, il client prova `ac_version.json` + `ac-update.zip` nella stessa cartella.

## Contenuto dello zip

Solo **parti nostre**, non redistribuisce `db\` / Anomaly intero:

- `ac_version.json` (non `ac_config.json` negli update — preserva `updateFeedUrl` amici)
- `ac_steam_cop_bridge.cmd`, `ac_steam_launch.args`, `steam_appid.txt`
- `MMX-Net-Launcher.exe` (**solo user**, mai `-dev`)
- file elencati in `toolkit/pack/ac_pack_include.txt` (inclusi, se presenti: script xrr_* minimi, `xrRazom-release.txt`, `bin\steam_api64.dll`, `GameNetworkingSockets.dll` per riparare install incomplete)

## Flusso client (amico)

1. All’avvio (o click ⬇ / AGGIORNA) → fetch manifest  
2. Se remoto > locale → popup «Nuovo aggiornamento — Installare?»  
3. Se Anomaly è aperto → avvisa di chiudere  
4. Download zip → applica overlay → aggiorna `ac_version.json` → chiede riavvio launcher  

## Pubblicare (Logan)

**Consigliato:** `MMX-Net-Launcher-dev.exe` → pannello **CARICA AGGIORNAMENTO** (one-click).

Il devkit:
1. Rileva modifiche vs `ac_dev_last_publish.json` (badge + REFRESH)
2. Propone bump patch se serve (Version remota deve essere **>** locale amici)
3. Scrive `dist\update\`, copia su `PublishTarget`, bake `FeedBaseUrl` nel template user

### Prima volta

Imposta in `ac_dev_publish.json` (o nel dialog al primo Carica):

- `FeedBaseUrl` — es. GitHub raw della cartella `dist/update/`
- e/o `PublishTarget` — cartella sync condivisa

### Checklist popup amici

1. **Carica** → `dist\update\` fresco con Version bumpata  
2. File online/sync sulla destinazione del feed  
3. Amici con `updateFeedUrl` = quel feed e `checkUpdatesOnStart: true`  
4. All’avvio: popup se Version remota > locale  

Da script:

```powershell
cd "<root>\toolkit"
.\publish_ac_update.ps1 -Version 0.1.2 -Notes "Descrizione breve"
```
