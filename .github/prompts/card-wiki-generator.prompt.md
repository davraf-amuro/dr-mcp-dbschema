---
agent: 'agent'
description: 'Genera la wiki card operativa con valori reali di configurazione per tutti gli ambienti'
tools: ['search/codebase']
---

# Prompt: Wiki Card — Scheda Operativa (AI Agent)

Genera la scheda operativa per condivisione su wiki interna aziendale.
Leggi e riporta i **valori reali** da tutti i file di configurazione disponibili.

> Il file output è sensibile: non committare. Pattern `docs/*-wiki.md` deve essere in `.gitignore`.

## Output
- Crea/aggiorna `docs/card-<nome_progetto>-wiki.md`

## File da analizzare (tutti, in ordine di priorità per colonna)

| Colonna | File sorgente | Note |
|---------|---------------|------|
| Local/Dev | `appsettings.local.json`, `appsettings.Development.json` | Non committati — contengono valori reali |
| Staging | `appsettings.Staging.json`, `appsettings.json` (sezione staging se presente) | |
| Produzione | `appsettings.Production.json` | Non committato |
| Deploy | `docker-compose_swarm.yaml`, `docker-compose*.yaml` | Per variabili env Swarm |

Se un file non esiste: indica `—` nella colonna.
Se un campo contiene placeholder (`CHISSADDOVE`, `CHISSACHI`, `PLACEHOLDER`): indica `[da compilare]`.

## Template wiki card

```markdown
# Wiki Card: [Nome Progetto]

> ⚠️ **DOCUMENTO SENSIBILE** — Contiene credenziali e parametri reali di accesso.
> **Non committare nel repository.** Condividere solo via wiki interna protetta.

**[Tipo Applicazione]** che [descrizione scopo in una riga].

## Contatti Operativi

- **Owner/Team:**
- **Contatto Supporto:** dev-support@unidata.it
- **Escalation:**
- **Repository:** [URL senza branch]

---

## Database

| Parametro | Local/Dev | Staging | Produzione |
|-----------|-----------|---------|------------|
| Chiave Config | `ConnectionStrings__[Nome]` | | |
| Server (Data Source) | | | |
| Database (Initial Catalog) | | | |
| Utente (User Id) | | | |
| Password | | | |
| Connection String completa | | | |

> Nota: la connection string completa permette test immediato con strumenti come SSMS o sqlcmd.

---

## API / Servizi Esterni

Per ogni client HTTP o servizio esterno configurato, una sezione separata:

### [Nome API — es. TnsApi]

| Parametro | Local/Dev | Staging | Produzione |
|-----------|-----------|---------|------------|
| Chiave Config | `[Sezione]__[Campo]` | | |
| Base URL | | | |
| Username | | | |
| Password / Token | | | |
| Account ID / Tenant | | | |
| Timeout (s) | | | |
| [altri parametri specifici] | | | |

---

## Configurazione Workers (se applicabile)

| Worker | Enabled (Local) | Enabled (Prod) | IntervalSeconds | BatchSize | Note |
|--------|----------------|----------------|-----------------|-----------|------|
| [Worker1] | | | | | |

---

## Hosting e Deploy

| Ambiente | Host/Server | Modalità | URL o Porta | Note |
|----------|-------------|----------|-------------|------|
| Local/Dev | localhost | `dotnet run` / Windows Service locale | | |
| Staging | | Docker Swarm (Portainer) | | |
| Produzione | | Docker Swarm (Portainer) | | |

---

## Variabili d'Ambiente Docker (Swarm)

Ricavate da `docker-compose_swarm.yaml`. Queste variabili sovrascrivono appsettings in produzione.

| Variabile ENV | Valore Staging | Valore Produzione |
|---------------|----------------|-------------------|
| | | |

---

## Log Operativi

| File Log | Contenuto | Rolling |
|----------|-----------|---------|
| `logs/service-.log` | Log generale del servizio | Giornaliero |
| `logs/alert-fallback-.log` | Alert critici non recapitati | Giornaliero |
| `logs/[Worker]-.log` | Log per worker specifico | Giornaliero |

---

## Intervento Rapido

### Test connessione DB
```
sqlcmd -S [server] -d [database] -U [utente] -P [password] -Q "SELECT 1"
```

### Test API esterna
```
curl -u [username]:[password] [BaseUrl]/[health-endpoint]
```

### Restart servizio
- **Windows Service:** `sc stop [ServiceName]` poi `sc start [ServiceName]`
- **Docker Swarm (Portainer):** Stack → Servizio → Forza re-deploy

### Portainer
- URL: https://portainer.unidata.it
- Stack: [nome stack]
- Servizi coinvolti: [lista]

---

*Wiki Card v1.0 — {YYYY-MM-DD HH:MM} — {modello-llm} — DOCUMENTO SENSIBILE*
```

## Regole

- Riporta **sempre** i valori reali trovati — questa card serve per operazioni di emergenza
- Un campo senza dati in nessun file: `[non trovato — verificare manualmente]`
- Sezione "API Esterne": una sottosezione per ogni HttpClient / servizio esterno trovato
- Sezione "Variabili Docker": compila solo se `docker-compose_swarm.yaml` è presente e ha variabili di ambiente
- Serilog e configurazioni di logging avanzate: ometti (non operative)
- Workers: includi la sezione solo per progetti Windows Service
- Risposta del prompt: indica solo la card generata, non riepilogare i dati

## ✅ Checklist Post-Generazione

- [ ] File output: `docs/card-<nome>-wiki.md`
- [ ] Header ⚠️ SENSIBILE presente
- [ ] Tabella Database compilata per ogni ambiente trovato
- [ ] Tabella API Esterne con URL e credenziali reali (non placeholder)
- [ ] Sezione Hosting compilata con host reali
- [ ] Variabili Docker ricavate da docker-compose_swarm.yaml (se presente)
- [ ] Sezione Intervento Rapido con comandi concreti (server, db, user reali)
- [ ] Footer con data e LLM presente
- [ ] `docs/*-wiki.md` è in `.gitignore`

*Template v1.0 - Operational Wiki Card - Token-optimized for AI agents* - Last Update 2026-06-11 - claude-sonnet-4-6
