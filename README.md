# mcp-db-schema

Server MCP per l'ispezione e la modifica dello schema di un database SQL Server. Permette a Claude Code di leggere la struttura delle tabelle e viste, e di eseguire operazioni DDL (CREATE TABLE, ALTER TABLE) con un flusso di conferma esplicita a due fasi.

## Tool disponibili

### Ispezione schema (read-only)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `ListConnections` | — | Elenca le connection string trovate negli `appsettings*.json` del progetto |
| `UseConnection` | `name` | Seleziona quale connection string usare |
| `ListViews` | — | Elenca tutte le tabelle e le viste del database |
| `GetViewDefinition` | `viewName` | Restituisce il codice SQL (`CREATE VIEW`) di una vista |
| `GetViewColumns` | `viewName` | Restituisce le colonne di una tabella o vista (nome, tipo, nullable) |

### Operazioni DDL (richiedono abilitazione esplicita)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `PreviewCreate` | `sql` | Analizza uno statement CREATE TABLE e genera un token di conferma. Non esegue nulla. |
| `ExecuteCreate` | `confirmationToken` | Esegue la CREATE TABLE associata al token. Il token è monouso e scade in 60 secondi. |
| `PreviewAlter` | `tableName`, `sql` | Mostra lo schema corrente, analizza il rischio e genera un token. Scrive uno script di audit in `schema-migrations/`. |
| `ExecuteAlter` | `confirmationToken` | Esegue l'ALTER TABLE associata al token. Aggiorna il file di audit. |

## Modello di sicurezza DDL

Le operazioni DDL sono **disabilitate per default**. Devono essere abilitate esplicitamente per ambiente, separatamente per CREATE e ALTER, perché gli impatti sono diversi.

Il flusso obbligatorio è a due fasi:

```
PreviewCreate / PreviewAlter  →  conferma visiva + token
ExecuteCreate / ExecuteAlter  →  esecuzione solo con token valido
```

Il token è:
- **monouso**: consumato al primo `Execute*`, non riutilizzabile
- **TTL 60 secondi**: scade automaticamente se non usato
- **legato alla connessione attiva**: non trasferibile tra sessioni

`PreviewAlter` esegue anche un'analisi automatica del rischio:

| Operazione rilevata | Risk level | Motivo |
|---------------------|------------|--------|
| `DROP COLUMN` | `DANGER` | I dati nella colonna vengono persi definitivamente |
| `ALTER COLUMN` | `DANGER` | Possibile troncamento o perdita di dati esistenti |
| `DROP CONSTRAINT` | `DANGER` | Impatto sull'integrità referenziale |
| `ADD ...` | `WARN` | Operazione additiva, nessun dato esistente modificato |

## Audit trail ALTER

Ogni `PreviewAlter` scrive uno script in `schema-migrations/` nella directory corrente:

```
schema-migrations/
  2026-03-27_1430_dbo_Utenti.sql   ← PENDING (creato da PreviewAlter)
  2026-03-27_1431_dbo_Utenti.sql   ← EXECUTED (aggiornato da ExecuteAlter)
```

Il file contiene: tabella, token, timestamp UTC, SQL proposto e stato finale (`PENDING` o `EXECUTED`). La directory `schema-migrations/` va inclusa nel `.gitignore` o tracciata nel repository a seconda delle policy di audit del progetto.

## Configurazione

### Connection string

Nessuna variabile d'ambiente necessaria. All'avvio il tool determina la radice di scansione con questa priorità:

1. `DB_SCHEMA_ROOT` (env var) — percorso esplicito
2. `src/` relativa alla directory corrente — se la cartella esiste
3. Directory corrente — fallback

Da quella radice scansiona ricorsivamente tutti gli `appsettings*.json` (escludendo `bin/` e `obj/`).

- Se trova **una sola** connection string, la seleziona automaticamente
- Se ne trova **più di una**, richiede di scegliere tramite `UseConnection`

Override esplicito della connection string:

```bash
DB_CONNECTION_STRING="Server=...;Database=...;" dotnet run ...
```

Override della radice di scansione (es. progetto senza `src/`):

```bash
DB_SCHEMA_ROOT="/percorso/al/progetto" dotnet run ...
```

### Abilitare le operazioni DDL

Aggiungi la sezione `Ddl` all'`appsettings.json` del progetto che usa il tool:

```json
{
  "ConnectionStrings": {
    "MioDb": "Server=...;Database=...;..."
  },
  "Ddl": {
    "AllowCreate": false,
    "AllowAlter": false
  }
}
```

| Flag | Default | Quando abilitare |
|------|---------|-----------------|
| `AllowCreate` | `false` | Ambienti di sviluppo/staging dove è necessario creare tabelle |
| `AllowAlter` | `false` | Ambienti dove sono necessarie migrazioni strutturali |

> **Non abilitare entrambi i flag in produzione** senza un processo di revisione esplicito. Ogni ALTER in produzione dovrebbe passare da un processo di migration formale (Flyway, EF Migrations, ecc.).

## Aggiungere al progetto

Clona la repo nella cartella `tools/` del tuo progetto:

```bash
git clone https://git.uniot.eu/voisoft/mcp/mcp-db-schema.git tools/mcp-db-schema
```

Aggiungi `tools/mcp-db-schema/` al `.gitignore` del progetto, poi aggiungi al file `.mcp.json` nella root:

```json
{
  "mcpServers": {
    "db-schema": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "tools/mcp-db-schema/mcp-db-schema.csproj"]
    }
  }
}
```

Riavvia Claude Code — il server viene compilato e avviato automaticamente.

## Esempi d'uso

### Ispezione schema

```
ListConnections
UseConnection → name: "MioDb"
ListViews
GetViewDefinition → viewName: "vw_Log106"
GetViewColumns → viewName: "dbo.Utenti"
```

### CREATE TABLE

```
PreviewCreate → sql: "CREATE TABLE dbo.Test (Id INT PRIMARY KEY, Nome NVARCHAR(100) NOT NULL)"
```

Output:
```
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
!!  ATTENZIONE — OPERAZIONE DDL — RICHIESTA CONFERMA     !!
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

risk_level  : DANGER
operazione  : CREATE TABLE
tabella     : dbo.Test
database    : MioDb
token       : A3F9C12B4E7D
scade tra   : 60 secondi
...
!! Per procedere: ExecuteCreate("A3F9C12B4E7D")
```

```
ExecuteCreate → confirmationToken: "A3F9C12B4E7D"
```

### ALTER TABLE

```
PreviewAlter → tableName: "dbo.Utenti", sql: "ALTER TABLE dbo.Utenti ADD Email NVARCHAR(200) NULL"
```

```
ExecuteAlter → confirmationToken: "<token da PreviewAlter>"
```

## Aggiornamento

```bash
cd tools/mcp-db-schema
git pull origin main
```

## Requisiti

- .NET 10 SDK
- Accesso di rete al database SQL Server

---

*Last update: 2026-03-27 — mcp-db-schema v2.0*
