# dr-mcp-dbschema

Server MCP per l'ispezione e la modifica dello schema di un database SQL Server. Compatibile con qualsiasi client che implementa il protocollo MCP: Claude Code, Claude Desktop, VS Code con GitHub Copilot, Cursor e altri.

Permette all'assistente IA di leggere la struttura di tabelle e viste ed eseguire operazioni DDL (CREATE TABLE, ALTER TABLE) con un flusso di conferma esplicita a due fasi.

## Client supportati

| Client | File di configurazione | Chiave JSON |
|--------|------------------------|-------------|
| Claude Code | `.mcp.json` | `mcpServers` |
| Claude Desktop | `claude_desktop_config.json` (globale) | `mcpServers` |
| VS Code + GitHub Copilot (>= 1.99) | `.vscode/mcp.json` | `servers` |
| Cursor | `.cursor/mcp.json` | `mcpServers` |

## Tool disponibili

### Ispezione schema (read-only)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `ListConnections` | -- | Elenca le connection string trovate negli `appsettings*.json` del progetto |
| `UseConnection` | `name` | Seleziona quale connection string usare |
| `ListViews` | -- | Elenca tutte le tabelle e le viste del database |
| `GetViewDefinition` | `viewName` | Restituisce il codice SQL (`CREATE VIEW`) di una vista |
| `GetViewColumns` | `viewName` | Restituisce le colonne di una tabella o vista (nome, tipo, nullable) |

### Operazioni DDL (richiedono abilitazione esplicita)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `PreviewCreate` | `sql` | Analizza uno statement CREATE TABLE e genera un token di conferma. Non esegue nulla. |
| `ExecuteCreate` | `confirmationToken` | Esegue la CREATE TABLE associata al token. Il token e monouso e scade in 60 secondi. |
| `PreviewAlter` | `tableName`, `sql` | Mostra lo schema corrente, analizza il rischio e genera un token. Scrive uno script di audit in `schema-migrations/`. |
| `ExecuteAlter` | `confirmationToken` | Esegue l'ALTER TABLE associata al token. Aggiorna il file di audit. |

## Modello di sicurezza DDL

Le operazioni DDL sono **disabilitate per default**. Devono essere abilitate esplicitamente per ambiente, separatamente per CREATE e ALTER, perche gli impatti sono diversi.

Il flusso obbligatorio e a due fasi:

```
PreviewCreate / PreviewAlter  -->  conferma visiva + token
ExecuteCreate / ExecuteAlter  -->  esecuzione solo con token valido
```

Il token e:
- **monouso**: consumato al primo `Execute*`, non riutilizzabile
- **TTL 60 secondi**: scade automaticamente se non usato
- **legato alla connessione attiva**: non trasferibile tra sessioni

`PreviewAlter` esegue anche un'analisi automatica del rischio:

| Operazione rilevata | Risk level | Motivo |
|---------------------|------------|--------|
| `DROP COLUMN` | `DANGER` | I dati nella colonna vengono persi definitivamente |
| `ALTER COLUMN` | `DANGER` | Possibile troncamento o perdita di dati esistenti |
| `DROP CONSTRAINT` | `DANGER` | Impatto sull'integrita referenziale |
| `ADD ...` | `WARN` | Operazione additiva, nessun dato esistente modificato |

## Audit trail ALTER

Ogni `PreviewAlter` scrive uno script in `schema-migrations/` nella directory corrente:

```
schema-migrations/
  2026-03-27_1430_dbo_Utenti.sql   <- PENDING (creato da PreviewAlter)
  2026-03-27_1431_dbo_Utenti.sql   <- EXECUTED (aggiornato da ExecuteAlter)
```

Il file contiene: tabella, token, timestamp UTC, SQL proposto e stato finale (`PENDING` o `EXECUTED`). La directory `schema-migrations/` va inclusa nel `.gitignore` o tracciata nel repository a seconda delle policy di audit del progetto.

## Configurazione

### Connection string

Nessuna variabile d'ambiente necessaria. All'avvio il tool determina la radice di scansione con questa priorita:

