# MMX-Net Launcher

| Exe | Ruolo |
|-----|--------|
| `MMX-Net-Launcher.exe` | Utente — check/download update + HOST/PLAY |
| `MMX-Net-Launcher-dev.exe` | Devkit — detect modifiche + Carica aggiornamento |

```powershell
cd "F:\Anomaly Coop\toolkit"
.\deploy_mmx_net_launcher.ps1
.\build_ac_installer.ps1
```

Feed: repo privata `LOGANFOREWORD/anomaly-coop-updates`  
`FeedBaseUrl` = `https://raw.githubusercontent.com/LOGANFOREWORD/anomaly-coop-updates/main/`

Dopo CARICA: `.\push_update_feed.ps1`

Dettagli: `toolkit/pack/UPDATE_FEED.md`, `FEED_GITHUB_PRIVATO.md`.
