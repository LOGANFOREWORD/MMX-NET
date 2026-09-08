# Feed GitHub privato — MMX-Net

Repo dedicata al **solo feed aggiornamenti** (manifest + zip), non al gioco intero.

## Perché un token

`raw.githubusercontent.com` su repo **privata** risponde **404** senza autenticazione.
Il launcher supporta `updateFeedToken` (header `Authorization: Bearer …`) su manifest e download zip.

## Una tantum (Logan)

1. Crea/usa la repo privata `mmx-net-updates`.
2. Clone locale → cartella sync, es. `F:\Anomaly Coop\dist\update-feed`  
   (è il `PublishTarget` in `ac_dev_publish.json`).
3. Imposta `FeedBaseUrl` a:
   `https://raw.githubusercontent.com/<TUO_USER>/mmx-net-updates/main/`
4. Invita gli amici: repo → **Settings → Collaborators** → Add (accesso Read).
5. Crea un PAT fine-grained (Contents: Read sulla repo) e condividilo **in privato** con gli amici
   (o ogni amico crea il proprio PAT dopo accettazione invito).

## Una tantum (amico)

In `ac_config.json` nella root MMX-Net:

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/<USER>/mmx-net-updates/main/",
  "updateFeedToken": "github_pat_…"
}
```

Oppure senza scrivere il token nel file: variabile d’ambiente `AC_UPDATE_FEED_TOKEN`.

`ac_config.json` **non** viene sovrascritto dagli update overlay (il token resta).

## Ogni release (Logan)

1. Apri `MMX-Net-Launcher-dev.exe` → **CARICA AGGIORNAMENTO**  
   (scrive `dist\update\` e copia su `PublishTarget`).
2. Esegui:

```powershell
cd "F:\Anomaly Coop\toolkit"
.\push_update_feed.ps1
```

3. Gli amici all’avvio vedono il popup se la Version remota è maggiore.

## Sicurezza

- Mai commitare PAT / `.env` / `updateFeedToken` nella repo del feed.
- Preferire PAT fine-grained limitato a quella sola repo, scadenza breve.
- Revocare il PAT se un amico esce dal gruppo.