1. `DB_SCHEMA_ROOT` (env var) -- percorso esplicito
2. `src/` relativa alla directory corrente -- se la cartella esiste
3. Directory corrente -- fallback

Da quella radice scansiona ricorsivamente tutti gli `appsettings*.json` (escludendo `bin/` e `obj/`).

- Se trova **una sola** connection string, la seleziona automaticamente
- Se ne trova **piu di una**, richiede di scegliere tramite `UseConnection`

Override esplicito della connection string:

```powershell
$env:DB_CONNECTION_STRING = "Server=...;Database=...;..."
dotnet run --project tools/dr-mcp-dbschema/dr-mcp-dbschema.csproj
```

Override della radice di scansione (es. progetto senza `src/`):

```powershell
$env:DB_SCHEMA_ROOT = "C:\percorso\al\progetto"
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
| `AllowCreate` | `false` | Ambienti di sviluppo/staging dove e necessario creare tabelle |
| `AllowAlter` | `false` | Ambienti dove sono necessarie migrazioni strutturali |

> **Non abilitare entrambi i flag in produzione** senza un processo di revisione esplicito. Ogni ALTER in produzione dovrebbe passare da un processo di migration formale (Flyway, EF Migrations, ecc.).

## Installazione nel progetto

### Prerequisiti

Nessun SDK .NET richiesto sul computer che usa il tool. Il binario e self-contained.

Serve solo accesso di rete al database SQL Server.

### Installare con setup.ps1

Esegui una volta nella root del progetto consumatore:

```powershell
# Claude Code (default)
irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1 | iex

# VS Code + GitHub Copilot
Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Client vscode"

# Cursor
Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Client cursor"

# Tutti i client in una sola esecuzione
Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Client all"
```

Lo script in sequenza:
1. Scarica il binario `win-x64` dall'ultima GitHub Release
2. Verifica il checksum SHA256
3. Estrae il binario in `tools/dr-mcp-dbschema/`
4. Crea o aggiorna il file di configurazione del client specificato

Per installare una versione specifica:

```powershell
Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Version v1.2.0"
```

### File di configurazione generati

setup.ps1 crea o aggiorna automaticamente il file corretto per il client scelto. I file locali non vanno committati.

Aggiungi al `.gitignore` del progetto:

```
tools/dr-mcp-dbschema/
.mcp.json
.vscode/mcp.json
.cursor/mcp.json
```

Commetti invece i file `.example` come riferimento per il team. I file example sono gia presenti nel repository di dr-mcp-dbschema:

| File committato | Usato da |
|-----------------|---------|
| `.mcp.example.json` | Claude Code / Claude Desktop |
| `.vscode/mcp.json.example` | VS Code + GitHub Copilot |
| `.cursor/mcp.json.example` | Cursor |

### Formato per Claude Desktop (configurazione globale)

Claude Desktop usa un file di configurazione globale con percorso assoluto:

- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "db-schema": {
      "type": "stdio",
      "command": "C:/percorso/assoluto/tools/dr-mcp-dbschema/dr-mcp-dbschema.exe"
    }
  }
}
```

### Aggiornare il binario

Ri-esegui lo stesso comando di installazione. Lo script rileva la versione gia presente e stampa `Aggiornamento: vX.Y.Z -> vA.B.C`.

Per aggiornare a una versione specifica:

```powershell
Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Version v2.0.0"
```

## Esempi d'uso

### Ispezione schema

```
ListConnections
UseConnection -- name: "MioDb"
ListViews
GetViewDefinition -- viewName: "vw_Log106"
GetViewColumns -- viewName: "dbo.Utenti"
```

### CREATE TABLE

```
PreviewCreate -- sql: "CREATE TABLE dbo.Test (Id INT PRIMARY KEY, Nome NVARCHAR(100) NOT NULL)"
```

Output:
```
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
!!  ATTENZIONE -- OPERAZIONE DDL -- RICHIESTA CONFERMA   !!
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
ExecuteCreate -- confirmationToken: "A3F9C12B4E7D"
```

### ALTER TABLE

