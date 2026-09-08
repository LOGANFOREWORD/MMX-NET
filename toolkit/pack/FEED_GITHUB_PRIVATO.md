# Feed GitHub - MMX-Net

Repo unica pubblica per codice e feed update:

| Repo | Visibilita | Contenuto | Write |
|------|------------|-----------|-------|
| LOGANFOREWORD/MMX-NET | pubblica | codice + ac_version/manifest/zip | solo owner |

Gli amici leggono il feed grezzo senza PAT. Non dare Write a nessuno.

## Setup Logan
1. toolkit/setup_update_feed_repo.ps1 collega PublishTarget = dist/update-feed (clone MMX-NET)
2. FeedBaseUrl = https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/

## Amici (ac_config.json)
updateFeedUrl: https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/
updateFeedToken: (vuoto)

## Flusso publish
1. MMX-Net-Launcher-dev.exe -> CARICA AGGIORNAMENTO
2. toolkit/push_update_feed.ps1 (o auto-push se PublishTarget e clone)
3. Amici con updateFeedUrl su MMX-NET vedono il popup (HTTP 200, no token)

## Regole
- Mai distribuire un PAT forever nello zip
- Mai dare Write/Admin agli amici
- Install vecchi su MMX-NET: il launcher migra a MMX-NET all avvio
