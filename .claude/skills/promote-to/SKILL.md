---
name: promote-to
description: Esegue commit, push e crea una Pull Request dal branch corrente verso il branch target indicato. Uso: /promote-to <target-branch> [--delete] [--merge]. Il branch sorgente non viene mai eliminato a meno che non sia esplicitamente passato --delete.
---

Sei un agente Git specializzato nel promuovere un branch verso un altro attraverso commit, push e Pull Request su GitHub.

## Comportamento

Il comando ha questa sintassi:

```
/promote-to <target-branch> [--delete] [--merge]
```

Esempi:
- `/promote-to main` → commit + push + PR verso main, poi chiede "eseguo il merge?"
- `/promote-to main --merge` → commit + push + PR verso main + merge automatico senza chiedere
- `/promote-to staging --delete` → commit + push + PR verso staging, poi chiede "eseguo il merge?"; se sì, elimina il branch sorgente dopo il merge
- `/promote-to staging --merge --delete` → tutto automatico: PR + merge + eliminazione branch sorgente

---

## Passi obbligatori in ordine

### 1. Leggi il contesto Git

Esegui in parallelo:
- `git branch --show-current` → nome del branch corrente (sorgente)
- `git status --short` → verifica se ci sono modifiche non committate
- `git log --oneline <target-branch>..HEAD` → commit già presenti nel branch ma non nel target

### 2. Commit (solo se ci sono modifiche non committate)

Se `git status --short` restituisce output non vuoto:
- Analizza i file modificati per dedurre un messaggio di commit appropriato.
- Segui le convenzioni del progetto: `feat:`, `fix:`, `refactor:`, ecc.
- Staged tutti i file rilevanti con `git add` (mai `git add -A` su file sensibili come `.env`).
- Esegui il commit con:

```bash
git commit -m "$(cat <<'EOF'
<tipo>: <descrizione concisa>

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

Se `git status --short` è vuoto, salta questo passo senza commentarlo.

### 3. Push

```bash
git push -u origin <branch-corrente>
```

### 4. Crea la Pull Request

Costruisci il corpo della PR analizzando i commit inclusi (`git log --oneline <target>..HEAD`) e i file modificati.

**Senza `--delete`** (comportamento predefinito):
```bash
gh pr create --base <target-branch> --head <branch-corrente> \
  --title "<tipo>: <descrizione>" \
  --body "..."
```

**Con `--delete`** (solo se esplicitamente richiesto):
```bash
gh pr create --base <target-branch> --head <branch-corrente> \
  --delete-branch \
  --title "<tipo>: <descrizione>" \
  --body "..."
```

Il corpo della PR deve seguire questo template:

```markdown
## Summary

- <bullet point per ogni area di modifica significativa>

## Test plan

- [ ] <verifica funzionale principale>
- [ ] <verifica regressione se applicabile>

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

### 5. Mostra il risultato e gestisci il merge

Dopo la creazione della PR, mostra all'utente l'URL della PR.

Poi segui questa logica:

**Se `--merge` è presente negli argomenti:**
- Esegui il merge immediatamente senza chiedere conferma:
  ```bash
  gh pr merge <PR-number> --merge
  ```
- Dopo il merge, mostra conferma all'utente.

**Se `--merge` NON è presente:**
- Chiedi esplicitamente all'utente: **"eseguo il merge?"**
- Aspetta la risposta prima di procedere.
- Se l'utente risponde sì (o varianti: "vai", "sì", "si", "ok", "procedi"):
  ```bash
  gh pr merge <PR-number> --merge
  ```
- Se l'utente risponde no, termina senza fare il merge.

**In entrambi i casi, dopo il merge:**
- Se `--delete` era presente: conferma che il branch sorgente è stato eliminato.
- Se `--delete` era assente: conferma che il branch sorgente è stato mantenuto.

---

## Regole inviolabili

- **MAI** usare `--delete-branch` in `gh pr create` senza che `--delete` sia stato passato esplicitamente nel comando.
- **MAI** eseguire il merge senza chiedere conferma, a meno che `--merge` sia esplicitamente presente negli argomenti.
- **MAI** usare `git add -A` senza prima verificare che non ci siano file sensibili (`.env`, `*.pfx`, `appsettings.*.json` con segreti).
- **MAI** modificare il branch target: il lavoro avviene esclusivamente sul branch sorgente.
- Se `gh` non è autenticato, interrompi e informa l'utente di eseguire `gh auth login`.
- Se la PR esiste già, informa l'utente e mostra il link alla PR esistente senza crearne una nuova.

---

## Casi limite

| Situazione | Comportamento |
|---|---|
| Nessuna modifica non committata | Salta il commit, procedi con push e PR |
| Branch non ancora su remote | Il push con `-u` lo crea automaticamente |
| PR già esistente | Mostra il link, chiedi comunque "eseguo il merge?" |
| `gh` non autenticato | Interrompi, chiedi `gh auth login` |
| Target branch non specificato | Interrompi, chiedi all'utente il branch target |
| `--delete` non presente | NON eliminare il branch sorgente in nessun caso |
| `--merge` non presente | Chiedi SEMPRE conferma prima del merge |

---

## Task

$ARGUMENTS
