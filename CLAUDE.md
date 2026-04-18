# Linee guida per Claude Code

## Lingua
- Rispondi sempre in **italiano**

## Comportamento generale
- Termine tecnico errato o impreciso → segnala gentilmente + forma corretta

## Regola fondamentale — Compatibilità duale agente

⛔ OGNI regola, istruzione, convenzione o linea guida creata o modificata in questo progetto **deve essere compatibile sia con Claude Code che con GitHub Copilot**.

- Preferire sintassi e struttura neutra, leggibile da entrambi
- No feature esclusive di un solo tool
- Verifica compatibilità prima di proporre o applicare regola
- Compatibilità non garantita → **fermati, chiedi all'utente** — no assunzioni, no azione autonoma

## Standard di progetto .NET
@.github/copilot-instructions.md

## Regola MCP Server
@.github/instructions/mcp-server-discovery.instructions.md

## Modifiche al codice

⛔ STOP — Prima di scrivere codice, completa tre passi e documentali nell'output:

1. **Leggi** `.github/copilot-instructions.md` e **cita** sezione rilevante per task corrente.
2. **Identifica e leggi** file `.github/instructions/*.md` pertinente. Se incerto, elenca file disponibili e scegli.
3. **Dichiara** scope, file da modificare e cosa NON toccare — formato obbligatorio:
   > "Modificherò `[file]` per `[motivo]`. Non toccherò `[fuori scope]`."

No procedere finché tre passi non completati e visibili nell'output.

⛔ OBBLIGO DI RENDICONTO — Prima di scrivere codice, elenca nell'output tutti i file letti:

```
File letti:
- .github/copilot-instructions.md  ✓
- .github/instructions/database-provider.instructions.md  ✓
```

File non letto che andava letto → dichiara `✗ non letto` e leggilo prima di procedere. No elenco visibile = no procedere.

---

### Checklist pre-task (obbligatoria, da compilare ad ogni task)

- [ ] Ho letto `.github/copilot-instructions.md`? (cita sezione rilevante)
- [ ] Ho identificato e letto file istruzioni modulare pertinente? (indica quale)
- [ ] Ho dichiarato scope, file da modificare e perimetro negativo?
- [ ] So esattamente quali file creerò/modificherò? (elencali)
- [ ] Ho verificato che struttura richiesta non esista già nel progetto?

Anche una risposta NO → fermati, completa passo prima di procedere.

---

⛔ OGNI MODIFICA — a qualsiasi file (codice, docs, config, test) — richiede piano approvato.

1. Usa `EnterPlanMode` per proporre piano
2. Dichiara: scope, file da modificare, motivazione, perimetro negativo
3. Attendi approvazione esplicita utente
4. Usa `ExitPlanMode` per procedere

Hook `pre_tool_use.py` blocca `Edit`/`Write`/`MultiEdit` automaticamente (validità 30 minuti dall'ultimo `ExitPlanMode`). Percorsi esenti: `.claude/` · `.ai/`

## Citazione fonti e modello

Fine risposta, se letti file o consultati documenti:
- Cita file usati come fonti (path relativo)
- Indica modello LLM usato (es. `claude-sonnet-4-6`)

## Invocazione automatica delle skill

Intento utente corrisponde a skill disponibile → **invoca direttamente** senza conferma. Usa contesto conversazione come argomento.

| Se l'utente dice qualcosa come... | Invoca |
|-----------------------------------|--------|
| "vai professor", "scrivi la doc", "aggiorna il README", "genera la scheda del progetto", "documenta gli endpoint", "prepara l'onboarding" | `/professor [richiesta]` |
| "consulta il warroom", "sentiamo le opinioni", "apri il tavolo", "cosa ne pensano gli esperti", "discutiamo questa scelta" | `/warroom [domanda o contesto]` |
| "chiedi al tattico", "rivedi questo prompt", "migliora il prompt", "scrivi un prompt per", "perché questo prompt non funziona" | `/tattico [prompt o descrizione]` |
| "pianifica il rilascio", "prepara l'ambiente", "come si deploya", "configura Docker", "procedura di deploy" | `/tech [task]` |
| "promote", "promuovi il branch", "crea la PR verso", "merge su", "porta su master/main/staging" | `/promote-to [target-branch] [--merge] [--delete]` |
| "audit api", "fai l'audit del backend", "analizza le api", "cerca dead code", "controlla il codice backend" | `/audit-api [focus opzionale]` |
| "audit frontend", "fai l'audit del fe", "analizza il frontend", "controlla i componenti" | `/audit-fe [focus opzionale]` |

Invoca skill → passa tutto contesto utile già in conversazione (codice aperto, domanda originale, file citati) — no chiedere all'utente di ripetere.