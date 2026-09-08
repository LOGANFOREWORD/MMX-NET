# Feed GitHub privato — MMX-Net

Repo dedicata al **solo feed aggiornamenti** (manifest + zip), non al gioco intero.

## Perché un token

`raw.githubusercontent.com` su repo **privata** risponde **404** senza autenticazione.
Il launcher supporta `updateFeedToken` (header `Authorization: Bearer …`) su manifest e download zip.

## Una tantum (Logan)

1. Usa la repo privata esistente `anomaly-coop-updates` (non crearne un’altra).
2. Clone locale → `F:\Anomaly Coop\dist\update-feed` (`PublishTarget`).
3. Imposta `FeedBaseUrl` a:
   `https://raw.githubusercontent.com/LOGANFOREWORD/anomaly-coop-updates/main/`
4. Invita gli amici: **Settings → Collaborators** (Read).
5. PAT fine-grained (Contents: Read) condiviso in privato, o ogni amico crea il proprio.

Oppure: `toolkit\setup_update_feed_repo.ps1` (collega la repo esistente).

## Una tantum (amico)

In `ac_config.json` nella root MMX-Net:

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/anomaly-coop-updates/main/",
  "updateFeedToken": "github_pat_…"
}
```

Oppure env `MMX_NET_UPDATE_FEED_TOKEN` / `AC_UPDATE_FEED_TOKEN`.

`ac_config.json` **non** viene sovrascritto dagli update overlay.

## Ogni release (Logan)

1. `MMX-Net-Launcher-dev.exe` → **CARICA AGGIORNAMENTO**
2. `toolkit\push_update_feed.ps1`
3. Gli amici vedono il popup se Version remota > locale.

## Sicurezza

- Mai commitare PAT / `.env` / `updateFeedToken` nella repo del feed.
- Preferire PAT fine-grained limitato a quella sola repo.
