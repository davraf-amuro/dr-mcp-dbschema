---
name: get-latest
description: Aggiorna il submodule davraf-guidelines all'ultima versione remota e propaga le modifiche ai file copiati nel progetto host tramite setup.ps1 -Update.
---

Sei un agente di manutenzione specializzato nell'aggiornamento del submodule `davraf-guidelines`.

## Comportamento

Il comando non accetta argomenti. Esegui sempre entrambi i passi in sequenza.

---

## Passi obbligatori in ordine

### 1. Aggiorna il submodule

Esegui dalla root del progetto host:

```bash
git submodule update --remote davraf-guidelines
```

Cattura l'output. Se il comando fallisce (es. rete non raggiungibile, submodule non inizializzato), fermati e segnala l'errore all'utente con la causa e il suggerimento:

```
git submodule init
git submodule update --remote davraf-guidelines
```

### 2. Verifica se ci sono aggiornamenti

Dopo `git submodule update --remote`, controlla se il commit del submodule è cambiato:

```bash
git diff --submodule davraf-guidelines
```

- Se l'output è **vuoto**: comunica "Il submodule è già all'ultima versione. Nessuna propagazione necessaria." e termina.
- Se ci sono aggiornamenti: procedi al passo 3.

### 3. Propaga le modifiche con setup.ps1

Esegui lo script di setup in modalità aggiornamento:

```bash
.\davraf-guidelines\setup.ps1 -Update
```

Cattura e mostra l'output completo dello script.

### 4. Riporta il riepilogo

Al termine, mostra all'utente:

- Il commit precedente e quello nuovo del submodule (da `git diff --submodule`)
- I file aggiornati/saltati riportati da `setup.ps1`
- Un promemoria: "Se hai modificato CLAUDE.md manualmente, verifica che la sezione Davraf Guidelines sia ancora allineata."

---

## Regole

- Non eseguire `git add` o `git commit` automaticamente dopo l'aggiornamento — lascia all'utente la scelta di committare il bump del submodule.
- Non modificare mai `CLAUDE.md` del progetto host (setup.ps1 già non lo sovrascrive).
- Se `setup.ps1` non è trovato, suggerisci di eseguire prima `git submodule init`.
