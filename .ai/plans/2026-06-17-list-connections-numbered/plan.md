# Piano: ListConnections — griglia numerata
Data: 2026-06-17
Stato: COMPLETATO

## Obiettivo
Aggiungere numerazione progressiva all'output di `ListConnections()` e un hint testuale finale per guidare la selezione via numero.

## Scope
### File da modificare
- [x] `Tools/DbSchemaTools.cs` — aggiungere `.Select((name, i) => ...)` con indice 1-based + riga hint finale
- [x] `tests/dr-mcp-dbschema.Tests/ListConnectionsTests.cs` (nuovo) — unit test per il nuovo formato

### Perimetro negativo
- Non toccherò: `UseConnection`, `UseCustomConnection`, `GetActiveConnection`, `ConnectionState`, integration tests, altri tool

## Fasi
- [x] 1. Modificare `ListConnections()` in `DbSchemaTools.cs`: aggiungere indice 1-based, allineare formato bullet (`*` attiva, ` ` non attiva), aggiungere riga hint finale
- [x] 2. Creare `tests/dr-mcp-dbschema.Tests/ListConnectionsTests.cs` con casi: lista vuota, lista con 3 connessioni (numerazione, attiva marcata con `*`, hint presente)
- [x] 3. Eseguire `dotnet test` e verificare green

## Criteri di verifica finale
- [x] Output `ListConnections` inizia con `1.` per la prima connessione
- [x] Connessione attiva marcata con `*` come prefisso
- [x] Riga hint presente in fondo (contiene `UseConnection`)
- [x] Lista vuota restituisce `[NO_CONNECTIONS_CONFIGURED]` invariata
- [x] `dotnet test` green (nessun test preesistente rotto) — 40/40
