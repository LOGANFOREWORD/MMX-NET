# MMX-Net Launcher

Due exe dallo stesso progetto (`BuildFlavor=User|Dev`):

| Exe | Ruolo |
|-----|--------|
| `MMX-Net-Launcher.exe` | **Utente** — check/download update + HOST/PLAY |
| `MMX-Net-Launcher-dev.exe` | **Devkit Logan** — detect modifiche + **Carica aggiornamento** one-click |

## Deploy (Logan)

```powershell
cd "<root MMX-Net>\toolkit"
.\deploy_anomaly_coop_launcher.ps1
# oppure full pack:
.\build_ac_installer.ps1
```

Root rilevata automaticamente dalla cartella `toolkit\` (niente path fisso obbligatorio).

Output tipici:
- `<root>\MMX-Net-Launcher.exe` / `-dev.exe`
- `<root>\dist\devkit\`, `dist\installer\`, `dist\update\`, `dist\download\MmxNet-Download.zip`

## Flusso Logan → amici (Carica one-click)

### Prima volta (una sola) — repo GitHub **privata**

1. Autenticati: `gh auth login --web` (browser).
2. Esegui `toolkit\setup_update_feed_repo.ps1` → crea `mmx-net-updates` (private), clone in `dist\update-feed`, scrive `FeedBaseUrl` / `PublishTarget`.
3. Invita gli amici come Collaborators (Read). Ogni amico mette un PAT in `ac_config.json` → `updateFeedToken`.
4. Dettagli: `toolkit/pack/FEED_GITHUB_PRIVATO.md`.

In alternativa manuale nel Devkit:

1. Avvia **`MMX-Net-Launcher-dev.exe`**.
2. Imposta **almeno uno** tra:
   - **FeedBaseUrl** — es. `https://raw.githubusercontent.com/<user>/mmx-net-updates/main/`
   - **PublishTarget** — clone locale del feed (`dist\update-feed`)
3. Se mancano entrambi, **Carica** chiede URL/cartella e salva in `ac_dev_publish.json`.

Esempio `ac_dev_publish.json` (root):

```json
{
  "FeedBaseUrl": "https://raw.githubusercontent.com/USER/mmx-net-updates/main/",
  "PublishTarget": "F:\\Anomaly Coop\\dist\\update-feed",
  "OutDir": "dist\\update",
  "AbsolutePackageUrl": true,
  "AutoBumpPatch": true
}
```

- `FeedBaseUrl` viene baked in `toolkit/pack/ac_config.user.json` → `updateFeedUrl` (+ `checkUpdatesOnStart: true`) al prossimo installer.
- Snapshot hash dei file pack: `ac_dev_last_publish.json` (creato dopo ogni Carica riuscita).
- Client amici: `updateFeedToken` (PAT) obbligatorio su repo privata — mai nel git del feed.

### Ogni volta che modifichi overlay/pack

1. Modifica file elencati in `toolkit\pack\ac_pack_include.txt` (o root AC).
2. Apri il **devkit** → badge **«Ci sono modifiche non pubblicate»** (o **REFRESH**).
3. Opzionale: lascia **Auto-bump patch** (0.1.1 → 0.1.2) e note changelog.
4. **CARICA AGGIORNAMENTO**:
   - scrive `dist\update\` (`ac-update.zip`, `ac_update_manifest.json`, `ac_version.json`)
   - copia su `PublishTarget` se impostato
   - aggiorna snapshot + template feed
5. Push feed: `toolkit\push_update_feed.ps1`
6. MessageBox: *«Pubblicato. Gli amici con updateFeedUrl=… vedranno il popup all'avvio.»*

Da script (equivalente):

```powershell
.\publish_ac_update.ps1 -Version 0.1.2 -Notes "Changelog"
.\push_update_feed.ps1
```

## Come l’amico vede il popup

1. Ha `ac_config.json` con `updateFeedUrl` = URL del feed, `checkUpdatesOnStart: true`, e su repo privata anche `updateFeedToken`.
2. Avvia `MMX-Net-Launcher.exe` → fetch `ac_update_manifest.json` (con Bearer token se presente).
3. Se **Version remota > locale** → popup «Nuovo aggiornamento — Installare?».
4. Download zip → overlay → aggiorna `ac_version.json`.

Test locale senza hosting: `updateFeedUrl` = path assoluto a `dist\update\` (vedi `toolkit/pack/UPDATE_FEED.md`).

## Installer per amici

```powershell
.\build_ac_installer.ps1
```

Pacchetto in `dist\installer\`:
- `MMX-Net-Installer.exe` + `ac-overlay.zip` (+ `LEGGIMI.txt`)
- Overlay include **solo** `MMX-Net-Launcher.exe` (user), non il -dev
- Cartella install consigliata: **«MMX-Net»** a parte (non vanilla)

## HOST / PLAY

Invariati: Steam Call of Pripyat (41700), Shift+Tab invite. Niente dedicated / CREATE SERVER. Detect/Carica non toccano HOST/PLAY.

Documentazione feed: `toolkit\pack\UPDATE_FEED.md`
