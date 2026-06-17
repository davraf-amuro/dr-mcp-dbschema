# Card: dr-mcp-dbschema

## Identificazione

- **Progetto:** dr-mcp-dbschema
- **Solution:** —
- **Repository:** https://github.com/davraf-amuro/dr-mcp-dbschema
- **Tipo Applicazione:** MCP Server — eseguibile console .NET 10 self-contained, trasporto stdio
- **Pattern Architetturale:** Tool distribution — binario distribuito nei progetti ospiti tramite `setup.ps1`; si integra con qualsiasi client MCP (Claude Code, VS Code Copilot, Cursor)
- **Versione Corrente:** 0.4.0
- **Owner/Team:** davraf
- **Referente:** davide 'davraf' raffagli
- **Contatto Supporto:** Da verificare

## Stack Tecnologico

| Tecnologia | Versione | Ruolo |
|------------|----------|-------|
| C# / .NET | 10.0 | Linguaggio e runtime |
| ModelContextProtocol | 1.1.0 | Protocollo MCP, registrazione tool via attributi |
| Microsoft.Data.SqlClient | 6.1.4 | Connettività SQL Server |
| Microsoft.Extensions.Hosting | 10.0.4 | DI container, host builder |
| Serilog | 10.0.0 + Sinks.File 7.0.0 | Logging su file (opzionale) |

## Dipendenze

### Progetti Interni
— (nessuno)

### Pacchetti Esterni

| Pacchetto | Versione | Scopo |
|-----------|----------|-------|
| `Microsoft.Data.SqlClient` | 6.1.4 | Connessioni SQL Server (include `sni.dll` nativa) |
| `Microsoft.Extensions.Hosting` | 10.0.4 | DI, lifecycle host |
| `ModelContextProtocol` | 1.1.0 | MCP server stdio transport |
| `Serilog.Extensions.Logging` | 10.0.0 | Bridge Serilog → ILogger |
| `Serilog.Sinks.File` | 7.0.0 | Log rolling su file |

## Database

Il tool non ha un database proprio. Legge le connection string dal progetto ospite (`appsettings*.json`) e le usa per connettersi al database target al momento delle query.

## Servizi Esterni

| Tipo | Nome/Endpoint | Protocollo | Autenticazione | Scopo |
|------|---------------|------------|----------------|-------|
| — | — | — | — | — |

## Tool MCP esposti

### Gestione connessione

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `list_connections` | — | Elenca le connection string trovate negli `appsettings*.json` con griglia numerata (1, 2, 3…) per selezione rapida |
| `use_connection` | `name` | Seleziona una connessione di progetto |
| `use_custom_connection` | `connectionString` | Imposta una CS custom (solo sessione, non loggata) |
| `get_active_connection` | — | Mostra connessione attiva e lista disponibili |

### Ispezione schema (read-only)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `list_views` | — | Elenca tabelle e viste del database |
| `get_view_definition` | `viewName` | Restituisce il codice SQL (`CREATE VIEW`) |
| `get_view_columns` | `viewName` | Colonne con tipo, nullable, posizione |

### Operazioni DDL (disabilitate per default)

| Tool | Parametri | Descrizione |
|------|-----------|-------------|
| `preview_create` | `sql` | Analizza CREATE TABLE, genera token (60s TTL) |
| `execute_create` | `confirmationToken` | Esegue CREATE con token valido |
| `preview_alter` | `tableName`, `sql` | Analizza rischio ALTER, genera token, scrive audit |
| `execute_alter` | `confirmationToken` | Esegue ALTER con token valido |
| `preview_drop` | `tableName` | Mostra schema, genera token per DROP |
| `execute_drop` | `confirmationToken` | Esegue DROP con token valido |
| `diagnostics` | — | CWD, searchRoot, file trovati, CS disponibili (mascherate) |

## Configurazione e Hosting

- **Entrypoint:** `dr-mcp-dbschema.exe` — binario win-x64 self-contained, nessun SDK .NET richiesto sull'host
- **Distribuzione:** `setup.ps1` scarica il binario da GitHub Release, verifica SHA256, configura il client MCP
- **Ambiente Test:** non pubblicato
- **Ambiente Produzione:** non pubblicato — distribuito nei progetti ospiti tramite `setup.ps1`

---

*Revisione v1.1 — 2026-06-17 00:00 — claude-sonnet-4-6*
