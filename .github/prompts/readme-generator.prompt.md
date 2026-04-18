---
agent: 'agent'
description: 'Crea o aggiorna il README.md usando solo dati presenti nel repository'
tools: ['search/codebase']
---

# Prompt: README Generator (AI Agent)

Crea o aggiorna README.md usando solo dati presenti nel repository. Non inventare.

## Obiettivo
- Titolo progetto
- Panoramica breve
- Sezione Documentazione (docs/) con elenco file .md
- Sezione Quick Links invariata

## Istruzioni operative
1) Panoramica breve e concreta
2) Elenca tutti i .md in docs/ (escludi readme.md se autoreferenziale)
3) Una descrizione breve per ogni file
4) Quick Links identici a quelli del README di riferimento
5) Nessuna sezione extra

## Footer
Usa data e ora correnti (Get-Date -Format "yyyy-MM-dd HH:mm"):
```markdown
---
*Card generata il: yyyy-MM-dd HH:mm | Versione template: x.x | LLM: [indicare il modello usato]*
```
La versione template e in fondo a questo file.

## Regole
- Non inventare dati
- Tono conciso
- Se un file non esiste, non inserirlo

## ✅ Checklist Post-Generazione
- [ ] README.md aggiornato con titolo e panoramica
- [ ] Documentazione: tutti i .md in docs/ elencati
- [ ] Quick Links invariati
- [ ] Nessuna sezione extra
- [ ] Footer con data e LLM

*Template v1.2 - .NET 10 - Token-optimized for AI agents* - Last Update 2026-03-17 21:28
