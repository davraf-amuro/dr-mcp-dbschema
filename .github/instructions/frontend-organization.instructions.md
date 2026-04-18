---
applyTo: "**"
---

# Frontend Organization — Struttura Componenti e Cartelle

Scopo: regole per organizzare componenti, logica e risorse nei progetti frontend (Vue.js, WPF/MVVM). Complementa `code-organization.instructions.md` — le regole generali si applicano anche qui.

---

## Regola 1 — Un componente = un file

- Ogni componente, ViewModel, UserControl o composable ha il suo file dedicato.
- Nome file = nome componente, PascalCase.
- Mai raggruppare più componenti nello stesso file.

```
// ✅ corretto
OrderStatusBadge.vue
UserAvatarCard.vue
OrderDetailView.xaml
OrderDetailViewModel.cs

// ❌ sbagliato
OrderComponents.vue     // contiene Badge + Card + Table insieme
SharedViews.xaml        // contiene più UserControl non correlati
```

---

## Regola 2 — Asse principale: generico vs specifico

Il criterio per scegliere la cartella non è la dimensione del componente, ma la sua **dipendenza dal dominio**.

> **Generico**: non conosce concetti di dominio (ordini, utenti, fatture). Può essere usato in qualsiasi vista.
> **Specifico**: il suo senso esiste solo nel contesto di una vista o dominio preciso.

```
// ✅ generico — nessun concetto di dominio
<BaseButton />
<DataTable />
<LoadingSpinner />

// ✅ specifico — legato al dominio ordini
<OrderStatusBadge />       // "status" è un concetto del dominio ordini
<UserPermissionToggle />   // "permission" è un concetto del dominio utenti
```

Se un componente in `shared/` inizia a importare tipi o logica di un dominio → spostarlo in `[domain]/`.

---

## Regola 3 — Struttura cartelle Vue

```
src/
  components/
    shared/             ← generici, zero dipendenze di dominio
      BaseButton.vue
      DataTable.vue
      LoadingSpinner.vue
      ConfirmModal.vue
    orders/             ← specifici del dominio ordini
      OrderStatusBadge.vue
      OrderSummaryCard.vue
    users/              ← specifici del dominio utenti
      UserAvatarCard.vue
      UserPermissionToggle.vue
  views/                ← pagine di primo livello (una per route), non riusabili
    OrderListView.vue
    UserProfileView.vue
  composables/
    shared/             ← logica reattiva generica
      useDebounce.ts
      usePagination.ts
      useTheme.ts
    orders/             ← logica reattiva specifica del dominio
      useOrderFilters.ts
      useOrderExport.ts
    users/
      useUserPermissions.ts
  stores/               ← uno store Pinia per dominio
    orders.ts
    users.ts
    ui.ts               ← stato UI globale (sidebar, tema, notifiche)
  services/             ← chiamate API, una classe/funzione per risorsa
    ordersService.ts
    usersService.ts
  utils/                ← funzioni pure stateless, zero dipendenze framework
    formatDate.ts
    parseQueryString.ts
  assets/
  router/
    index.ts
    routes/             ← definizione route per dominio
      orders.ts
      users.ts
```

### Regola per `views/`

Le view non contengono logica di business. Orchestrano componenti e delegano a store/composable.

```vue
<!-- ✅ corretto: view come orchestratore -->
<script setup lang="ts">
import OrderStatusBadge from '@/components/orders/OrderStatusBadge.vue'
import DataTable from '@/components/shared/DataTable.vue'
import { useOrderFilters } from '@/composables/orders/useOrderFilters'

const { filters, applyFilter } = useOrderFilters()
</script>

<!-- ❌ sbagliato: logica di business nella view -->
<script setup lang="ts">
const orders = ref([])
onMounted(async () => {
  orders.value = await fetch('/api/orders').then(r => r.json()) // ← va in service/store
})
</script>
```

---

## Regola 4 — Struttura cartelle WPF (MVVM)

