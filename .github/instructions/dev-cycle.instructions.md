---
applyTo: "**"
---

# Dev Cycle — Ciclo Obbligatorio per Task AI

Regole trasversali valide per qualsiasi task di sviluppo. Le istruzioni modulari specifiche (es. `minimal-api-architecture`, `database-provider`) si sovrappongono a queste senza sostituirle.

---

## Fase 1: DICHIARA prima di agire

Prima di scrivere codice o modificare file, dichiara:
- Cosa farai (file e operazione)
- Perché (quale requisito soddisfa)
- Cosa NON farai (perimetro negativo esplicito)

Formato obbligatorio:
> "Modificherò `[file]` per `[motivo]`. Non toccherò `[fuori scope]`."

Se il task richiede più file, elencali in ordine prima di iniziare.

---

## Fase 2: ESEGUI un'operazione alla volta

- Una modifica per turno
- Non modificare più file contemporaneamente senza averli dichiarati in Fase 1
- Segui l'ordine dichiarato — non saltare passaggi

---

## Fase 3: VERIFICA dopo ogni modifica (ReAct loop)

Pattern **Reason → Act → Observe** per ogni step:

```
Reason  → "Devo fare X perché Y. Il prossimo passo è Z."
Act     → [esegui la modifica]
Observe → "Rileggo il file modificato. Il contenuto corrisponde? Sì/No."
          → Sì: "✓ [file] verificato. Prossimo passo: ..."
          → No: [correggi e ripeti Observe]
```

Regola: non passare al file successivo finché il file corrente non è verificato.

---

## Fase 4: CRITERI DI COMPLETAMENTO

Prima di dichiarare il task completato, verifica:
- [ ] Tutti i file modificati sono stati riletti e verificati
- [ ] Nessuna modifica al di fuori dello scope dichiarato in Fase 1
- [ ] Le checklist specifiche dell'istruzione modulare pertinente sono soddisfatte

---

## Segnalazione incertezza (obbligatoria)

Se non sei sicuro su come procedere, devi dichiararlo esplicitamente prima di agire:

> "Non sono sicuro su `[punto specifico]`. Le opzioni sono:
> - A) ...
> - B) ...
> Quale preferisci?"

Non procedere con assunzioni silenziose su punti ambigui.

---

## Scope lock

Se durante l'esecuzione il task richiede più di quanto dichiarato in Fase 1:
1. Fermati
2. Dichiara lo scope aggiuntivo
3. Chiedi conferma prima di procedere

Non espandere silenziosamente lo scope.

---

## Rollback gate

Se dopo la verifica (Fase 3) il file non corrisponde a quanto atteso:
1. Non dichiarare il task completato
2. Identifica la divergenza
3. Correggi il file
4. Rileggi e verifica di nuovo
5. Solo allora dichiara completato

In caso di modifiche a più file: se un file è errato, correggilo **prima** di procedere al successivo.

*Template v1.0 - Token-optimized for AI agents* - Last Update 2026-03-25 — claude-sonnet-4-6
