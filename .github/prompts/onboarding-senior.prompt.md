---
agent: 'agent'
description: 'Genera il documento di onboarding per un programmatore senior che entra nel progetto'
tools: ['search/codebase', 'edit/editFiles']
---

# Template: Onboarding Senior Developer

Genera `docs/onboarding.md` per un programmatore senior che entra nel progetto.
Un senior vuole capire scelte architetturali, vincoli e dove trovare le cose — non tutorial elementari.

## Prima di scrivere

1. Leggi i file `.github/instructions/*.instructions.md` pertinenti al progetto
2. Analizza la struttura del repository: cartelle, file chiave, entry point
3. Leggi `Program.cs` o l'entry point principale
4. Identifica stack, dipendenze principali e pattern usati

## Struttura del documento

Crea `docs/onboarding.md` con questa struttura:

### 1. Il progetto in tre righe
Cosa fa, per chi, in quale contesto. Niente storia, solo fatti.

### 2. Stack e scelte tecniche
Tabella con: tecnologia, versione, motivo della scelta (o "standard di progetto").
Includi solo ciò che è rilevante per chi scrive codice.

### 3. Come avviare il progetto
Passi minimi per avere il progetto in esecuzione in locale.
Prerequisiti, configurazione, comando di avvio. Niente ovvietà.

### 4. Struttura del codice
Mappa delle cartelle principali con una riga di descrizione per ognuna.
Evidenzia dove vivono le cose che tocca più spesso: endpoint, modelli, configurazione.

### 5. Convenzioni obbligatorie
Le regole che non si discutono in questo progetto.
Ricavale dai file `.instructions.md`. Solo quelle che impattano il lavoro quotidiano.

### 6. Flusso di lavoro
Come si lavora su questo progetto: branch, commit, PR, deploy.
Se ci sono automazioni (CI/CD, script), citale con il comando esatto.

### 7. Dati sensibili e configurazione locale
Come gestire credenziali e file locali. Punta a `sensitive-data.instructions.md`.

### 8. Dove chiedere / cosa leggere dopo
Link ai file di riferimento più importanti nel repository.

## Regole

- Non inventare nulla: ogni affermazione deve essere verificabile nel codice o nei file di configurazione
- Se un'informazione non è ricavabile, scrivi `Da verificare con il team`
- Niente tutorial: un senior sa già come funziona .NET, spiega solo le specificità di *questo* progetto
- Footer: `*Revisione v1.0 — {YYYY-MM-DD HH:MM} — {modello-llm}*`
