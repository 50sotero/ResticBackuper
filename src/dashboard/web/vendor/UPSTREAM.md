# Beautiful UI upstream provenance

These components are copied from the Beautiful UI repository:

- Source: https://github.com/slev12397/beautiful-ui
- Pinned commit: `ff0f74d62d8be9d89bcb735b3632e31a6ccf88dc`
- Reference site: https://www.beautifului.dev/
- License: [BEAUTIFULUI-MIT-LICENSE.txt](./BEAUTIFULUI-MIT-LICENSE.txt)

Copied source files:

- `components/atoms/Button.tsx`
- `components/atoms/Shimmer.tsx`
- `components/atoms/EntityChip.tsx`
- `components/atoms/ValuePill.tsx`
- `components/primitives/GlideMenu.tsx`
- `components/primitives/SidebarNav.tsx`
- `components/primitives/TaskRows.tsx`
- `components/primitives/FilterTable.tsx`
- `components/primitives/LoadingState.tsx`
- `components/primitives/InsightCards.tsx`
- `components/primitives/ContextCards.tsx`
- `lib/utils.ts`
- `app/globals.css` as `styles/beautifului.css`

The DOM structure, Tailwind classes, keyframes, transitions, stagger delays, pixel loader, gliding menu, expandable rows, and filter collapse animation remain from the pinned upstream source. Adaptations are limited to the integration seams:

- `SidebarNav` accepts Restic workspace, navigation, recent-history, workspace-settings, and primary-action data/callbacks and help text; its paid icon imports and demo workspace defaults are replaced with `lucide-react` aliases and Restic-safe defaults. Navigation exposes accessible names/current-page state, Settings remains available when collapsed, and the workspace menu supports keyboard navigation and focus return.
- `TaskRows` accepts real `pending`, `failed`, and `cancelled` statuses and a neutral numbered `guide` mode; only the upstream `sequence` status uses its timer. Collapsed details are inert and hidden from assistive technology.
- `FilterTable` accepts row IDs, mapped and per-row result labels, computed counts, selected rows, row callbacks, controlled filters, an empty state, sort controls, and a class name. Collapsed rows are inert and hidden from assistive technology; selectable rows expose their pressed state and support Enter to open details and Space to select.
- `ContextCards` accepts stable chunk IDs and trailing actions.
- `InsightCards` accepts an action callback and disabled state, and keeps the upstream page change motion with a keyed `fade-up`.
- `LoadingState` exports its existing prop shape as an interface for integration, hides the rapidly updating decorative timer from assistive technology, and has no default external demo-video URL. The app uses its original pixel and orbit loaders.
