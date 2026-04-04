# Changelog

Tutte le modifiche notevoli al progetto sono documentate in questo file.

Il formato segue [Keep a Changelog](https://keepachangelog.com/it/1.0.0/),
e il progetto aderisce al [Semantic Versioning](https://semver.org/lang/it/).

---

## [0.2.0] - 2026-04-04

### Added
- Progetto autonomo con repository Git indipendente (`https://github.com/davraf-amuro/dr-mcp-dbschema`)
- 27 unit test senza dipendenze da database (`DdlTokenStoreTests`, `DbSchemaHelpersTests`)
- Workflow GitHub Actions: `ci.yml` (build + test su push/PR) e `release.yml` (pubblicazione self-contained)
- Eseguibili single-file self-contained per `win-x64` e `linux-x64`
- Solution file `dr-mcp-dbschema.slnx`
- README.md completo con sezioni configurazione, tool MCP, build, sicurezza, troubleshooting
- `DbSchemaHelpers` (classe `internal`) con `ExtractObjectName`, `AnalyzeAlterRisk`, `MaskConnectionString`
- Sezione Troubleshooting nel README con i casi più comuni

### Changed
- Metadati csproj: `Version`, `Authors`, `Description`, `RepositoryUrl`
- `release.yml`: versione iniettata dal tag Git nel binario pubblicato (`/p:Version=...`)
- `Program.cs`: XML summaries per `ConnectionState`, `DdlSettings` e loro property
- `DbSchemaHelpers`: commenti inline sulla regex di `ExtractObjectName` e sulla logica di livello di `AnalyzeAlterRisk`

---

## [0.1.0] - 2026-03-30

### Added
- MCP server .NET 10 con trasporto `stdio`
- 12 tool MCP esposti:
  - `Diagnostics` — stato CWD, searchRoot, file scansionati, CS disponibili
  - `ListConnections` — elenco CS con file sorgente
  - `UseConnection` — selezione CS attiva
  - `ListViews` — elenco tabelle e viste
  - `GetViewDefinition` — SQL di creazione di una vista
  - `GetViewColumns` — colonne con tipo, nullable, posizione
  - `PreviewCreate` / `ExecuteCreate` — DDL CREATE TABLE con token
  - `PreviewAlter` / `ExecuteAlter` — DDL ALTER TABLE con token e audit trail
  - `PreviewDrop` / `ExecuteDrop` — DDL DROP TABLE con token
- Scansione automatica `appsettings*.json` con priorità (base < env < local)
- Auto-selezione connection string: singola CS o per `ASPNETCORE_ENVIRONMENT`
- Pattern preview → token monouso (60s) → execute per tutte le DDL
- Risk analysis per ALTER TABLE (`DANGER` / `WARN`)
- Audit trail in `schema-migrations/` per ogni tentativo di ALTER TABLE
- DDL flags (`AllowCreate` / `AllowAlter` / `AllowDrop`) disabilitati per default
- Logging opzionale su file con Serilog (configurabile da appsettings)
- Override via variabile d'ambiente `DB_CONNECTION_STRING`
