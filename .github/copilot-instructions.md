# Copilot Instructions (AI Agent)

Language: Italian — Rispondi sempre in italiano.

Progetto .NET 10. Rileva il tipo dal codice prima di procedere.

## Tipo di progetto

| Segnale nel codice | Tipo | Istruzione modulare |
|--------------------|------|---------------------|
| ``Workers/*.cs`` presente | Windows Service | ``windows-service.instructions.md`` |
| ``Endpoints/*.cs`` presente | Minimal API | ``minimal-api-architecture.instructions.md`` |
| Nessun segnale riconoscibile | Tipo non rilevato | Fermati. Chiedi: "Questo è un Minimal API o un Windows Service?" |

Leggi sempre l'istruzione modulare corretta prima di generare o modificare codice.

## Convenzioni essenziali (tutti i tipi)
- Primary constructors, async/await per I/O
- Logging strutturato con placeholder (no string interpolation nei log)
- Naming: namespace snake_case, classi PascalCase, variabili camelCase
- Validazione input: ogni endpoint con body usa ``IValidator<T>``; segui ``input-validation.instructions.md``
- Dati sensibili: segui sempre ``sensitive-data.instructions.md``; credenziali **mai** in file committati

## Checklist Pre-Task (obbligatoria)

Prima di qualsiasi azione:
- [ ] Ho letto `.github/copilot-instructions.md`? (cita sezione rilevante)
- [ ] Ho identificato e letto il file istruzione modulare pertinente? (indica quale)
- [ ] Ho dichiarato scope, file da modificare e perimetro negativo?
- [ ] So esattamente quali file creerò/modificherò? (elencali)
- [ ] Ho verificato che la struttura richiesta non esista già nel progetto?

Anche una sola risposta NO → fermati e completa il passo prima di procedere.

## Checklist Post-Generazione
- [ ] Tipo rilevato correttamente, istruzione modulare letta
- [ ] Ho seguito le istruzioni modulari pertinenti
- [ ] Logging strutturato e async/await usati dove serve

## Verifica post-modifica (qualsiasi file)
Dopo ogni modifica a un file:
1. Rileggi il file modificato
2. Confronta il contenuto con quanto richiesto
3. Solo se corrispondono, dichiara la modifica completata

## Ciclo di sviluppo obbligatorio
Ogni task segue il ciclo definito in ``dev-cycle.instructions.md``:
- **Dichiara** scope e file prima di agire
- **Esegui** un'operazione alla volta
- **Verifica** (rileggi) dopo ogni modifica
- **Segnala** incertezza - non assumere silenziosamente

Task con >= 2 operazioni: crea piano su disco in ``.ai/plans/<YYYY-MM-DD>-<slug>/`` prima di procedere.
Segui ``plan-tracking.instructions.md`` per struttura e verifica finale.

## Gate di Push — Lint obbligatorio

⛔ Prima di qualsiasi `git push`, eseguire sempre la verifica lint in base al tipo di progetto:

| Tipo | Comando |
|------|---------|
| .NET | `dotnet format <percorso>.csproj --verify-no-changes` |
| Node.js | `npm run lint` (se lo script `lint` è definito in `package.json`) |
| Python | `ruff check .` oppure `flake8` |

| Exit code | Azione |
|-----------|--------|
| `0` | Lint clean — push consentita |
| Non-zero | **BLOCCA la push** — segnala le violazioni |

In caso di blocco:
1. Elenca i file con violazioni (dall'output del comando lint)
2. Chiedi conferma prima di applicare correzioni automatiche
3. Riesegui il check, poi procedi con la push

> Regola assoluta: nessun `git push` senza lint clean confermato.

*Template v1.6 - .NET 10 - Token-optimized for AI agents* - Last Update 2026-06-10 - claude-sonnet-4-6
