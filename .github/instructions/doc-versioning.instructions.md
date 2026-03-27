---
applyTo: "docs/**/*.md"
---

# Versionamento della documentazione

Ogni volta che un documento in `docs/` viene creato o modificato, il footer deve essere aggiornato.

## Formato footer obbligatorio

```
*Revisione v{N} — {YYYY-MM-DD HH:MM} — {modello-llm}*
```

Esempi:
```
*Revisione v1.0 — 2026-03-23 14:30 — claude-sonnet-4-6*
*Revisione v1.1 — 2026-03-23 15:45 — claude-sonnet-4-6*
```

## Regole

| Campo | Regola |
|-------|--------|
| `v{N}` | Incrementa di 0.1 ad ogni modifica, di 1.0 se la struttura cambia radicalmente |
| `{YYYY-MM-DD HH:MM}` | Data e ora locale al momento della modifica |
| `{modello-llm}` | ID del modello usato (es. `claude-sonnet-4-6`, `claude-opus-4-6`) |

## Quando aggiornare

- Sempre, anche per modifiche minori (aggiunta di una riga, correzione typo)
- Il footer va in fondo al file, come ultima riga
- Se il documento non ha ancora un footer, aggiungilo alla prima modifica partendo da `v1.0`

*Template v1.0 — 2026-03-23 — claude-sonnet-4-6*
