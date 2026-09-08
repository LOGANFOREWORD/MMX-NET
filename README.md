# MMX-Net

Mod coop su **Anomaly 1.5.3** (Steam P2P, App ID 41700), con launcher/pack/update feed propri.

Non è un dump completo di Anomaly: in workspace c’è **overlay + source** che controlliamo.

## Inventario

| Nostro | Upstream / locale (escluso o solo riferimento) |
|--------|------------------------------------------------|
| `toolkit/MmxNetLauncher` | `bin/` engine Anomaly |
| `toolkit/MmxNetInstaller` | `db/` (~17 GB) |
| `toolkit/*.ps1`, `toolkit/pack/` | `tools/` unpack cache |
| `ac_*.json`, bridge Steam | `appdata/` saves/logs |
| `gamedata/` overlay (script `xrr_*` + config) | `MT/` overlay engine opzionale |
| file di release protocollo, `fsgame.ltx`, `steam_appid.txt` | |

Protocollo pack resta `ac_*` (`ac_config.json`, `ac-update.zip`, `ac_update_manifest.json`) per non rompere il feed.

Runtime engine resta `xrr_*` / DLL net / `AnomalyLauncher.exe` — identificatori tecnici non rinominati (compatibilità protocollo).

## Build launcher

```powershell
cd "F:\Anomaly Coop\toolkit"
.\deploy_mmx_net_launcher.ps1
```

Produce `MMX-Net-Launcher.exe` (user) e `MMX-Net-Launcher-dev.exe` (devkit publish).

## Feed aggiornamenti

- **Prodotto:** [LOGANFOREWORD/MMX-NET](https://github.com/LOGANFOREWORD/MMX-NET) **privata** (solo owner write)
- **Feed update:** [LOGANFOREWORD/MMX-NET](https://github.com/LOGANFOREWORD/MMX-NET) **pubblica** (solo manifest + zip; nessun source)
- `updateFeedUrl`: `https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/` — amici **senza PAT**
- Setup: `toolkit\setup_update_feed_repo.ps1` · push: `toolkit\push_update_feed.ps1`
- Istruzioni: `FEED_PRIVATO_AMICI.md` · dettagli: `toolkit/pack/FEED_GITHUB_PRIVATO.md`
- Non dare write a nessuno su nessuna repo; non rimettere MMX-NET pubblica

## Note

- Non toccare Call to Arms / Modern Military.
- Fingerprint: tutti sullo stesso pack + stessa base.
- Overlay Steam: HOST avvia da shortcut libreria «Call of Pripyat — MMX-Net».
