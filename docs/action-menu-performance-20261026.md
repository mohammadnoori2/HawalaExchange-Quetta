# Operation menu response — 2026-10-26

The shared three-dot menu previously sent a Blazor event to the server, rendered server state, synchronized quick-action preferences through JS interop, and then requested browser positioning. Switching menus could also await the previous menu's server callback. Network latency therefore affected a purely visual interaction.

Opening, switching and closing menus now run synchronously in the browser. Escape restores focus, outside clicks close the menu, internal menu scrolling remains usable, and detached menus release their listeners. A delayed settings render only refreshes an already open menu; it cannot reopen a closed menu. The settings editor, permissions and business action handlers remain on the server. Existing styles, labels and stored quick-action preferences are retained.

`hawala-tools.js` now uses the static asset fingerprint in the application shell so that a new deployment loads its matching script.

## Controlled browser comparison

Microsoft Edge, disposable SQL Server fixture, simulated 150ms delay in each direction on the Blazor WebSocket (300ms round trip). Measurements run from click dispatch until the menu becomes visible; they do not measure loading a business form or executing its operation.

| Page | Previous first open | Updated first open |
| --- | ---: | ---: |
| Accounts | 962ms | 1.6ms |
| Customers (registered receiver tab in the seeded dataset) | 965ms | 1.6ms |
| Correspondents | 996ms | 1.3ms |

All nine updated openings across the three pages were synchronous (1.0–2.2ms). Settings opened and canceled successfully on all three pages under the same latency. A 375px mobile check confirmed the menu remains within the viewport. These are controlled local measurements, not measurements of the public Cloudflare hostname.

Validation also passed 11 component/connection lifecycle tests, three quick-action permission/preference tests and the standalone browser regression for keyboard access, outside close, single action dispatch, stalled previous callbacks, late server refresh, menu scrolling, detached elements and shortcuts.

All 30 existing navigation/payment/receipt regression tests also passed in Release. Debug verification encountered files locked by the user's running Visual Studio application; that application was left running and the checks were completed in Release instead.

Evidence: [previous timings](action-menu-20261026/before.json), [updated timings](action-menu-20261026/after.json), [mobile screenshot](action-menu-20261026/mobile.png).

## Delivery

Complete release output: `publish/action-menu-20261026`. It includes the earlier responsive and execution-receipt changes. IIS was not modified. Deploy the complete output while preserving production configuration, `App_Data` and uploads. No database migration is introduced by this change.
