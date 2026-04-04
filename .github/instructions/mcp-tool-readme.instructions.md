---
applyTo: "tools/**/README.md"
---

# Istruzioni — README dei Tool MCP

Ogni tool MCP nella cartella `tools/` ha un proprio repository Git e un `README.md` che segue questa struttura canonica.

## Struttura obbligatoria

```markdown
# nome-tool

Breve descrizione in una riga: cosa fa e per chi è pensato.

## Tool disponibili

| # | Tool | Descrizione |
|---|------|-------------|
| 1 | `nome_tool` | Descrizione breve |

## Configurazione

### Variabili d'ambiente

| Variabile | Obbligatoria | Descrizione |
|---|:---:|---|
| `VAR_OBBLIGATORIA` | ✅ | Descrizione |
| `VAR_OPZIONALE` | — | Descrizione (default: valore) |

### Come ottenere le credenziali   ← solo se necessario

Passi per generare token/API key.

## Aggiungere al progetto

git clone ... tools/nome-tool
(cd + npm install  ← solo per tool Node.js)

Snippet .mcp.json con placeholder.

"Poi riavvia Claude Code per caricare il server."

## Esempi d'uso

Un esempio per ogni tool, nel formato:
  nome_tool → param1: valore, param2: valore

## Aggiornamento   ← solo se pertinente

## Requisiti
```

## Regole

### Tabella Tool disponibili
- Sempre con colonna `#` (numerazione progressiva)
- Nomi tool in backtick

### Tabella Variabili d'ambiente
- `✅` per obbligatoria, `—` per opzionale
- Indicare sempre il default per le opzionali
- Sezione `### Come ottenere le credenziali` solo se la generazione del token non è ovvia

### Sezione "Aggiungere al progetto"
- **Node.js**: `git clone` + `cd` + `npm install`
- **dotnet**: solo `git clone` (il build avviene automaticamente al primo avvio)
- Snippet `.mcp.json` con valori placeholder (mai valori reali)
- Chiudere sempre con: `Poi riavvia Claude Code per caricare il server.`

### Sezione "Esempi d'uso"
- Un sottotitolo `###` per ogni tool esposto
- Formato: blocco di codice con `tool → param: valore`

### Lingua
- Tutto in italiano

### Cosa NON includere
- Date o versioni nel testo
- Sezioni vuote o placeholder non compilati
- Dettagli implementativi interni
