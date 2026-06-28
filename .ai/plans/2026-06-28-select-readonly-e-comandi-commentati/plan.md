# Piano — SELECT read-only + generazione comandi non-SELECT commentati

**Data:** 2026-06-28
**Slug:** select-readonly-e-comandi-commentati
**Stato:** COMPLETATO
**Modello:** claude-opus-4-8

---

## Obiettivo

Aggiungere al server MCP `dr-mcp-dbschema` due capacità:

1. **SELECT read-only** su tabelle/viste: esegue una query `SELECT` e restituisce le righe.
2. **Generazione comandi non-SELECT senza esecuzione**: qualsiasi comando diverso da `SELECT`
   (INSERT/UPDATE/DELETE/DDL/EXEC…) viene restituito al chiamante **commentato** (prefisso `-- ` per riga),
   mai eseguito sul database.

## Decisioni utente (confermate)

- **Gate SELECT:** flag `AllowSelect` in sezione `Ddl`, **default `false`**.
- **DDL Execute esistenti:** lasciati invariati (nessuna rimozione di Preview/Execute Create/Alter/Drop).
- **Stile commento:** prefisso `-- ` per riga, dentro un header esplicativo.

## Scope

### File da modificare/creare
- `Models/DdlSettings.cs` — aggiunta proprietà `AllowSelect` (default `false`).
- `Program.cs` — lettura `AllowSelect` dalla sezione `Ddl`.
- `Helpers/DbSchemaHelpers.cs` — `IsReadOnlySelect(sql, out reason)` + `CommentOutSql(sql)`.
- `Tools/DbSchemaTools.cs` — nuovi tool `RunSelect` e `GenerateCommand`.
- `appsettings.json` — aggiunta `AllowSelect: false` nella sezione `Ddl`.
- `README.md` — documentazione nuovi tool + flag.
- `.github/instructions/db-schema-mcp.instructions.md` — flusso SELECT e flusso comando commentato.
- `tests/dr-mcp-dbschema.Tests/DbSchemaHelpersTests.cs` — test per i due nuovi helper.

### Perimetro negativo (NON toccare)
- Flusso DDL Preview/Execute (Create/Alter/Drop) e `DdlTokenStore`.
- Gestione connessioni (`ConnectionState`, UseConnection/UseCustomConnection).
- Logica di scansione appsettings in `Program.cs` (solo aggiunta lettura `AllowSelect`).

## Design

### `RunSelect(sql, maxRows = 1000, ct)`
- Gated da `ddlSettings.AllowSelect`; se off → messaggio stile `DdlDisabledMessage("SELECT", "AllowSelect")`.
- Richiede connessione attiva (`NoConnectionMessage`).
- Valida con `IsReadOnlySelect`; se non valido → `[INVALID_INPUT]` con motivo.
- Esegue reader con `CommandTimeout` 30s; cap righe a `maxRows`; output tabellare testo.
- Logging strutturato con placeholder.

### `GenerateCommand(sql)`
- Nessun gate (pura generazione testo, nessun accesso DB).
- Se `IsReadOnlySelect(sql)` è `true` → rifiuta: indirizza a `RunSelect`.
- Altrimenti restituisce il comando passato da `CommentOutSql` con header `STATUS: NOT_EXECUTED`.

### `IsReadOnlySelect(string sql, out string reason)`
- Trim; vuoto → false.
- Rimuove `;` finali; se restano `;` interni → false (statement multipli).
- Primo token deve essere `SELECT` o `WITH`.
- Keyword vietate (word-boundary, case-insensitive): INSERT, UPDATE, DELETE, MERGE, DROP, CREATE,
  ALTER, TRUNCATE, EXEC, EXECUTE, GRANT, REVOKE, DENY, INTO, BACKUP, RESTORE, `sp_`, `xp_`.

### `CommentOutSql(string sql)`
- Prefissa ogni riga con `-- `.

## Fasi

- [x] F1 — `Models/DdlSettings.cs`: aggiunta `AllowSelect`.
- [x] F2 — `Program.cs`: lettura `AllowSelect`.
- [x] F3 — `Helpers/DbSchemaHelpers.cs`: `IsReadOnlySelect` + `CommentOutSql`.
- [x] F4 — `Tools/DbSchemaTools.cs`: `RunSelect` + `GenerateCommand`.
- [x] F5 — `appsettings.json`: flag `AllowSelect: false`.
- [x] F6 — Test helper nuovi.
- [x] F7 — README + istruzione modulare.
- [x] F8 — Build + test (`dotnet test -c Release`).

## Criteri di verifica

- [x] `dotnet build` ok.
- [x] `dotnet test -c Release` verde (62 test, vecchi + nuovi).
- [x] `RunSelect` con `AllowSelect:false` → messaggio disabilitato; con `true` e SQL non-SELECT → `[INVALID_INPUT]`.
- [x] `GenerateCommand` non esegue mai nulla e ritorna comando commentato con `-- `.
- [x] Flusso DDL esistente invariato (nessuna firma cambiata).
