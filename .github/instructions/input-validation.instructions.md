---
applyTo: "**"
---

# Input Validation Rules (AI Agent)

Scopo: regole obbligatorie per la validazione di ogni dato in ingresso da fonti esterne. Segui sempre quando crei o modifichi un endpoint o qualsiasi processo ricevente.

## Obbligo fondamentale

⛔ **Ogni processo che riceve dati dall'esterno (endpoint HTTP, webhook, consumer, ecc.) deve avere un validatore.**

- Il validatore va scritto **sempre**, senza eccezioni.
- Se il developer autorizza la mancata validazione di uno o più campi, il validatore va scritto comunque: i campi esenti non aggiungono errori.
- Se il developer autorizza di non validare nulla, il validatore ritorna sempre `ValidationResult.Success()`.
- **Solo il developer può concedere esenzioni**, dichiarandole esplicitamente campo per campo con motivazione.

Prima di scrivere il validator di un **body** (POST/PUT/PATCH), **chiedi al developer come validare ogni singolo campo in input**. Non assumere regole di validazione in autonomia.

> **Eccezione — schema disponibile via MCP `db-schema`**: non chiedere nulla. Vedi sezione dedicata sotto.

> **Eccezione — filtri di entità (query string GET)**: non chiedere nulla — le regole si ereditano dai metadati dell'entità. Vedi sezione «Validazione filtri di entità».

**Fallback — nessuna specifica disponibile:** crea comunque il validator, che ritorna sempre `ValidationResult.Success()`, con commento `// TODO: validazione` sui campi senza regola. Il programmatore fisserà i parametri corretti in seguito. Il validator sempre-valido tiene il punto di aggancio nel flusso.

---

## Override: schema disponibile via MCP db-schema

Se la struttura della tabella è stata letta tramite MCP `db-schema`, **non chiedere le regole di validazione** — genera il validator direttamente:

- Regole inferibili dal metadato → applica subito (vedi mapping in `minimal-api-architecture.instructions.md` § "Scoperta automatica struttura DB via MCP")
- Campi con regole non determinabili → nessun errore per quel campo + commento `// TODO: validazione`
- Il validator deve compilare ed essere funzionante da subito

Il comportamento standard ("chiedi prima") si applica **solo** quando lo schema non è disponibile via MCP.

---

## Pattern obbligatorio

### Struttura cartella

```
src/<project>/
  Validators/
    IValidator.cs          ← interfaccia + record base (una volta sola per progetto)
    <Entity>Validator.cs   ← implementazione per ogni DTO/request in ingresso
```

### `Validators/IValidator.cs` (base — non duplicare)

```csharp
namespace <Project>.Validators;

public interface IValidator<T>
{
    ValidationResult Validate(T input);
}

public record ValidationResult(bool IsValid, IDictionary<string, string[]> Errors)
{
    public static ValidationResult Success() => new(true, new Dictionary<string, string[]>());
    public static ValidationResult Failure(IDictionary<string, string[]> errors) => new(false, errors);
}
```

### `Validators/<Entity>Validator.cs` (implementazione)

```csharp
namespace <Project>.Validators;

public class MyRequestValidator : IValidator<MyRequestDto>
{
    public ValidationResult Validate(MyRequestDto input)
    {
        var errors = new Dictionary<string, string[]>();

        // Valida ogni campo secondo le istruzioni del developer
        if (string.IsNullOrWhiteSpace(input.Name))
            errors["name"] = ["Name is required"];

        if (input.Amount <= 0)
            errors["amount"] = ["Amount must be greater than 0"];

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
```

### Registrazione in `Program.cs`

```csharp
builder.Services.AddScoped<IValidator<MyRequestDto>, MyRequestValidator>();
```

### Uso nel handler (Minimal API)

```csharp
private static async Task<IResult> PostHandler(
    MyRequestDto request,
    IValidator<MyRequestDto> validator,
    MyProvider provider,
    CancellationToken ct)
{
    var validation = validator.Validate(request);
    if (!validation.IsValid)
        return TypedResults.ValidationProblem(validation.Errors);

    // logica di business
    var result = await provider.SaveAsync(request, ct);
    return TypedResults.Created($"/api/v1/resource/{result.Id}", result);
}
```

### Metadata OpenAPI obbligatorio per endpoint con body

Aggiungi sempre `Produces(StatusCodes.Status400BadRequest)` al metadata dell'endpoint:

```csharp
group.MapPost("/", PostHandler)
    .Produces<MyResponseDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)   // ← obbligatorio se c'è validazione
    .WithName("CreateResource")
    .WithSummary("Create resource")
    .WithDescription("Creates a new resource. Returns 400 if input is invalid.");
```

---

## Regola di esenzione (skip)

| Condizione | Comportamento |
|---|---|
| Developer autorizza skip su campo X | Il validator non aggiunge errori per X, ma controlla tutto il resto |
| Developer autorizza skip su tutti i campi | Il validator ritorna sempre `ValidationResult.Success()` |
| Nessuna autorizzazione | Tutti i campi in input **devono** essere validati |

Il validator deve essere **sempre presente** anche nei casi di esenzione totale.

---

## Validazione filtri di entità — regole ereditate

I filtri GET (`<Entity>Filter`) ricevono dati esterni → validator obbligatorio (`<Entity>FilterValidator : IValidator<<Entity>Filter>`). Le regole **non si chiedono al developer**: si ereditano dai metadati dell'entità/colonna.

| Metadato entità/colonna | Regola filtro |
|---|---|
| `varchar(N)` / `nvarchar(N)` | `maxLength = N` sul campo filtro corrispondente |
| Campo filtro (qualsiasi) | Sempre opzionale → mai `required` |
| Numerici, date, bool | Tipo garantito dal model binding → nessuna regola |
| Regola non inferibile dai metadati | Nessun errore per quel campo + commento `// TODO: validazione` |

- Handler GET: valida il filtro **prima** della query → `TypedResults.ValidationProblem(errors)`; endpoint con `Produces(StatusCodes.Status400BadRequest)`
- Entità senza vincoli inferibili → validator sempre-valido (fallback in «Obbligo fondamentale»)
- Genera subito, codice compilabile — il programmatore raffina le regole in seguito

---

## ✅ Checklist Post-Generazione

- [ ] `Validators/IValidator.cs` creato (se non esiste già nel progetto)
- [ ] `Validators/<Entity>Validator.cs` creato con regole dichiarate dal developer
- [ ] Validator registrato in `Program.cs` (`AddScoped<IValidator<T>, TValidator>`)
- [ ] Validator iniettato e chiamato nel handler prima della logica di business
- [ ] Handler ritorna `TypedResults.ValidationProblem(errors)` in caso di fallimento
- [ ] Endpoint ha `Produces(StatusCodes.Status400BadRequest)` nel metadata
- [ ] GET con filtro: `<Entity>FilterValidator` creato con regole ereditate dall'entità, chiamato prima della query
- [ ] Nessuna specifica disponibile → validator sempre-valido con `// TODO: validazione`
- [ ] Se campi esenti: developer li ha dichiarati esplicitamente con motivazione

---

## Errori comuni

- Validatore non registrato in DI → `Cannot resolve service IValidator<T>` a runtime
- `TypedResults.ValidationProblem` richiede `IDictionary<string, string[]>` — non `IEnumerable<string>`
- Non chiamare il validator in un endpoint filter globale: ogni endpoint ha il proprio validator specifico
- Non riutilizzare lo stesso validator per DTO diversi anche se i campi si sovrappongono

*Istruzione v1.1 - Input Validation - 2026-06-10 — claude-fable-5*
