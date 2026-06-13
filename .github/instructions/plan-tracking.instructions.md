---
applyTo: "**"
---

# Plan Tracking — Tracciamento Obbligatorio dei Task

Regola trasversale valida per qualsiasi task di sviluppo. Si integra con `dev-cycle.instructions.md` senza sostituirlo.

---

## Quando si applica

Ogni richiesta di modifica (codice, config, docs, test) con **≥ 2 operazioni**.

Per operazione singola: dichiarazione inline dev-cycle Fase 1 è sufficiente — nessun piano su disco richiesto.

---

## Fase 0: CREA PIANO SU DISCO (prima di EnterPlanMode)

### Struttura cartella

```
.ai/plans/
└── <YYYY-MM-DD>-<slug>/
    └── plan.md
```

- slug: breve descrizione kebab-case del task (es. `add-auth-endpoint`, `fix-ef-projection`)
- Data: data di inizio in formato `YYYY-MM-DD`
- Cartella `.ai/` è esente da EnterPlanMode — scrivi senza blocchi

### Template obbligatorio `plan.md`

```markdown
# Piano: <titolo task>
Data: <YYYY-MM-DD>
Stato: IN CORSO

## Obiettivo
<descrizione obiettivo — una riga>

## Scope
### File da modificare
- [ ] `<percorso>` — <motivo>

### Perimetro negativo
- Non toccherò: <lista esplicita>

## Fasi
- [ ] 1. <descrizione fase>
- [ ] 2. <descrizione fase>

## Criteri di verifica finale
- [ ] <criterio misurabile>
- [ ] <criterio misurabile>
```

---

## Fase 1–3: Esecuzione

Segui `dev-cycle.instructions.md` (Dichiara → Esegui → Verifica).

Aggiorna `plan.md` durante l'esecuzione:
- Marca `[x]` ogni fase completata dopo la verifica dev-cycle Fase 3
- Se il lavoro viene interrotto, aggiorna `Stato: INTERROTTO` e salva prima di chiudere la sessione

---

## Fase 4: VERIFICA FINALE (obbligatoria)

Prima di dichiarare il task completato:

1. Rileggi `plan.md`
2. Verifica ogni criterio in "Criteri di verifica finale"
3. Verifica ogni file in "Scope" — riletto e confermato
4. Se tutti i criteri soddisfatti:
   - Aggiorna `Stato: COMPLETATO`
   - Dichiara esplicitamente: `"Piano [slug] verificato. Tutti i criteri soddisfatti."`

Se un criterio non è soddisfatto:
- Non dichiarare completato
- Aggiungi nota `⚠️ Divergenza: <descrizione>` in `plan.md`
- Correggi → ri-verifica dev-cycle Fase 3
- Rimuovi la nota divergenza → aggiorna `Stato: COMPLETATO`
- Solo allora dichiara completato

---

## Gestione interruzioni

Piano `IN CORSO` esistente all'avvio sessione:
1. Leggi `plan.md` per ricostruire il contesto
2. Identifica ultima fase con `[x]` completata
3. Riprendi dalla prima fase ancora `[ ]`
4. Non aprire nuovo piano — continua quello esistente

Piano `INTERROTTO` esistente: decidi con l'utente se riprendere o archiviare prima di procedere.

---

## Regole di perimetro

- Piano su disco obbligatorio per ogni task con ≥ 2 operazioni
- Non eliminare piani completati — sono traccia storica
- Non aprire nuovo piano se esiste piano `IN CORSO` non completato

---

*Istruzione v1.1 - Plan Tracking - 2026-06-10 — claude-sonnet-4-6*
