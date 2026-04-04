---
applyTo: "README.md"
---

# Struttura README — davraf-guidelines

Questo file definisce la struttura obbligatoria del `README.md` di questo repository.
Quando crei o aggiorni il README, rispetta esattamente questa struttura. Non aggiungere sezioni non previste. Non rimuovere sezioni esistenti.

---

## Struttura obbligatoria

Il README deve contenere queste sezioni, in questo ordine:

| # | Sezione | Emoji | Scopo |
|---|---------|-------|-------|
| 1 | Titolo + tagline | — | Nome repo + una riga che spiega cos'è |
| 2 | Avvio Rapido — Nuovo Progetto | 🚀 | Come usarlo su un progetto nuovo |
| 3 | Progetto Esistente — Aggiungere le Guidelines | 🔧 | Come aggiungerlo a un progetto già esistente |
| 4 | Cosa viene configurato | 📦 | Tabella dei file installati da `setup.ps1` |
| 5 | Aggiornare le Guidelines | 🔄 | Come aggiornare il submodule e i file copiati |
| 6 | Istruzioni Modulari (Copilot / Claude) | 🤖 | Tabella dei file `.instructions.md` presenti |
| 7 | Claude Code Skills | 🤖 | Una voce per ogni skill in `.claude/skills/` |
| 8 | MCP Servers | 🔌 | Una voce per ogni server in `.mcp.json` |
| 9 | FAQ | ❓ | Domande frequenti in formato Q/A |
| 10 | Footer | — | `*Documento aggiornato: Mese Anno*` |

---

## Regole per sezione

### 1 — Titolo + tagline
- H1 con il nome del repository
- Una sola riga di descrizione, concreta e diretta

### 2 — Avvio Rapido (nuovo progetto)
- Mostra il comando PowerShell `irm ... | iex` per eseguire `CreateNewSolution.ps1`
- Elenca i passi che lo script esegue (lista numerata, breve)
- **Non modificare il comando PowerShell** senza verificare che l'URL sia ancora valido

### 3 — Progetto Esistente
- Due comandi PowerShell: `git submodule add` + esecuzione di `setup.ps1`
- Una nota su cosa fa `setup.ps1` sui file già presenti (comportamento non distruttivo)

### 4 — Cosa viene configurato
- Tabella con colonne: `File/Cartella | Provenienza | Scopo`
- Aggiorna la tabella se `setup.ps1` cambia i file che installa

### 5 — Aggiornare le Guidelines
- Comando `git submodule update --remote davraf-guidelines`
- Nota su quali file si aggiornano automaticamente (junction `.github/`) e quali no (file copiati)

### 6 — Istruzioni Modulari
- Tabella con colonne: `File | Quando usarlo`
- Una riga per ogni file `.instructions.md` presente in `.github/instructions/`
- Aggiorna la tabella quando aggiungi o rimuovi file instruction

### 7 — Claude Code Skills
- Una voce H3 per ogni file in `.claude/skills/`
- Per ogni skill: descrizione breve, comando PowerShell per il setup globale (se serve), esempio d'uso
- Aggiorna questa sezione quando aggiungi o rimuovi skill

### 8 — MCP Servers
- Una voce H3 per ogni server definito in `.mcp.json`
- Per ogni server: prerequisito di installazione + snippet `.mcp.json` da copiare
- Chiudi sempre con: `Poi riavvia Claude Code per caricare il server.`

### 9 — FAQ
- Formato: `### Q: domanda` / `**A:** risposta`
- Aggiungi una voce quando emerge una domanda ricorrente
- Non rimuovere voci esistenti senza motivo esplicito

### 10 — Footer
- Formato esatto: `*Documento aggiornato: Mese Anno*`
- Aggiorna mese e anno ad ogni modifica significativa

---

## Quando aggiornare il README

| Evento | Cosa aggiornare |
|--------|-----------------|
| Nuova skill aggiunta in `.claude/skills/` | Sezione "Claude Code Skills" |
| Nuovo file instruction in `.github/instructions/` | Sezione "Istruzioni Modulari" |
| Nuovo MCP server in `.mcp.json` | Sezione "MCP Servers" |
| `setup.ps1` installa nuovi file | Tabella "Cosa viene configurato" |
| Nuova domanda frequente | Sezione FAQ |

*Template v1.0 - davraf-guidelines - Last Update 2026-03-17 21:28*
