# MMX-Net — aggiornamenti amici

**Token forever / PAT nello zip: non serve.** Serve solo il feed pubblico.

```json
{
  "checkUpdatesOnStart": true,
  "updateChannel": "dev",
  "updateFeedUrl": "https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/",
  "updateFeedToken": ""
}
```

- `MMX-NET` = codice (dovrebbe restare **privata**)
- `MMX-NET-feed` = **pubblica**, solo zip/manifest

Se un install punta ancora a `.../MMX-NET/main/`, il launcher all'avvio migra da solo a `MMX-NET-feed`.