```
src/
  Views/
    Shared/             ← UserControl generici, zero dipendenze di dominio
      LoadingOverlay.xaml
      ConfirmDialog.xaml
      PaginationControl.xaml
    Orders/             ← View specifiche del dominio ordini
      OrderListView.xaml
      OrderDetailView.xaml
    Users/
      UserProfileView.xaml
  ViewModels/
    Shared/             ← base classes e helper VM
      ViewModelBase.cs
      RelayCommand.cs
      AsyncRelayCommand.cs
    Orders/
      OrderListViewModel.cs
      OrderDetailViewModel.cs
    Users/
      UserProfileViewModel.cs
  Controls/             ← UserControl riusabili (diversi da View: non hanno VM proprio)
    Shared/
      TagInput.xaml
      StatusIndicator.xaml
    Orders/
      OrderStatusBadge.xaml
  Converters/           ← sempre generici, nessuna dipendenza di dominio
    BoolToVisibilityConverter.cs
    NullToPlaceholderConverter.cs
    EnumToStringConverter.cs
  Behaviors/            ← sempre generici
    ScrollIntoViewBehavior.cs
    FocusOnLoadBehavior.cs
  Models/               ← DTO e model locali (non dipendono da ViewModel)
    OrderDto.cs
    UserDto.cs
  Services/             ← logica applicativa (navigation, dialog, data fetch)
    NavigationService.cs
    DialogService.cs
    OrderDataService.cs
```

### Regola MVVM

- **View**: zero logica. Solo binding, trigger, template. Nessun event handler con logica di business.
- **ViewModel**: zero riferimenti a controlli UI (`Button`, `TextBox`, ecc.). Espone solo proprietà e `ICommand`.
- **Dipendenza**: View dipende da ViewModel via DataContext. ViewModel non conosce View.

```csharp
// ✅ corretto: azione nel ViewModel come ICommand
public ICommand SaveOrderCommand => new RelayCommand(async () =>
{
    await _orderService.SaveAsync(CurrentOrder);
});

// ❌ sbagliato: logica di business nel code-behind
private async void SaveButton_Click(object sender, RoutedEventArgs e)
{
    await _orderService.SaveAsync(CurrentOrder); // ← va nel ViewModel
}
```

---

## Regola 5 — Pattern adatti

**Composable (Vue)** — logica reattiva riusabile
```
Problema: più componenti condividono la stessa logica di filtro/paginazione.
Soluzione: useX() in composables/shared/ o composables/[domain]/.
Mai duplicare logica reattiva tra componenti — estrarla in composable.
```

**MVVM (WPF)** — separazione View/ViewModel
```
Problema: logica di UI e logica applicativa mescolate nel code-behind.
Soluzione: ViewModel espone tutto via binding, View è dichiarativa.
```

**Store per dominio (Pinia)** — stato condiviso tra view
```
Problema: più view condividono lo stesso stato (es. lista ordini, utente corrente).
Soluzione: uno store per dominio. No store monolitico globale con tutto.
```

**Provide/Inject (Vue)** — dipendenze cross-componente
```
Problema: prop drilling su molti livelli di componenti.
Soluzione: provide() nel parent, inject() nel descendant.
Usare solo per dati stabili (configurazione, servizi) — non per stato mutabile frequentemente.
```

**Command (WPF)** — azioni utente
```
Problema: ogni click/interazione deve essere testabile e separata dalla View.
Soluzione: ICommand (RelayCommand/AsyncRelayCommand) nel ViewModel.
Regola: zero logica nei gestori Click/KeyDown del code-behind.
```

---

## Regola 6 — Interfaccia esplicita dei componenti

### Vue
```typescript
// ✅ corretto: props tipizzate esplicitamente
const props = defineProps<{
  orderId: number
  status: 'pending' | 'confirmed' | 'shipped'
  onStatusChange?: (newStatus: string) => void
}>()

// ❌ sbagliato
const props = defineProps(['orderId', 'status']) // no tipi
```

### WPF (UserControl generico)
```csharp
// ✅ corretto: DependencyProperty per ogni input pubblico
public static readonly DependencyProperty StatusProperty =
    DependencyProperty.Register(nameof(Status), typeof(string), typeof(StatusIndicator));

public string Status
{
    get => (string)GetValue(StatusProperty);
    set => SetValue(StatusProperty, value);
}

// ❌ sbagliato: proprietà CLR normale in un UserControl (non supporta binding)
public string Status { get; set; }
```

---

## ✅ Checklist pre-commit

- [ ] Ogni componente/ViewModel/composable ha il suo file dedicato?
- [ ] Il file è in `shared/` o `[domain]/` in base alla dipendenza dal dominio?
- [ ] Un file in `shared/` non importa tipi o logica di un dominio specifico?
- [ ] Logica reattiva riusabile estratta in composable (Vue) o servizio (WPF)?
- [ ] Uno store Pinia per dominio — nessun store monolitico?
- [ ] Nessuna logica di business nel code-behind XAML o nella `<script>` di una view?
- [ ] Props Vue tipizzate esplicitamente? UserControl WPF usa DependencyProperty?

---

*Istruzione v1.0 - Frontend Organization (Vue + WPF) - 2026-04-16 — claude-sonnet-4-6*
