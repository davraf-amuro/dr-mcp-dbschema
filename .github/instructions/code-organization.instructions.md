---
applyTo: "**"
---

# Code Organization — Struttura e Responsabilità del Codice

Regole trasversali per organizzare il codice in modo leggibile, manutenibile e riusabile.

---

## Regola 1 — Una classe = un file

- Ogni classe, interfaccia, enum o tipo composto ha il suo file dedicato.
- Il nome del file corrisponde al nome della classe (case convention del linguaggio).
- Non raggruppare classi non correlate nello stesso file.

```
// ✅
UserDto.cs
EmailService.ts
order_mapper.py

// ❌
Models.cs         // contiene UserDto, OrderDto, ProductDto insieme
helpers.ts        // logica email, parsing, validazione mescolati
```

---

## Regola 2 — Cartella = ruolo della classe

| Cartella | Contenuto |
|---|---|
| `Dto/` o `Models/` | Oggetti di trasporto dati. Solo proprietà. |
| `Services/` | Logica applicativa. Orchestrazione. |
| `Repositories/` o `Infrastructure/` | Accesso dati (DB, file, API esterne). |
| `Mappers/` o `Transformers/` | Conversioni tra tipi. |
| `Helpers/` o `Utils/` | Funzioni generiche riutilizzabili, stateless. |
| `Validators/` | Validazione input e regole di business. |
| `Events/` o `Handlers/` | Definizione eventi e gestori. |
| `Factories/` | Creazione oggetti complessi o condizionali. |

Aggiungere nuove cartelle solo se motivate da un concetto di dominio reale.

---

## Regola 3 — Separazione funzioni generiche vs specifiche (SRP)

Se una funzione può essere usata da più contesti, non appartiene alla classe che la usa per prima. Un metodo privato che cresce fino a diventare logica riutilizzabile va estratto.

> Una funzione è generica se non dipende dallo stato specifico della classe ospite.

```csharp
// ❌ GetTemplate è logica generica nascosta in EmailService
public class EmailService
{
    public async Task SendAsync(string to, string templateName)
    {
        var template = await GetTemplate(templateName);
        // ...
    }
    private async Task<string> GetTemplate(string name) { /* legge da disco/DB */ }
}

// ✅ GetTemplate estratto in TemplateService
public class TemplateService
{
    public async Task<string> GetAsync(string name) { /* logica generica */ }
}

public class EmailService(TemplateService templates)
{
    public async Task SendAsync(string to, string templateName)
    {
        var template = await templates.GetAsync(templateName);
        // ...
    }
}
```

```typescript
// ❌ parser CSV annidato nel servizio di importazione
class ImportService {
  import(raw: string) {
    const rows = this.parseCsv(raw);
    // ...
  }
  private parseCsv(data: string): string[][] { /* ... */ }
}

// ✅ parser estratto
// utils/csv-parser.ts
export function parseCsv(data: string): string[][] { /* ... */ }

// services/import-service.ts
import { parseCsv } from '../utils/csv-parser';
class ImportService {
  import(raw: string) {
    const rows = parseCsv(raw);
    // ...
  }
}
```

---

## Regola 4 — Pattern adatti alla situazione

Applicare un pattern solo se risolve un problema concreto nel contesto attuale.

> "Uso [Pattern] perché [problema concreto che risolve in questo contesto]."

Se la risposta è "perché si fa così" → non applicarlo.

| Pattern | Quando usarlo |
|---|---|
| **Strategy** | Comportamento intercambiabile a runtime (es. più provider di notifica). |
| **Factory** | Creazione condizionale o complessa (oggetto dipende da config/input). |
| **Decorator** | Aggiungere comportamento senza modificare la classe (es. caching, logging). |
| **Observer/Event** | Disaccoppiamento produttore/consumatore (es. evento "ordine creato"). |
| **Pipeline** | Trasformazioni sequenziali su un input (validazione, trasformazione). |

---

## Regola 5 — Dipendenze tra classi

- Le dipendenze vanno dichiarate esplicitamente (costruttore o parametro), non istanziate internamente.
- Una classe non crea le proprie dipendenze: le riceve.
- Evitare dipendenze circolari — se due classi dipendono l'una dall'altra, estrarre un terzo concetto.

---

## Regola 6 — Commenti minimi sulle funzioni principali

Ogni funzione o metodo con logica non ovvia deve avere un commento breve (una riga) che descriva **a cosa serve**.

- In **C#**: usare `///` (XML doc comment) — visibile in IntelliSense e nei tooltip dell'IDE.
- In **TypeScript/JavaScript**: usare `//` o `/** */` JSDoc.
- In **Python**: usare una docstring `"""..."""` su una riga.

**Scope obbligatorio**: funzioni pubbliche, metodi di servizio, factory method, entry point.  
**Esclusi**: getter/setter banali, wrapper di una riga, override con comportamento ovvio.

```csharp
// ✅ C# — XML doc comment visibile in IntelliSense
/// <summary>Invia un'email usando il template specificato.</summary>
public async Task SendAsync(string to, string templateName) { ... }

/// <summary>Carica il testo del template dal provider configurato.</summary>
public async Task<string> GetAsync(string name) { ... }

// ❌ commento inutile — non aggiunge informazione
/// <summary>Metodo SendAsync.</summary>
public async Task SendAsync(...) { ... }
```

```typescript
// ✅ TypeScript
/** Verifica che l'utente abbia i permessi per accedere alla risorsa. */
function canAccess(user: User, resource: Resource): boolean { ... }
```

```python
# ✅ Python
def parse_csv(data: str) -> list[list[str]]:
    """Converte una stringa CSV in matrice di righe e colonne."""
    ...
```

---

## ✅ Checklist pre-commit

- [ ] Ogni classe, interfaccia o tipo ha il suo file dedicato?
- [ ] Il nome del file corrisponde al nome della classe?
- [ ] La cartella riflette il ruolo della classe nel sistema?
- [ ] Ci sono metodi privati che potrebbero essere estratti in un servizio/helper?
- [ ] Il pattern scelto è motivato da un problema concreto, non da abitudine?
- [ ] Le dipendenze sono iniettate, non istanziate internamente?
- [ ] Le funzioni principali hanno un commento `///` (C#) o equivalente?

---

*Istruzione v1.1 - Code Organization - 2026-04-17 — claude-sonnet-4-6*
