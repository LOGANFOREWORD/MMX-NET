# Feed GitHub — MMX-Net

Due repo:

| Repo | Visibilità | Contenuto | Write |
|------|------------|-----------|-------|
| `LOGANFOREWORD/MMX-NET` | **privata** | codice / toolkit / overlay | solo owner |
| `LOGANFOREWORD/MMX-NET-feed` | **pubblica** | solo `ac_version.json`, `ac_update_manifest.json`, `ac-update.zip` | solo owner |

Gli amici leggono il feed grezzo senza PAT. Non dare Write a nessuno.

## Una tantum (Logan)

1. `toolkit\setup_update_feed_repo.ps1` → crea/collega `MMX-NET-feed` pubblica + `PublishTarget` = `dist\update-feed`
2. `FeedBaseUrl` = `https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/`
3. Non invitare collaboratori Write su nessuna repo

## Amici (`ac_config.json`)

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/",
  "updateFeedToken": ""
}
```

`ac_config.json` **non** viene sovrascritto dagli update overlay.

## Ogni release (Logan)

1. `MMX-Net-Launcher-dev.exe` → **CARICA AGGIORNAMENTO**
2. `toolkit\push_update_feed.ps1` (o auto-push se `PublishTarget` è il clone)
3. Amici con `updateFeedUrl` su `MMX-NET-feed` vedono il popup (HTTP 200, no token)

## Sicurezza

- Mai commitare PAT / source nella repo feed
- Mai dare Write/Admin agli amici su MMX-NET o MMX-NET-feed
