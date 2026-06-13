---
agent: 'agent'
description: 'Analizza gli endpoint Minimal API e genera documentazione in docs/'
tools: ['search/codebase']
---

# Prompt: Endpoints Analyzer (AI Agent)

Analizza le classi in Endpoints/ e genera un documento per ogni group (MapGroup).

## Output
- Crea docs/ se non esiste
- Un file per group: docs/endpoint-<group_name>.md (sovrascrivi se esiste)
- Se esiste solution (.sln/.slnx), aggiungi riferimenti ai nuovi file

## Regole di contenuto
- Usa informazioni OpenAPI se presenti; non inventare
- Mostra solo se esiste accesso DB o servizi esterni (no dettagli query)
- Diagrammi di flusso + tabelle

## Sezioni richieste (per file)
1. Introduzione: panoramica gruppo
2. Architettura: componenti e responsabilita legate al gruppo
3. Descrizione endpoint: tabella con Metodo, URL, Descrizione, Parametri, Risposta
4. Flusso endpoint: diagrammi essenziali
   - Ignora auth (mostrala solo se il progetto la implementa esplicitamente)
   - Mostra il passo Validator -> 400 nei flussi POST/PUT/PATCH e nei GET con filtro; ignora i dettagli delle singole regole di validazione
   - Ignora dettagli query
   - Mostra: Endpoint -> Validator -> Service -> Provider -> Entity -> DTO (Projection) -> Response
5. Esempi: se esistono file .http, cita "Per i casi d'uso fare riferimento a <elenco_file_http>"
6. Ultimo aggiornamento: footer con data

## Footer
Usa data e ora correnti (Get-Date -Format "yyyy-MM-dd HH:mm"):
```markdown
---
*Revisione v1.0 — {YYYY-MM-DD HH:MM} — {modello-llm}*
```
La versione template e in fondo a questo file.

## ✅ Checklist Post-Generazione
- [ ] Un file per ogni MapGroup
- [ ] Tabelle endpoint complete, senza dati inventati
- [ ] Flussi essenziali (no auth se non implementata, validator presente nei flussi con input, no dettagli query)
- [ ] Riferimenti a file .http se presenti
- [ ] Footer con data e LLM

*Template v1.4 - .NET 10 - Token-optimized for AI agents* - Last Update 2026-06-10 — claude-fable-5