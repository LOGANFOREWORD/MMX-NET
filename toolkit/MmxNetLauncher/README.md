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

Feed update: repo **pubblica** `LOGANFOREWORD/MMX-NET-feed` (solo manifest + zip)  
`FeedBaseUrl` = `https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/`  
Prodotto: `MMX-NET` **privata**. Solo owner write su entrambe. Amici: nessun PAT.

Dopo CARICA: auto-push se PublishTarget è clone git; altrimenti `.\push_update_feed.ps1`.  
Setup feed: `.\setup_update_feed_repo.ps1`

**Popup + AGGIORNA giallo:** all'avvio (o su AGGIORNA), se remoto > locale → MessageBox; poi AGGIORNA resta stile accent (giallo come PLAY) finché non applichi.  
Test UI senza abbassare `ac_version`: in PowerShell DEVKIT  
`$env:MMX_NET_SIMULATE_UPDATE='1'; .\MMX-Net-Launcher-dev.exe`  
(poi togli la variabile). Non lasciare versione locale rotta.

Dettagli: `toolkit/pack/UPDATE_FEED.md`, `FEED_PRIVATO_AMICI.md`, `FEED_GITHUB_PRIVATO.md`.
