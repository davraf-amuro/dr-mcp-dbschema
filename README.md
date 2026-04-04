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

## Sicurezza

- **Token monouso** — ogni operazione DDL richiede un token che scade in 60 secondi e viene consumato all'uso
- **Pattern preview-execute** — nessuna DDL viene eseguita senza conferma esplicita
- **Risk analysis** — DROP COLUMN e ALTER COLUMN sono segnalati come DANGER
- **Audit trail** — ogni tentativo di ALTER viene scritto in `schema-migrations/` con timestamp UTC
- **Password masking** — le connection string non vengono mai mostrate in chiaro nell'output diagnostico
- **DDL flags** — CREATE / ALTER / DROP richiedono flag espliciti in appsettings
