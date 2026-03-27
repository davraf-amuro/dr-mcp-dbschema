---
applyTo: "**"
---

# MCP Server Discovery & Creation

## Scopo

Questa istruzione si applica ogni volta che un task richiede l'uso di un **MCP server** (Model Context Protocol server) per accedere a strumenti, risorse o capacità esterne all'agente.

---

## Flusso obbligatorio

### 1. Cerca prima di creare

Prima di proporre la creazione di un nuovo MCP server, l'agente **deve cercarlo** in tutti i contesti accessibili, nell'ordine seguente:

1. **Settings locali del progetto corrente** — `.claude/settings.json` / `.claude/settings.local.json` (chiave `mcpServers`)
2. **Settings globali utente** — `~/.claude/settings.json` / `~/.claude/settings.local.json`
3. **Repository workspace** — altri progetti aperti nell'ambiente corrente (working directories elencate nell'environment)
4. **MCP servers già registrati nell'IDE** — configurazioni VS Code, JetBrains o altri ambienti attivi
5. **Registry pubblici noti** — se si ha accesso a internet, cerca nel [MCP Registry ufficiale](https://github.com/modelcontextprotocol/servers) o repository GitHub/npm correlati

Se il server viene trovato in uno qualsiasi di questi punti:
- **Proponi il server trovato** all'utente, indicando dove si trova e come configurarlo per il progetto corrente.
- Non procedere alla creazione.

---

### 2. Proponi la creazione se non trovato

Se la ricerca non produce risultati:
- **Comunica esplicitamente** che nessun MCP server adatto è stato trovato.
- **Proponi di crearne uno nuovo**, descrivendo:
  - Nome suggerito (es. `mcp-<dominio>`)
  - Strumenti/risorse che esporrebbe
  - Tecnologia consigliata (TypeScript SDK `@modelcontextprotocol/sdk` oppure Python SDK `mcp`)
- **Prima di procedere**, aspetta conferma dell'utente.

---

### 3. Ingaggia `/warroom` prima di progettare

Se l'utente approva la creazione, **è obbligatorio invocare la skill `/warroom`** con la domanda:

```
/warroom Come dovremmo progettare il server MCP "<nome>" per <obiettivo>?
  Considerare: strumenti esposti, autenticazione, trasporto (stdio/SSE/HTTP),
  naming conventions, struttura del repository.
```

Aspetta l'output del warroom prima di scrivere qualsiasi codice.

---

### 4. Proponi un repository Git dedicato

Dopo la creazione del server MCP (o alla sua approvazione finale):
- **Proponi di pubblicarlo in un repository Git dedicato**, separato dal progetto che lo consuma.
- Il repository deve seguire la convenzione di naming: `mcp-<dominio>` (es. `mcp-portainer`, `mcp-db-schema`).
- Suggerisci di aggiungere il repository come **submodule** nel progetto consumatore, se necessario.
- Segnala che il repository dovrebbe includere:
  - `README.md` con la guida all'installazione e configurazione
  - File di configurazione esempio per `.claude/settings.json`
  - Istruzioni di build/publish

---

## Regole di perimetro

- Non saltare la fase di ricerca nemmeno se il task sembra semplice o urgente.
- Non creare un MCP server inline nel progetto consumatore: i server MCP sono artefatti autonomi.
- Non proporre l'uso di un server trovato senza prima verificare che sia compatibile con il task corrente (strumenti esposti, versione del protocollo, sicurezza).
- Se il warroom produce indicazioni in conflitto tra loro, segnalale all'utente e chiedi una decisione prima di procedere.

---

## Comportamento di fallback

Se non è possibile accedere ad alcuna fonte di ricerca (es. nessun accesso internet, nessun settings file leggibile):
- Dichiara esplicitamente il limite.
- Proponi comunque la creazione, partendo dalla sessione warroom.

---

*Istruzione v1.0 - MCP Server Discovery - 2026-03-27 — claude-sonnet-4-6*