```
PreviewAlter -- tableName: "dbo.Utenti", sql: "ALTER TABLE dbo.Utenti ADD Email NVARCHAR(200) NULL"
ExecuteAlter -- confirmationToken: "<token da PreviewAlter>"
```

## Creare una release

Il workflow `.github/workflows/release.yml` si attiva automaticamente al push di un tag nel formato `v*.*.*`.

### Passi per pubblicare una nuova versione

```bash
git checkout main
git pull
git tag v1.0.0
git push origin v1.0.0
```

Il workflow esegue in sequenza:

| Passo | Operazione |
|-------|------------|
| Restore + Build | Compila solo `dr-mcp-dbschema.csproj` |
| Publish win-x64 | Binario single-file self-contained per Windows |
| Publish linux-x64 | Binario single-file self-contained per Linux |
| Create archives + checksums | `dr-mcp-dbschema-win-x64.zip`, `dr-mcp-dbschema-linux-x64.tar.gz`, `checksums.sha256` |
| Create GitHub Release | Pubblica archivi e checksum con note generate automaticamente |

La release e visibile in `https://github.com/davraf-amuro/dr-mcp-dbschema/releases`.

### Convenzione versioning

Usa [Semantic Versioning](https://semver.org/): `vMAGGIORE.MINORE.PATCH`

| Tipo di cambiamento | Cosa incrementare |
|---------------------|-------------------|
| Nuovo tool o breaking change | MAGGIORE |
| Nuova funzionalita compatibile | MINORE |
| Bug fix, aggiornamento dipendenze | PATCH |

---

## Test di integrazione

Il progetto include una suite di integration test che verifica il ciclo completo dei tool MCP su un database SQL Server reale.

### Struttura

```
tests/DrMcpDbSchema.IntegrationTests/
  McpEnvironmentFixture.cs   <- fixture condivisa: container + seed + client MCP
  FullCycleTests.cs          <- test sequenziale a 12 step
```

### Come funziona

La `McpEnvironmentFixture` esegue automaticamente questi passi prima dei test:

1. Avvia un container SQL Server via **Testcontainers** (richiede Docker attivo)
2. Crea il database `TEST` con schema seed: tabella `Customers` + vista `ActiveCustomers`
3. Scrive un `appsettings.json` temporaneo con `AllowCreate: true` e `AllowAlter: true`
4. Avvia il server MCP come subprocess (`dotnet run`) e connette il client MCP

### Scenario coperto da `FullCycleTests`

| Step | Tool | Verifica |
|------|------|----------|
| 1 | `ListConnections` | La connessione `(override)` e elencata e attiva |
| 2 | `UseConnection` | Selezione riuscita |
| 3 | `ListViews` | Tabella `Customers` e vista `ActiveCustomers` presenti |
| 4 | `GetViewDefinition` (vista) | Restituisce SQL `SELECT` |
| 5 | `GetViewDefinition` (tabella) | Risposta descrive l'oggetto come tabella |
| 6 | `GetViewColumns` | Colonne `Id`, `Name`, `Email` presenti |
| 7 | `PreviewCreate` | Token generato, `Orders` citato nell'output |
| 8 | `ExecuteCreate` | `[OK]` nella risposta |
| 9 | `ListViews` | `Orders` presente dopo la CREATE |
| 10 | `PreviewAlter` | Token generato, `Orders` citato |
| 11 | `ExecuteAlter` | `[OK]` nella risposta |
| 12 | `GetViewColumns` | Colonna `Note nvarchar` presente dopo l'ALTER |

### Requisiti per i test

| Requisito | Dettaglio |
|-----------|-----------|
| Docker Desktop | Necessario per Testcontainers (avvia SQL Server automaticamente) |
| .NET 10 SDK | Build del server MCP dal subprocess |
| Accesso a internet (prima esecuzione) | Pull immagine `mcr.microsoft.com/mssql/server` |

### Eseguire i test

```bash
dotnet test tests/DrMcpDbSchema.IntegrationTests/
```

> I test sono a esecuzione lenta (30-120 secondi) perche avviano un container Docker e compilano il server MCP. Non usare timeout brevi.

---

*Last update: 2026-03-28 -- dr-mcp-dbschema v2.5*
