---
applyTo: "**"
---

# Structured Logging Rules (AI Agent)

Scopo: configurazione obbligatoria del logging strutturato con identificatori di contesto (thread ID, task ID). Segui sempre nei progetti .NET con Serilog.

## Dipendenze NuGet

```xml
<PackageReference Include="Serilog.Enrichers.Thread" Version="4.*" />
```

## Regole core (sempre)

1. Ogni evento di log deve includere `{ThreadId}` (ID thread gestito) e `{TaskId}` (ID task async, `-` se non in contesto Task)
2. Usa `.Enrich.WithThreadId()` per il thread ID (da `Serilog.Enrichers.Thread`)
3. Usa `.Enrich.With<TaskIdEnricher>()` per il task ID (custom enricher, vedi pattern)
4. I template Console e File devono entrambi esporre `T:{ThreadId}` e `A:{TaskId}`
5. Non usare `Thread.CurrentThread.ManagedThreadId` direttamente nel messaggio di log: usa l'enricher
6. Non usare string interpolation per includere thread/task nei messaggi: usa placeholder strutturati

## Template consigliati

### Console
```
{Timestamp:HH:mm:ss} [{Level:u3}] T:{ThreadId} A:{TaskId} {Message:lj}{NewLine}{Exception}
```

### File (rolling giornaliero)
```
{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] T:{ThreadId} A:{TaskId} {Message:lj}{NewLine}{Exception}
```

## Pattern richiesti (copiabili)

### Program.cs — configurazione Serilog completa
```csharp
builder.Services.AddSerilog((_, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.WithThreadId()
    .Enrich.With<TaskIdEnricher>()
    .WriteTo.Console(outputTemplate:
        "{Timestamp:HH:mm:ss} [{Level:u3}] T:{ThreadId} A:{TaskId} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate:
        "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] T:{ThreadId} A:{TaskId} {Message:lj}{NewLine}{Exception}"));
```

### TaskIdEnricher.cs
```csharp
using Serilog.Core;
using Serilog.Events;

internal sealed class TaskIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory) =>
        logEvent.AddPropertyIfAbsent(
            factory.CreateProperty("TaskId", Task.CurrentId?.ToString() ?? "-"));
}
```

Posizionare in `Infrastructure/Logging/TaskIdEnricher.cs` (`internal sealed` — non modificare la visibilità).

## Errori comuni (rapidi)

- `{ThreadId}` non appare nel log → `.Enrich.WithThreadId()` non chiamato o pacchetto non installato
- `{TaskId}` sempre `-` → normale per codice non in contesto `Task` (es. codice sincrono in `Main`)
- Template con `{Properties}` al posto di `{ThreadId}` → mostra tutti i campi enriched, verboso, da evitare in produzione
- `Task.CurrentId` è `null` fuori da un `Task` esplicito: il fallback `"-"` nell'enricher è obbligatorio

## ✅ Checklist Post-Configurazione

- [ ] `Serilog.Enrichers.Thread` aggiunto al `.csproj`
- [ ] `.Enrich.WithThreadId()` presente nella configurazione Serilog
- [ ] `TaskIdEnricher.cs` creato in `Infrastructure/Logging/` come `internal sealed`
- [ ] `.Enrich.With<TaskIdEnricher>()` presente nella configurazione Serilog
- [ ] Template Console include `T:{ThreadId} A:{TaskId}`
- [ ] Template File include `T:{ThreadId} A:{TaskId}`
- [ ] Nessun thread/task ID inserito manualmente nei messaggi di log via placeholder custom o string interpolation

*Template v1.0 - Logging - Token-optimized for AI agents* - Last Update 2026-05-29 — claude-sonnet-4-6
