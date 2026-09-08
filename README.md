# MMX-Net

Mod coop basata su **Anomaly 1.5.3 + xrRazom** (Steam P2P, App ID 41700), con launcher/pack/update feed propri.

Non è un dump completo di Anomaly: in workspace c’è **overlay + source** che controlliamo.

## Inventario

| Nostro | Upstream / locale (escluso o solo riferimento) |
|--------|------------------------------------------------|
| `toolkit/MmxNetLauncher` | `bin/` engine Anomaly |
| `toolkit/MmxNetInstaller` | `db/` (~17 GB) |
| `toolkit/*.ps1`, `toolkit/pack/` | `tools/` unpack cache |
| `ac_*.json`, bridge Steam | `appdata/` saves/logs |
| `gamedata/` overlay (script `xrr_*` + config) | `MT/` overlay engine opzionale |
| `xrRazom-release.txt`, `fsgame.ltx`, `steam_appid.txt` | |

Protocollo pack resta `ac_*` (`ac_config.json`, `ac-update.zip`, `ac_update_manifest.json`) per non rompere il feed.

Runtime engine resta `xrr_*` / DLL `xr*` / `AnomalyLauncher.exe` — non rinominati (compatibilità).

## Build launcher

```powershell
cd "F:\Anomaly Coop\toolkit"
.\deploy_mmx_net_launcher.ps1
```

Produce `MMX-Net-Launcher.exe` (user) e `MMX-Net-Launcher-dev.exe` (devkit publish).

## Feed aggiornamenti

Repo privata (esistente): [`LOGANFOREWORD/anomaly-coop-updates`](https://github.com/LOGANFOREWORD/anomaly-coop-updates)

- Amici: Collaborator + PAT read-only in `ac_config.json` → `updateFeedToken` (o env `MMX_NET_UPDATE_FEED_TOKEN`)
- Dev: CARICA AGGIORNAMENTO nel launcher-dev, poi `toolkit\push_update_feed.ps1`

## Note

- Non toccare Call to Arms / Modern Military.
- Fingerprint xrRazom: tutti sullo stesso pack + stessa base.
- Overlay Steam: HOST avvia da shortcut libreria «Call of Pripyat — MMX-Net».
