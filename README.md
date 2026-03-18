# mcp-db-schema

Tool MCP per leggere lo schema di tabelle e viste di un database SQL Server, pensato per agenti AI che devono lavorare sulla struttura del DB.

## Tool disponibili

| # | Tool | Descrizione |
|---|------|-------------|
| 1 | `list_connections` | Elenca le connection string reali trovate negli appsettings |
| 2 | `change_connection` | Cambia la connection string attiva per la sessione |
| 3 | `get_schema` | Legge lo schema completo (o filtrato) del database |

## Configurazione

### Variabili d'ambiente

| Variabile | Obbligatoria | Descrizione |
|---|:---:|---|
| `MCP_PROJECT_PATH` | ✅ | Percorso della cartella che contiene gli appsettings del progetto |

## Aggiungere al progetto

```bash
git clone https://git.uniot.eu/voisoft/mcp/mcp-db-schema.git tools/mcp-db-schema
```

Aggiungere al file `.mcp.json` nella root del progetto:

```json
{
  "mcpServers": {
    "dr-mcp-dbschema": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "tools/mcp-db-schema/mcp-db-schema.csproj", "--"],
      "env": {
        "MCP_PROJECT_PATH": "/percorso/al/progetto/src/NomeProgetto.Api"
      }
    }
  }
}
```

Poi riavvia Claude Code per caricare il server.

## Esempi d'uso

### list_connections

```
list_connections → (nessun parametro)
```

### change_connection

```
change_connection → connectionName: DrNutrizioNinoSql
```

### get_schema

```
get_schema → (nessun parametro, restituisce tutto lo schema)
get_schema → tableName: Foods
```

## Requisiti

- .NET 10 SDK
- SQL Server accessibile dalla macchina in cui gira il tool
- Connection string reale negli appsettings (`appsettings.json`, `appsettings.Development.json` o `appsettings.local.json`)
