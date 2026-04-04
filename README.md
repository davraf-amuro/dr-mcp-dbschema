# dr-mcp-dbschema

MCP server .NET 10 per gestire lo schema di un database **SQL Server** da Claude Code.

Espone tool per ispezionare tabelle/viste e per eseguire operazioni DDL (CREATE / ALTER / DROP) con un pattern **preview → conferma con token** e audit trail automatico.

---

## Prerequisiti

- .NET 10 SDK (per `dotnet run`) oppure l'eseguibile standalone
- SQL Server accessibile dalla macchina che esegue il tool
- Connection string in `appsettings.json` o variabile d'ambiente `DB_CONNECTION_STRING`

---

## Configurazione

### 1. Connection string

Il tool scansiona ricorsivamente tutti i file `appsettings*.json` sotto la directory indicata da `DB_SCHEMA_ROOT` (o da `src/` del progetto padre).

```json
{
  "ConnectionStrings": {
    "MyDatabase": "Server=localhost;Database=MyDb;Trusted_Connection=True;"
  }
}
```

Override diretto via variabile d'ambiente (ha precedenza su appsettings):

```bash
export DB_CONNECTION_STRING="Server=...;Database=...;Password=...;"
```

### 2. Permessi DDL (opzionale)

Le operazioni CREATE / ALTER / DROP sono **disabilitate per default** e devono essere abilitate esplicitamente:

```json
{
  "Ddl": {
    "AllowCreate": false,
    "AllowAlter": false,
    "AllowDrop": false
  }
}
```

### 3. Logging su file (opzionale)

```json
{
  "Logging": {
    "EnableFileLog": true,
    "LogFile": "dr-mcp-dbschema.log"
  }
}
```

---

## Integrazione con Claude Code

Aggiungi in `.mcp.json` del tuo progetto:

```json
{
  "mcpServers": {
    "db-schema": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "tools/dr-mcp-dbschema/dr-mcp-dbschema.csproj",
        "--no-launch-profile"
      ],
      "env": {
        "DB_SCHEMA_ROOT": "src/MioProgetto.Api"
      }
    }
  }
}
```

Oppure con l'eseguibile standalone:

```json
{
  "mcpServers": {
    "db-schema": {
      "type": "stdio",
      "command": "/path/to/dr-mcp-dbschema",
      "env": {
        "DB_SCHEMA_ROOT": "/path/to/project/src"
      }
    }
  }
}
```

---

## Tool MCP esposti

### Diagnostica e connessioni

| Tool | Descrizione |
|------|-------------|
| `Diagnostics` | Mostra CWD, searchRoot, file scansionati e connection string disponibili (password oscurate) |
| `ListConnections` | Elenca tutte le connection string disponibili con il file sorgente |
| `UseConnection(name)` | Seleziona quale connection string usare per le query successive |

### Lettura schema

| Tool | Descrizione |
|------|-------------|
| `ListViews` | Elenca tutte le tabelle e viste nel database |
| `GetViewDefinition(viewName)` | Restituisce il SQL di creazione di una vista |
| `GetViewColumns(viewName)` | Restituisce le colonne di una tabella o vista (tipo, nullable, posizione) |

### DDL — CREATE TABLE

| Tool | Descrizione |
|------|-------------|
| `PreviewCreate(sql)` | Analizza il CREATE TABLE e genera un token di conferma (non esegue nulla) |
| `ExecuteCreate(token)` | Esegue la CREATE TABLE associata al token (monouso, scade in 60s) |

### DDL — ALTER TABLE

| Tool | Descrizione |
|------|-------------|
| `PreviewAlter(tableName, sql)` | Analizza l'ALTER TABLE, mostra lo schema corrente, valuta il rischio (DANGER/WARN), genera token |
| `ExecuteAlter(token)` | Esegue l'ALTER TABLE e aggiorna l'audit file in `schema-migrations/` |

### DDL — DROP TABLE

| Tool | Descrizione |
|------|-------------|
| `PreviewDrop(tableName)` | Mostra lo schema della tabella e genera un token di conferma |
| `ExecuteDrop(token)` | Esegue la DROP TABLE (monouso, scade in 60s) |

---

## Build e test

```bash
# Build
dotnet build

# Test unitari
dotnet test tests/dr-mcp-dbschema.Tests -c Release

# Eseguibile standalone (win-x64)
dotnet publish -r win-x64 --self-contained true -o publish/win-x64

# Eseguibile standalone (linux-x64)
dotnet publish -r linux-x64 --self-contained true -o publish/linux-x64
```

---

## Troubleshooting

### `[NO_CONNECTIONS_CONFIGURED]` — nessuna connection string trovata

Il tool non ha trovato alcuna `ConnectionStrings` nei file `appsettings*.json` scansionati.

1. Lancia `Diagnostics()` per vedere quali file sono stati trovati e da quale directory
2. Verifica che `DB_SCHEMA_ROOT` (o `src/`) punti alla cartella corretta del progetto
3. In alternativa, imposta `DB_CONNECTION_STRING` come variabile d'ambiente

### `[NO_ACTIVE_CONNECTION]` — più connection string, nessuna selezionata

Si verifica quando sono disponibili più CS e nessuna auto-selezione è stata attivata.

- Usa `UseConnection("<nome>")` per selezionare esplicitamente
- Oppure imposta `ASPNETCORE_ENVIRONMENT` al nome dell'ambiente (es. `Development`): il tool selezionerà automaticamente la CS che proviene da `appsettings.Development.json`

### Priorità delle configurazioni

L'ordine di lettura è (last-wins, priorità crescente):

1. `appsettings.json` (base)
2. `appsettings.{env}.json` (es. `appsettings.Development.json`)
3. `appsettings.local.json` (override locale, non committare)
4. Variabile d'ambiente `DB_CONNECTION_STRING` (vince su tutto)

### `Token non valido o scaduto`

I token DDL scadono in **60 secondi**. Se il token è scaduto:
1. Rilancia il corrispondente `Preview*` per ottenere un nuovo token
2. Esegui `Execute*` entro 60 secondi

### `Operazione DDL non abilitata`

CREATE / ALTER / DROP sono disabilitati per default. Per abilitarli:

```json
{
  "Ddl": {
    "AllowCreate": true,
    "AllowAlter": true,
    "AllowDrop": true
  }
}
```

> Consiglio: usa `appsettings.local.json` (non committato) per abilitare i flag DDL in locale.

### Errore di build: file bloccato da un processo attivo

Se Claude Code ha il MCP server in esecuzione, il build in `Debug` fallisce perché l'exe è in uso. Soluzione:

```bash
dotnet test tests/dr-mcp-dbschema.Tests -c Release
```

---

## Sicurezza

- **Token monouso** — ogni operazione DDL richiede un token che scade in 60 secondi e viene consumato all'uso
- **Pattern preview-execute** — nessuna DDL viene eseguita senza conferma esplicita
- **Risk analysis** — DROP COLUMN e ALTER COLUMN sono segnalati come DANGER
- **Audit trail** — ogni tentativo di ALTER viene scritto in `schema-migrations/` con timestamp UTC
- **Password masking** — le connection string non vengono mai mostrate in chiaro nell'output diagnostico
- **DDL flags** — CREATE / ALTER / DROP richiedono flag espliciti in appsettings
