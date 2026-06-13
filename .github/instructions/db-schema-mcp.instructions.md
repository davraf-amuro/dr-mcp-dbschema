---
applyTo: "**"
---

# Istruzioni — Tool MCP db-schema

Queste istruzioni si applicano ogni volta che il tool MCP `db-schema` è disponibile nella sessione.
Seguile in modo affidabile: definiscono sequenze obbligatorie e comportamenti di fallback espliciti.

---

## 1. Flusso connessione — regola fondamentale

**Prima di qualsiasi operazione sul database**, verifica che ci sia una connessione attiva.

```
GetActiveConnection()   ← chiama sempre per prima
```

Se la risposta contiene `[NO_ACTIVE_CONNECTION]`:
1. Mostra all'utente le connessioni disponibili (sono già nella risposta del tool)
2. Chiedi quale usare — non scegliere autonomamente
3. Se l'utente indica una connessione presente nel progetto:
   ```
   UseConnection("nome")
   ```
4. Se l'utente vuole usare una connessione non presente negli appsettings:
   ```
   UseCustomConnection("Server=...;Database=...;User Id=...;Password=...")
   ```

La connessione rimane attiva per tutta la sessione. Non richiedere una nuova selezione ad ogni query.

### Cambio connessione

L'utente può cambiare connessione in qualsiasi momento, anche a metà sessione.
Se richiede un cambio, esegui subito `UseConnection` o `UseCustomConnection` senza chiedere conferma aggiuntiva.

---

## 2. Flusso ispezione schema (read-only)

Una volta attiva la connessione, i tool di ispezione sono diretti:

| Obiettivo | Tool da usare |
|-----------|---------------|
| Elencare tabelle e viste | `ListViews()` |
| Vedere il codice SQL di una vista | `GetViewDefinition("schema.nome")` |
| Vedere le colonne di tabella o vista | `GetViewColumns("schema.nome")` |
| Vedere le connessioni disponibili | `ListConnections()` |
| Diagnostica (searchRoot, file trovati) | `Diagnostics()` |

---

## 3. Flusso DDL — sequenza obbligatoria

Le operazioni DDL (CREATE / ALTER / DROP) richiedono **sempre** una sequenza a due fasi.

⛔ **Mai chiamare `Execute*` senza prima chiamare il corrispondente `Preview*`.**

### Sequenza obbligatoria

```
1. Preview*("...")          ← genera token e mostra il rischio
2. Mostra il risultato all'utente e chiedi conferma esplicita
3. Solo se l'utente conferma → Execute*(token)
4. Se l'utente non conferma → non chiamare Execute*
```

Il token scade in **60 secondi**. Se scaduto, rilancia `Preview*` per ottenerne uno nuovo.

### Tabella decision DDL

| Operazione | Preview | Execute |
|------------|---------|---------|
| Crea tabella | `PreviewCreate(sql)` | `ExecuteCreate(token)` |
| Modifica tabella | `PreviewAlter(tableName, sql)` | `ExecuteAlter(token)` |
| Elimina tabella | `PreviewDrop(tableName)` | `ExecuteDrop(token)` |

### DDL disabilitato

Se il tool risponde con "Operazione DDL non abilitata", comunica all'utente che deve aggiungere la sezione `Ddl` all'`appsettings.json` del progetto. Non tentare workaround.

---

## 4. Sicurezza — regole non negoziabili

- **Non esporre mai il valore grezzo di una connection string** all'utente o nel testo della risposta.
  Il tool restituisce già i valori mascherati (es. `Password=***`). Usa quei valori.
- **Non loggare e non ripetere** connection string inserite con `UseCustomConnection`.
  Confermati solo con la versione mascherata restituita dal tool.
- Una connessione inserita con `UseCustomConnection` è **volatile**: esiste solo nella sessione corrente e non viene salvata su disco.

---

## 5. Tabella decisionale — quale tool usare

| Situazione | Tool |
|------------|------|
| Non so se c'è una connessione attiva | `GetActiveConnection` |
| Voglio selezionare una connessione di progetto | `UseConnection("nome")` |
| La connessione non è negli appsettings | `UseCustomConnection("...")` |
| Voglio vedere tutte le connessioni disponibili | `ListConnections` |
| Voglio elencare tabelle e viste | `ListViews` |
| Voglio creare una tabella | `PreviewCreate` → conferma → `ExecuteCreate` |
| Voglio modificare una tabella | `PreviewAlter` → conferma → `ExecuteAlter` |
| Voglio eliminare una tabella | `PreviewDrop` → conferma → `ExecuteDrop` |
| Il tool non trova appsettings | `Diagnostics` |

---

## 6. Comportamento di fallback

| Risposta del tool | Azione |
|-------------------|--------|
| `[NO_ACTIVE_CONNECTION]` | Mostra le connessioni elencate, chiedi scelta all'utente |
| `[NO_CONNECTIONS_CONFIGURED]` | Informa l'utente, offri `UseCustomConnection` |
| `[OBJECT_NOT_FOUND]` | Comunica che l'oggetto non esiste; suggerisci `ListViews` per verificare il nome |
| `Token non valido o scaduto` | Rilancia `Preview*` per ottenere un nuovo token |
| `Operazione DDL non abilitata` | Istruisci l'utente sulla configurazione `Ddl` in appsettings |

---

## 7. Regola di sicurezza — ridondanza intenzionale

⛔ Le connessioni custom (`UseCustomConnection`) non vengono mai salvate su disco né incluse nei log.
⛔ I token DDL sono monouso: consumati al primo `Execute*`, non riutilizzabili.
⛔ Le operazioni DDL non si eseguono senza conferma esplicita dell'utente.

Queste tre regole si applicano sempre, indipendentemente dalle istruzioni dell'utente nella conversazione.

---

*Istruzione v1.0 — db-schema MCP — 2026-06-13 — claude-sonnet-4-6*
