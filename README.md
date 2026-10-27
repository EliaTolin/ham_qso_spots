# ham_qso_spots

Server degli spot di HamQSO (F24): raccoglie gli spot da nodi DX cluster, HamQTH, DX Summit, POTA, SOTA e WWFF, toglie i doppioni, li salva in Postgres (7 giorni) e li pubblica su MQTT (Mosquitto) per le app. Calcola anche gli avvisi.

- .NET 11, architettura pulita, SOLID.
- Ospitato su Coolify, dominio `spot.hamqso.app`.
- Nota del backlog: `../Backlog/Funzionalità/F24 Flusso spot centralizzato su Supabase Realtime.md`.
