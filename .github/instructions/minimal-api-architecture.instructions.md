---
applyTo: "**"
---

# Minimal API Design Rules (AI Agent)

Scopo: regole obbligatorie per progetti Minimal API .NET 10. Segui sempre. Testo ottimizzato per token.

## Stack (obbligatorio)
- .NET 10, ASP.NET Core Minimal API (no Controllers)
- Tinyhelpers.AspNetCore (installare sempre)
- Scalar per docs (no Swagger UI)
- Asp.Versioning.Mvc.ApiExplorer (obbligatorio per Scalar)
- Serilog
- Entity Framework Core 10: chiedere prima di aggiungere il pacchetto
- SimpleAuthenticationTools (API Key): chiedere prima di aggiungere il pacchetto
- Aggiungi sempre il file launchSettings.json con configurazione per IIS Express e Kestrel
- Aggiungi sempre il file appsettings.local.json, aggiungi la chiamata in program.cs, e ignora il file in .gitignore
- Dati sensibili: segui sempre `sensitive-data.instructions.md`

## Vietato
- MVC Controllers
- Swagger UI
- IRepository pattern
- AutoMapper
- MediatR

## Struttura progetto
- src/<project>/
  - Dto/
  - Endpoints/
  - Infrastructure/Provider/{Entities,Filters,*DbContext.cs,*Provider.cs}
  - Transformers/
  - Properties/
  - Program.cs
- test/
- docs/

## Regole core (sempre)
1) Endpoint solo in extension methods in Endpoints/*Mapping.cs
2) URL standard: api/v{version:apiVersion}/{gruppo}/{comando?}
3) Usa route group con WithTags + WithApiVersionSet + MapToApiVersion
4) Versioning: UrlSegmentApiVersionReader + ApiExplorer GroupNameFormat='v'VVV + SubstituteApiVersionInUrl=true
5) Parametri handler: route -> query (o [AsParameters] se >2) -> body -> servizi DI -> CancellationToken ultimo
6) OpenAPI metadata completo: Produces + WithSummary + WithDescription + WithName
7) Program.cs deve chiamare gli extension methods dopo MapOpenApi
8) Transformer OpenAPI: classe AddDocumentInformations in Transformers/ + registrazione AddOpenApi
9) GET con provider: filtro dedicato, mapping manuale a DTO, ProblemDetails 404 se vuoto

## Pattern richiesti (copiabili)

### Extension method + group
```csharp
public static class WeatherMapping
{
    public static IEndpointRouteBuilder MapWeatherEndpoints(this IEndpointRouteBuilder routes, ApiVersionSet versionSet)
    {
        var group = routes.MapGroup("api/v{version:apiVersion}/weather")
            .WithTags("Weather")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(ApiVersionFactory.Version1);

        group.MapGet("/", GetHandler)
            .RequireAuthorization()
            .Produces<IEnumerable<WeatherDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetWeather")
            .WithSummary("Get weather")
            .WithDescription("Returns weather data");

        return routes;
    }
}
```

### Program.cs (versioning + openapi + mapping)
```csharp
builder.Services.AddApiVersioning(options =>
{
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
    options.DefaultApiVersion = ApiVersionFactory.Version1;
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<AddDocumentInformations>();
});

var versionSet = app.NewApiVersionSet()
    .HasApiVersion(ApiVersionFactory.Version1)
    .Build();

app.MapOpenApi();
app.MapWeatherEndpoints(versionSet);
```

### OpenAPI transformer
```csharp
public class AddDocumentInformations : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "<SolutionName> API";
        document.Info.Description = "<Short description>";
        document.Info.Version = "v1";
        document.Info.Contact = new OpenApiContact
        {
            Name = "Voisoft per Unidata spa, @ <Year>",
            Url = new Uri("https://www.twt.it/"),
            Email = "tron@twt.it"
        };

        return Task.CompletedTask;
    }
}
```

### GET con provider
```csharp
private static async Task<IResult> GetHandler(DateTime FromDate, DateTime ToDate, MyProvider provider, CancellationToken ct)
{
    var filter = new MyFilter { FromDate = FromDate, ToDate = ToDate };
    var result = await provider.GetAsync(filter, e => e.ToDto(), ct);
    if (!result.Any())
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Title = "Data Not Found",
            Status = StatusCodes.Status404NotFound,
            Detail = "No data for specified range."
        });
    }

    return Results.Ok(result);
}
```

## Errori comuni (rapidi)
- Version reader non UrlSegmentApiVersionReader => errore su MapToApiVersion
- Route senza api/v{version:apiVersion}/... => 404 o no route match
- Mancata MapToApiVersion => versione richiesta ma non specificata
- Scalar senza ApiExplorer config => nessun endpoint
- Provider non registrato in DI => Cannot resolve service
- Date query non ISO 8601 => DateTime conversion error

## ✅ Checklist Post-Generazione
- [ ] Endpoint solo in extension methods in Endpoints/*Mapping.cs
- [ ] Route group usa WithTags + WithApiVersionSet + MapToApiVersion
- [ ] URL formato api/v{version:apiVersion}/{gruppo}/{comando?}
- [ ] Versioning configurato con UrlSegmentApiVersionReader + ApiExplorer
- [ ] Metadata OpenAPI completi (Produces, Summary, Description, Name)
- [ ] Transformer AddDocumentInformations creato e registrato
- [ ] Program.cs chiama MapOpenApi prima dei Map*Endpoints
- [ ] GET con provider: filter + mapping DTO + ProblemDetails 404 se vuoto
- [ ] File .http aggiunto per endpoint nuovi
- [ ] `appsettings.json` contiene solo valori fake/placeholder per dati sensibili, mai credenziali reali

## 🎯 Criteri di successo (verificare prima di iniziare)

Prima di iniziare, chiediti:
- [ ] So esattamente quali file creerò/modificherò?
- [ ] Ho letto le istruzioni modulari pertinenti al task?
- [ ] Ho verificato che l'endpoint o il componente non esista già?

Se una risposta è NO → chiedi chiarimenti all'utente prima di procedere.

## Test
- Aggiungi sempre un file .http per endpoint nuovi

*Template v1.4 - .NET 10 - Token-optimized for AI agents* - Last Update 2026-03-25 — claude-sonnet-4-6

