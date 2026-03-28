# dr-mcp-dbschema

Server MCP per l'ispezione e la modifica dello schema di un database SQL Server. Compatibile con qualsiasi client che implementa il protocollo MCP: Claude Code, Claude Desktop, VS Code con GitHub Copilot, Cursor e altri.

Permette all'assistente IA di leggere la struttura di tabelle e viste ed eseguire operazioni DDL (CREATE TABLE, ALTER TABLE, DROP TABLE) con un flusso di conferma esplicita a due fasi.

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
| `list_connections` | -- | Elenca le connection string trovate negli `appsettings*.json` del progetto |
| `use_connection` | `name` | Seleziona quale connection string usare |
| `list_views` | -- | Elenca tutte le tabelle e le viste del database |
| `get_view_definition` | `viewName` | Restituisce il codice SQL (`CREATE VIEW`) di una vista |
| `get_view_columns` | `viewName` | Restituisce le colonne di una tabella o vista (nome, tipo, nullable) |

### Operazioni DDL (richiedono abilitazione esplicita)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `preview_create` | `sql` | Analizza uno statement CREATE TABLE e genera un token di conferma. Non esegue nulla. |
| `execute_create` | `confirmationToken` | Esegue la CREATE TABLE associata al token. Il token è monouso e scade in 60 secondi. |
| `preview_alter` | `tableName`, `sql` | Mostra lo schema corrente, analizza il rischio e genera un token. Scrive uno script di audit in `schema-migrations/`. |
| `execute_alter` | `confirmationToken` | Esegue l'ALTER TABLE associata al token. Aggiorna il file di audit. |
| `preview_drop` | `tableName` | Mostra lo schema corrente della tabella e genera un token di conferma per eliminarla. Non esegue nulla. |
| `execute_drop` | `confirmationToken` | Esegue la DROP TABLE associata al token. Il token è monouso e scade in 60 secondi. |

## Modello di sicurezza DDL

Le operazioni DDL sono **disabilitate per default**. Ogni tipo di operazione si abilita separatamente perché gli impatti sono diversi.

Il flusso obbligatorio è a due fasi:

```
Preview*  -->  conferma visiva + token (60 secondi TTL)
Execute*  -->  esecuzione solo con token valido e non scaduto
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

`PreviewDrop` classifica sempre al livello `DANGER` e mostra lo schema completo della tabella prima di generare il token.

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

Nessuna variabile d'ambiente necessaria. All'avvio il tool determina la radice di scansione con questa priorità:

1. `DB_SCHEMA_ROOT` (env var) — percorso esplicito
2. `src/` relativa alla directory corrente — se la cartella esiste
3. Directory corrente — fallback

Da quella radice scansiona ricorsivamente tutti gli `appsettings*.json` (escludendo `bin/` e `obj/`) con questa priorità (l'ultimo letto sovrascrive):

| Priorità | Pattern | Esempi |
|----------|---------|--------|
| 1 — base | `appsettings*.json` senza punto interno | `appsettings.json` |
| 2 — ambiente | `appsettings.{env}.json` | `appsettings.Development.json` |
| 3 — locale (**vince su tutto**) | `appsettings.local.json` | `appsettings.local.json` |

`appsettings.local.json` è già escluso dal `.gitignore` del progetto ed è il posto giusto per connection string locali che non devono essere committate.

- Se trova **una sola** connection string, la seleziona automaticamente
- Se ne trova **più di una**, richiede di scegliere tramite `UseConnection`

Override esplicito della connection string (due modalità, in ordine di priorità):

```powershell
# 1 — argomento da riga di comando (priorità massima)
dotnet run --project tools/dr-mcp-dbschema/dr-mcp-dbschema.csproj -- "Server=...;Database=...;..."

# 2 — variabile d'ambiente
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
    "AllowAlter": false,
    "AllowDrop": false
  }
}
```

| Flag | Default | Quando abilitare |
|------|---------|-----------------|
| `AllowCreate` | `false` | Ambienti di sviluppo/staging dove è necessario creare tabelle |
| `AllowAlter` | `false` | Ambienti dove sono necessarie migrazioni strutturali |
| `AllowDrop` | `false` | Ambienti di test o sviluppo dove è necessario eliminare tabelle |

> **Non abilitare i flag DDL in produzione** senza un processo di revisione esplicito. Ogni operazione strutturale in produzione dovrebbe passare da un processo di migration formale (Flyway, EF Migrations, ecc.).

## Installazione nel progetto

### Prerequisiti

Nessun SDK .NET richiesto sul computer che usa il tool. Il binario è self-contained.

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

Commetti invece i file `.example` come riferimento per il team. I file example sono già presenti nel repository di dr-mcp-dbschema:

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

Ri-esegui lo stesso comando di installazione. Lo script rileva la versione già presente e stampa `Aggiornamento: vX.Y.Z -> vA.B.C`.

Per aggiornare a una versione specifica:

```powershell
Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Version v2.0.0"
```

## Esempi d'uso

### Ispezione schema

```
list_connections
use_connection -- name: "MioDb"
list_views
get_view_definition -- viewName: "vw_Log106"
get_view_columns -- viewName: "dbo.Utenti"
```

### CREATE TABLE

```
preview_create -- sql: "CREATE TABLE dbo.Test (Id INT PRIMARY KEY, Nome NVARCHAR(100) NOT NULL)"
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
!! Per procedere: execute_create("A3F9C12B4E7D")
```

```
execute_create -- confirmationToken: "A3F9C12B4E7D"
```

### ALTER TABLE

```
preview_alter -- tableName: "dbo.Utenti", sql: "ALTER TABLE dbo.Utenti ADD Email NVARCHAR(200) NULL"
execute_alter -- confirmationToken: "<token da preview_alter>"
```

### DROP TABLE

```
preview_drop -- tableName: "dbo.Test"
```

Output:
```
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
!!  ATTENZIONE -- OPERAZIONE DDL -- RICHIESTA CONFERMA   !!
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

