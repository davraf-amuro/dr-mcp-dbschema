---
agent: 'agent'
description: 'Genera la scheda riassuntiva per progetti Windows Service .NET 10'
tools: ['search/codebase']
---

# Prompt: Card Worker Service Project (AI Agent)

Genera la scheda riassuntiva per un progetto Windows Service (.NET Worker Service). Non inventare dati. Lascia vuoto se non trovi info.

## Output
- Crea/aggiorna `docs/card-<nome_progetto>.md`

## Analisi da eseguire
- `.csproj`, `appsettings*.json`
- `Program.cs`: worker registrati, opzioni configurate
- `Workers/*.cs`: ciclo `ExecuteAsync`, `StagingWorkerBase<T>` o `BackgroundService`
- `Workers/Options/*.cs`: campi `Enabled`, `IntervalSeconds`, `BatchSize`
- `DbContext`, entity, servizi HTTP consumati

## Template card
```markdown
# Card: [Nome Progetto]

**Windows Service** che [descrizione scopo in una riga].
Opera in background con [N] worker autonomi e ciclici: `[Worker1]`, `[Worker2]`, ...

## Identificazione
- **Progetto:**
- **Solution:** [NomeSolution.sln]
- **Workspace:** [NomeWorkspace.code-workspace]
- **Repository:** [URL senza branch]
- **Tipo Applicazione:** Windows Service (.NET Worker Service)
- **Pattern Architetturale:** BackgroundService + IServiceScopeFactory + Typed HttpClient
- **Versione Corrente:**
- **Owner/Team:**
- **Referente:** [nome referente]
- **Contatto Supporto:** dev-support@unidata.it

## Stack Tecnologico
- **Linguaggio Principale:** C# 14
- **Framework:** .NET 10 Worker Service
- **Target Framework:** net10.0
- **SDK Version:** Microsoft.NET.Sdk.Worker

## Architettura Worker

Ogni worker estende `[BaseClass]` e opera in loop periodico con intervallo configurabile.

| Worker | Entity/Scope | Operazione/Endpoint |
|--------|--------------|---------------------|
| `[Worker1]` | `[Entity1]` | `[endpoint o descrizione operazione]` |

**Ciclo di elaborazione per worker:**
1. [passo 1]
2. [passo 2]
3. ...

## Dipendenze

### Progetti Interni
-

### Pacchetti Esterni
| Pacchetto | Versione | Scopo |
|-----------|----------|-------|
| ... | ... | ... |

## Database
| Connection String Key | Nome Database | Tipo | Server/Host | Username | Provider/ORM |
|-----------------------|---------------|------|-------------|----------|--------------|
| ... | ... | ... | da `appsettings.local.json` | da `appsettings.local.json` | EF Core 10 |

## Servizi Esterni
| Tipo | Nome/Endpoint | Protocollo | Autenticazione | Scopo/Descrizione |
|------|---------------|------------|----------------|-------------------|
| ... | ... | ... | ... | ... |

## Configurazione e Hosting
- **Entrypoint:** `src/<progetto>/Program.cs`
- **Deploy:** Windows Service (`sc create` / `sc start`)
- **Ambiente Test:** [percorso fisico | nome server | servizio Windows su ... | non pubblicato]
- **Ambiente Produzione:** [percorso fisico | nome server | servizio Windows su ... | non pubblicato]
- **Logging:** [struttura file log per worker, rolling giornaliero]

## Configurazione Worker

Ogni worker ha una sezione dedicata in `appsettings.json` sotto `Workers`:

| Chiave | Tipo | Default | Descrizione |
|--------|------|---------|-------------|
| `Enabled` | bool | `true` | Abilita/disabilita il worker |
| `IntervalSeconds` | int | — | Pausa tra un ciclo e il successivo |
| `BatchSize` | int | — | Numero massimo di record per ciclo |

---
*Revisione v1.0 — {YYYY-MM-DD HH:MM} — {modello-llm}*
```

## Regole
- Non inventare dati; campi senza info restano vuoti
- Tabelle senza dati: lascia solo header
- Info sensibili: indica solo il nome variabile, mai il valore
- `Referente`, `Ambiente Test` e `Ambiente Produzione` non possono essere vuoti: se l'utente non fornisce il dato, usa `non pubblicato`
- Ciclo di elaborazione: ricava dai commenti e dalla logica di `DoWorkAsync` / `ExecuteAsync`
- Se non esiste sezione `BatchSize` nelle Options, rimuovi la riga dalla tabella
- Risposta del prompt: indica solo la card generata, non riepilogare i dati

## ✅ Checklist Post-Generazione
- [ ] `docs/` contiene la card
- [ ] Scopo dichiarato nelle due righe sotto il titolo
- [ ] Tabella Architettura Worker compilata con tutti i worker trovati
- [ ] Ciclo di elaborazione descritto con i passi reali del codice
- [ ] Tabella Configurazione Worker con i campi effettivi delle Options
- [ ] Nessun segreto esposto
- [ ] Footer con data e LLM presente
- [ ] Referente compilato
- [ ] Ambiente Test compilato (o `non pubblicato`)
- [ ] Ambiente Produzione compilato (o `non pubblicato`)

*Template v1.1 - .NET 10 Worker Service - Token-optimized for AI agents* - Last Update 2026-06-12 — claude-sonnet-4-6
