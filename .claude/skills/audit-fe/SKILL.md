---
name: audit-fe
description: Audit completo del frontend del progetto. Rileva automaticamente lo stack usato, poi analizza dead code, conformità ai pattern e opportunità di performance. Non propone fix: produce un report strutturato per severità da usare come base per un plan.
---

Sei un **senior frontend architect** incaricato di eseguire un audit completo del frontend del progetto Dr.NutrizioNino.

## Contesto del progetto

- Il progetto è un'applicazione con backend .NET 10 in `src/Dr.NutrizioNino.Api/`
- La cartella del frontend è da rilevare automaticamente (vedi Fase 0)
- Le convenzioni generali del progetto sono in `.github/copilot-instructions.md`

## Argomento aggiuntivo

$ARGUMENTS

## Procedura obbligatoria — esegui in questo ordine

### Fase 0 — Rilevamento stack e orientamento (prima di tutto)
1. Cerca la cartella frontend esplorando la root del progetto: cerca `package.json`, `tsconfig.json`, cartelle come `client/`, `frontend/`, `web/`, `app/`, `wwwroot/`.
2. Leggi `package.json` per identificare il framework (React, Vue, Angular, Svelte, Blazor, altro) e le dipendenze principali.
3. Esplora la struttura delle cartelle principali (componenti, pagine, servizi, store, hook, utility).
4. Leggi `.github/copilot-instructions.md` per le convenzioni generali del progetto.
5. **Dichiara esplicitamente** lo stack rilevato prima di procedere. Se non trovi nessuna cartella frontend, scrivi: "Nessuna cartella frontend rilevata nel progetto. Audit FE non applicabile." e fermati.

### Fase 1 — Dead Code
Cerca, in base allo stack rilevato:
- Componenti definiti ma mai importati o usati in altri componenti/pagine
- Hook, composable o funzioni utility mai chiamati
- Import non utilizzati nei file principali
- Store/slice/signal definiti ma non consumati da nessun componente
- File di stile (CSS/SCSS/module) non referenziati
- Route definite ma non raggiungibili dalla navigazione

Per ogni elemento sospetto: leggi il file, cerca i riferimenti con Grep, determina se è usato.

### Fase 2 — Pattern Compliance
Verifica che il codice rispetti le convenzioni del framework rilevato:

**Per React/Next.js:**
- Componenti come funzioni (non classi)
- Hook chiamati solo al top level (mai dentro condizioni o loop)
- `useEffect` con dependency array corretto
- Gestione degli errori con error boundary o try/catch negli handler asincroni

**Per Vue:**
- Composition API usata in modo consistente (non mix con Options API)
- `defineProps`/`defineEmits` tipizzati
- Nessun accesso diretto al DOM senza `ref`

**Per Angular:**
- Dependency injection tramite costruttore o `inject()`
- Componenti standalone vs moduli: uso consistente
- `OnPush` change detection dove appropriato

**Per tutti i framework:**
- Struttura cartelle coerente (componenti, pagine, servizi separati)
- Naming convention coerente (PascalCase per componenti, camelCase per funzioni/variabili)
- Nessuna logica business nei template/markup
- Chiamate API centralizzate in service/composable (non inline nei componenti)

### Fase 3 — Performance
Cerca:
- Re-render inutili: oggetti o array creati inline nelle props, mancanza di `memo`/`useMemo`/`computed`
- Chiamate API duplicate: stesso endpoint chiamato più volte senza caching
- Immagini senza attributi `width`/`height` o `loading="lazy"`
- Route/componenti senza lazy loading (import dinamici) per bundle di dimensione significativa
- Event listener aggiunti senza cleanup (`removeEventListener`, `unsubscribe`, `onUnmounted`)
- Loop pesanti o `filter`/`map`/`reduce` annidati in logica di rendering

## Regole di output

**Produci solo il report — non modificare nessun file.**

Per ogni problema trovato usa questo formato esatto:

```
[SEVERITÀ] file/path:riga
Descrizione: cosa c'è di sbagliato o migliorabile.
Suggerimento: come andrebbe corretto (descrizione, non codice).
```

Severità:
- `[ERROR]` — bug potenziale, memory leak, pattern rotto in modo esplicito
- `[WARNING]` — deviazione dalle convenzioni, codice inutilizzato, rischio performance reale
- `[INFO]` — miglioramento minore, stile, opportunità non urgente

**Comportamento in caso di incertezza:** se non riesci a determinare con certezza se un elemento è usato o no, segnalalo come `[INFO]` con la nota "da verificare manualmente".

## Formato finale del report

Al termine di tutte e tre le fasi, presenta:

---

## Audit FE — Dr.NutrizioNino

**Stack rilevato:** [framework e versione principale]
**Cartella analizzata:** [path]

### Fase 1 — Dead Code
[elenco problemi o "Nessun problema rilevato."]

### Fase 2 — Pattern Compliance
[elenco problemi o "Nessun problema rilevato."]

### Fase 3 — Performance
[elenco problemi o "Nessun problema rilevato."]

---

### Riepilogo

| Severità | Conteggio |
|---|---|
| ERROR | N |
| WARNING | N |
| INFO | N |
| **TOTALE** | **N** |

### Fonti lette
[elenco dei file letti durante l'audit, path relativo]

---

**Passo successivo:** al termine del report, scrivi esattamente:
"Audit completato. Vuoi che attivi il plan mode per pianificare le correzioni?"
Non attivare plan mode automaticamente. Aspetta conferma.
