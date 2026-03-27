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
   - Ignora auth/validation
   - Ignora dettagli query
   - Mostra: Endpoint -> Provider/Service -> Entity -> DTO -> Response
5. Esempi: se esistono file .http, cita "Per i casi d'uso fare riferimento a <elenco_file_http>"
6. Ultimo aggiornamento: footer con data

## Footer
Usa la data corrente (Get-Date -Format "yyyy-MM-dd"):
```markdown
---
*Card generata il: yyyy-MM-dd | Versione template: x.x | LLM: GitHub Copilot*
```
La versione template e in fondo a questo file.

## ✅ Checklist Post-Generazione
- [ ] Un file per ogni MapGroup
- [ ] Tabelle endpoint complete, senza dati inventati
- [ ] Flussi essenziali (no auth, no dettagli query)
- [ ] Riferimenti a file .http se presenti
- [ ] Footer con data e LLM

*Template v1.2 - .NET 10 - Token-optimized for AI agents* - Last Update 2026-03-17 21:28