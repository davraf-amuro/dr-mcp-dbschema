# Onboarding — dr-mcp-dbschema

## Il progetto in tre righe

`dr-mcp-dbschema` è un MCP Server .NET 10 che espone tool per ispezionare lo schema di un database SQL Server e per eseguire operazioni DDL (CREATE / ALTER / DROP) con conferma esplicita a due fasi. Viene installato come binario self-contained nel progetto ospite tramite `setup.ps1` e comunicato con il client MCP (Claude Code, VS Code Copilot, Cursor) via trasporto stdio. Non ha UI, non ha API HTTP.

---

## Stack e scelte tecniche

| Tecnologia | Versione | Motivo |
|------------|----------|--------|
| .NET 10 | 10.0 | Standard di progetto |
| C# 14 | — | Primary constructors, pattern matching avanzato |
| ModelContextProtocol | 1.1.0 | SDK ufficiale MCP — registrazione tool via `[McpServerTool]` |
| Microsoft.Data.SqlClient | 6.1.4 | Unico provider supportato (SQL Server only) |
| Serilog | 10.0 + File 7.0 | Log opzionale su file, abilitato da appsettings o env var |
| PublishSingleFile | — | Binario self-contained win-x64; `sni.dll` bundlata nell'exe |

Dipendenze da evitare: no Entity Framework, no ORM. Query dirette via `SqlCommand`.

---

## Come avviare il progetto

### Prerequisiti

- .NET 10 SDK installato (per build/test — non serve sull'host finale)
- SQL Server accessibile dalla macchina locale
- Docker Desktop (solo per i test Testcontainers)

### Build

```bash
dotnet build dr-mcp-dbschema.csproj
```

### Configurazione minima

Crea `appsettings.local.json` nella root (già in `.gitignore`):

```json
{
  "ConnectionStrings": {
    "MioDb": "Server=localhost;Database=NomeDb;Integrated Security=true;"
  }
}
```

### Avvio manuale (debug)

```bash
dotnet run --project dr-mcp-dbschema.csproj
```

Il server rimane in attesa su stdin. In produzione è il client MCP ad avviarlo come subprocess.

### Test

```bash
# Integration test con Testcontainers (richiede Docker)
dotnet test tests/DrMcpDbSchema.IntegrationTests/

# Test su DB locale reale
$env:DB_LOCAL_CONNECTION_STRING = "Data Source=localhost;Initial Catalog=MyDb;..."
dotnet test tests/DrMcpDbSchema.IntegrationTests/ --filter "Category=LocalDB"
```

---

## Struttura del codice

```
dr-mcp-dbschema/
├── Program.cs                  # Entry point: scansione appsettings, setup DI, avvio MCP server
├── Models/
│   ├── ConnectionState.cs      # Stato sessione: CS disponibili, CS attiva, searchRoot
│   ├── DdlSettings.cs          # Flag AllowCreate/Alter/Drop letti da appsettings
│   ├── DdlKind.cs              # Enum Create/Alter/Drop
│   └── PendingDdl.cs           # Dati operazione DDL in attesa di conferma (token, SQL, scadenza)
├── Tools/
│   └── DbSchemaTools.cs        # Tutti i tool MCP (classe con [McpServerToolType])
├── Services/
│   └── DdlTokenStore.cs        # Store token monouso (ConcurrentDictionary, TTL 60s)
├── Helpers/
│   └── DbSchemaHelpers.cs      # ExtractObjectName, AnalyzeAlterRisk, MaskConnectionString
└── tests/
    ├── dr-mcp-dbschema.Tests/
    │   ├── DbSchemaHelpersTests.cs     # Unit test helpers (ExtractObjectName, AnalyzeAlterRisk, MaskConnectionString)
    │   ├── DdlTokenStoreTests.cs       # Unit test token store
    │   └── ListConnectionsTests.cs     # Unit test output griglia numerata ListConnections
    └── DrMcpDbSchema.IntegrationTests/
        ├── McpEnvironmentFixture.cs    # Testcontainers: avvia SQL Server + server MCP
        ├── FullCycleTests.cs           # 12 step su DB isolato
        ├── LocalDbFixture.cs           # DB locale reale
        └── LocalDbRealCycleTests.cs    # 15 step su DB locale
```

**Entry point del comportamento:** tutto parte da `Program.cs` — scansiona gli appsettings, popola `ConnectionState`, configura il DI container e avvia il server MCP. I tool in `DbSchemaTools.cs` ricevono `ConnectionState` via primary constructor.

---

## Convenzioni obbligatorie

| Regola | Dove si applica |
|--------|-----------------|
| Primary constructor per dipendenze (`ConnectionState state, ...`) | `DbSchemaTools` e qualsiasi nuova classe con DI |
| `async/await` per ogni I/O su database | Tutti i tool che aprono `SqlConnection` |
| Logging strutturato con placeholder: `logger.LogInformation("{Name}", name)` — **mai** string interpolation | Ogni `logger.Log*` call |
| Connection string attiva **mai loggata** (anche a livello Debug) | `UseCustomConnection` e qualunque nuovo tool che gestisce CS |
| Token DDL monouso, TTL 60s, via `DdlTokenStore` | Tutti i flow Preview/Execute |
| `MaskConnectionString` nell'output utente, **mai il valore grezzo** | `Diagnostics`, `GetActiveConnection`, `UseCustomConnection` |

---

## Flusso di lavoro

### Branch e commit

- Branch principale: `main`
- Commit: Conventional Commits (`feat:`, `fix:`, `chore:`, ecc.)
- Versione in `dr-mcp-dbschema.csproj` → `<Version>X.Y.Z</Version>`

### Lint prima del push

```bash
dotnet format dr-mcp-dbschema.csproj --verify-no-changes
```

Push bloccato se lint non è clean (policy di progetto).

### Rilascio

Push di un tag `vX.Y.Z` su `main` → GitHub Actions compila e pubblica release win-x64 + linux-x64 con checksum SHA256.

```bash
git tag v0.3.1
git push origin v0.3.1
```

---

## Dati sensibili e configurazione locale

- Connection string: **sempre in `appsettings.local.json`** (già in `.gitignore`)
- Mai in `appsettings.json` committato
- L'env var `DB_CONNECTION_STRING` è un override esplicito — utile per CI o override temporaneo, non per configurazione permanente
- La CS inserita via `UseCustomConnection` (tool MCP) non viene mai loggata né persistita su disco

Riferimento completo: `.github/instructions/sensitive-data.instructions.md`

---

## Dove chiedere / cosa leggere dopo

| File | Contenuto |
|------|-----------|
| `.github/copilot-instructions.md` | Regole obbligatorie per l'agente AI nel progetto |
| `.github/instructions/dev-cycle.instructions.md` | Ciclo dichiarazione → esecuzione → verifica |
| `.github/instructions/sensitive-data.instructions.md` | Gestione credenziali e file locali |
| `.github/instructions/plan-tracking.instructions.md` | Piano su disco per task con ≥ 2 operazioni |
| `docs/card-dr-mcp-dbschema.md` | Scheda riassuntiva del progetto |
| `README.md` | Guida operativa completa (installazione, configurazione, esempi) |

---

*Revisione v1.1 — 2026-06-17 00:00 — claude-sonnet-4-6*
