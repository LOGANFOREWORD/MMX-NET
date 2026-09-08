# Feed aggiornamenti MMX-Net

Il launcher legge updateFeedUrl da ac_config.json e confronta la versione remota con ac_version.json locale.

## Config amici
updateFeedUrl: https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET/main/
updateFeedToken: (vuoto)

- Feed pubblico MMX-NET: nessun PAT.
- Install vecchi su MMX-NET: il launcher migra a MMX-NET all avvio.
- Dopo publish, FeedBaseUrl in ac_dev_publish.json bake updateFeedUrl nel template.

## Repo GitHub
1. Feed e codice sulla stessa repo MMX-NET pubblica.
2. Solo owner LOGANFOREWORD ha write. Amici: nessun PAT. Vedi FEED_PRIVATO_AMICI.md
3. Dopo CARICA AGGIORNAMENTO, Logan esegue toolkit/push_update_feed.ps1 (o auto-push).
4. Setup: toolkit/setup_update_feed_repo.ps1

## Test locale
In ac_config.json: updateFeedUrl verso F:/Anomaly Coop/dist/update/

## Pubblicare (Logan)
1. Modifiche solo in locale su `F:\Anomaly Coop` (DEVKIT: `MMX-Net-Launcher-dev.exe`).
2. Imposta tu la versione in `ac_version.json` / casella Versione (auto-bump default **off**).
3. CARICA AGGIORNAMENTO: pack da install locale; nello zip solo `MMX-Net-Launcher.exe` (mai `-dev`).
4. OutDir/PublishTarget da `ac_dev_publish.json` (relativi alla root install).
5. Push feed: auto se PublishTarget è clone git, altrimenti `toolkit/push_update_feed.ps1`.
6. Amici: ApplyUpdate aggiorna launcher user; LegacyCleanup non cancella `-dev` su PC Logan.