risk_level  : DANGER
operazione  : DROP TABLE
tabella     : dbo.Test
database    : MioDb
token       : B7E2D45F9C1A
scade tra   : 60 secondi

Schema che verrà ELIMINATO:
------------------------------------------------------------
pos | column | type | max_len | nullable
1 | Id | int | - | NO
2 | Nome | nvarchar | 100 | NO
------------------------------------------------------------

!! ATTENZIONE: questa operazione DISTRUGGE la tabella e tutti i suoi dati.
!! Per procedere: execute_drop("B7E2D45F9C1A")
```

```
execute_drop -- confirmationToken: "B7E2D45F9C1A"
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

La release è visibile in `https://github.com/davraf-amuro/dr-mcp-dbschema/releases`.

### Convenzione versioning

Usa [Semantic Versioning](https://semver.org/): `vMAGGIORE.MINORE.PATCH`

| Tipo di cambiamento | Cosa incrementare |
|---------------------|-------------------|
| Nuovo tool o breaking change | MAGGIORE |
| Nuova funzionalità compatibile | MINORE |
| Bug fix, aggiornamento dipendenze | PATCH |

---

## Test di integrazione

Il progetto include due suite di integration test che verificano il ciclo completo dei tool MCP su un database SQL Server reale.

### Struttura

```
tests/DrMcpDbSchema.IntegrationTests/
  McpEnvironmentFixture.cs    <- fixture Testcontainers: container + seed + client MCP
  FullCycleTests.cs           <- test su DB isolato in Docker (12 step)
  LocalDbFixture.cs           <- fixture per DB locale reale: connessione + client MCP
  LocalDbRealCycleTests.cs    <- test su DB locale reale (15 step, categoria LocalDB)
```

---

### FullCycleTests — DB isolato con Testcontainers

Richiede Docker attivo. Crea un database SQL Server temporaneo in container, esegue il ciclo completo e lo distrugge al termine. Adatto alla CI.

#### Come funziona

`McpEnvironmentFixture` esegue questi passi prima dei test:

1. Avvia un container SQL Server via **Testcontainers**
2. Crea il database `TEST` con schema seed: tabella `Customers` + vista `ActiveCustomers`
3. Scrive un `appsettings.json` temporaneo con `AllowCreate: true` e `AllowAlter: true`
4. Avvia il server MCP come subprocess (`dotnet run`) e connette il client MCP

#### Scenario coperto

| Step | Tool | Verifica |
|------|------|----------|
| 1 | `list_connections` | La connessione `(override)` è elencata e attiva |
| 2 | `use_connection` | Selezione riuscita |
| 3 | `list_views` | Tabella `Customers` e vista `ActiveCustomers` presenti |
| 4 | `get_view_definition` (vista) | Restituisce SQL `SELECT` |
| 5 | `get_view_definition` (tabella) | Risposta descrive l'oggetto come tabella |
| 6 | `get_view_columns` | Colonne `Id`, `Name`, `Email` presenti |
| 7 | `preview_create` | Token generato, `Orders` citato nell'output |
| 8 | `execute_create` | `[OK]` nella risposta |
| 9 | `list_views` | `Orders` presente dopo la CREATE |
| 10 | `preview_alter` | Token generato, `Orders` citato |
| 11 | `execute_alter` | `[OK]` nella risposta |
| 12 | `get_view_columns` | Colonna `Note nvarchar` presente dopo l'ALTER |

#### Requisiti

| Requisito | Dettaglio |
|-----------|-----------|
| Docker Desktop | Necessario per Testcontainers |
| .NET 10 SDK | Build del server MCP subprocess |
| Accesso a internet (prima esecuzione) | Pull immagine `mcr.microsoft.com/mssql/server` |

#### Eseguire

```bash
dotnet test tests/DrMcpDbSchema.IntegrationTests/
```

> I test sono lenti (30–120 secondi): avviano un container Docker e compilano il server MCP.

---

### LocalDbRealCycleTests — DB locale reale

Non richiede Docker. Si connette al database SQL Server locale configurato nella connection string. Simula esattamente il flusso che un assistente IA esegue in produzione: ispeziona, crea, modifica lo schema, elimina.

Ogni esecuzione produce un **log dettagliato** in `test-logs/` (escluso da git) con i comandi SQL inviati al DB e le relative risposte.

#### Come funziona

`LocalDbFixture` esegue questi passi:

1. Legge la connection string da `DB_LOCAL_CONNECTION_STRING` (env var) o usa il default hardcoded
2. Verifica la connettività al database — salta il test automaticamente se non raggiungibile
3. Pre-pulisce `dbo.IA_TEST` se esistesse da un run precedente fallito
4. Scrive un `appsettings.json` temporaneo con `AllowCreate: true`, `AllowAlter: true`, `AllowDrop: true`
5. Avvia il server MCP come subprocess e connette il client MCP
6. Nel teardown: elimina `dbo.IA_TEST` come safety net e cancella i file temporanei

#### Scenario coperto

| Step | Tool | Operazione SQL sul DB |
|------|------|-----------------------|
| 1 | `list_views` | Verifica assenza `IA_TEST` (pre-condizione) |
| 2 | `list_connections` | Verifica auto-selezione connessione `(override)` |
| 3 | `preview_create` | Genera token per `CREATE TABLE dbo.IA_TEST` (4 colonne) |
| 4 | `execute_create` | `CREATE TABLE dbo.IA_TEST (Id UNIQUEIDENTIFIER, Nome NVARCHAR(200), Valore DECIMAL(10,2), CreatedAt DATETIME2)` |
| 5 | `list_views` | Verifica presenza `IA_TEST` dopo CREATE |
| 6 | `get_view_columns` | Legge schema iniziale: 4 colonne |
| 7 | `preview_alter` | Genera token per `ALTER TABLE dbo.IA_TEST ADD Aggiunta NVARCHAR(500) NULL` |
| 8 | `execute_alter` | `ALTER TABLE dbo.IA_TEST ADD Aggiunta NVARCHAR(500) NULL` |
| 9 | `get_view_columns` | Verifica presenza colonna `Aggiunta` |
| 10 | `preview_alter` | Genera token per `EXEC sp_rename 'dbo.IA_TEST.Aggiunta', 'Modificata', 'COLUMN'` |
| 11 | `execute_alter` | `EXEC sp_rename 'dbo.IA_TEST.Aggiunta', 'Modificata', 'COLUMN'` |
| 12 | `get_view_columns` | Verifica `Modificata` presente, `Aggiunta` assente |
| 13 | `preview_drop` | Genera token per `DROP TABLE [dbo].[IA_TEST]` |
| 14 | `execute_drop` | `DROP TABLE [dbo].[IA_TEST]` |
| 15 | `list_views` | Verifica assenza `IA_TEST` dopo DROP |

#### Log di esecuzione

Ad ogni run viene creato un file in `test-logs/localdb-YYYY-MM-DD_HH-mm-ss.log` con il dettaglio di ogni comando inviato e la risposta ricevuta:

```
=== LocalDB Full Cycle Test — 2026-03-28 21:38:43 UTC ===
    database : Data Source=localhost;Initial Catalog=DrNutrizioNino;...

────────────────────────────────────────────────────────────
[Step 03] preview_create — dbo.IA_TEST con 4 colonne
  → TOOL: preview_create
    sql: CREATE TABLE dbo.IA_TEST (...)
  ← RESPONSE:
    risk_level  : DANGER
    operazione  : CREATE TABLE
    token       : 9265F1F5E972
    ...

[Step 04] execute_create
  → TOOL: execute_create
    confirmationToken: 9265F1F5E972
  ← RESPONSE:
    [OK] CREATE TABLE eseguita con successo.
    timestamp : 2026-03-28 21:38:44 UTC
```

#### Requisiti

| Requisito | Dettaglio |
|-----------|-----------|
| SQL Server locale | Raggiungibile con la connection string configurata |
| .NET 10 SDK | Build del server MCP subprocess |
| Nessun Docker | Il test usa il DB locale, non Testcontainers |

#### Eseguire

```bash
# Con la connection string di default (DrNutrizioNino su localhost)
dotnet test tests/DrMcpDbSchema.IntegrationTests/ --filter "Category=LocalDB"

# Con connection string personalizzata
$env:DB_LOCAL_CONNECTION_STRING = "Data Source=myserver;Initial Catalog=MyDb;..."
dotnet test tests/DrMcpDbSchema.IntegrationTests/ --filter "Category=LocalDB"
```

#### Escludere dalla CI

I test `LocalDB` usano un database locale non disponibile in CI. Aggiungi il filtro al workflow:

```bash
dotnet test tests/DrMcpDbSchema.IntegrationTests/ --filter "Category!=LocalDB"
```

---

*Last update: 2026-03-28 — dr-mcp-dbschema v2.7*
