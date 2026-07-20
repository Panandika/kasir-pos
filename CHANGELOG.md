# Changelog

## [2.7.0](https://github.com/Panandika/kasir-pos/compare/v2.6.1...v2.7.0) (2026-07-20)


### Features

* **pos:** persist in-progress cart for crash recovery (F36) ([6d61486](https://github.com/Panandika/kasir-pos/commit/6d614863e67ef992d28501b70117ee38658e790b))


### Bug Fixes

* **accounting:** block period close on unposted purchase returns (F18, F39) ([c875588](https://github.com/Panandika/kasir-pos/commit/c875588832377243a7d1cde4ee6ce9164fab0015))
* **accounting:** fall back to cash debit when tender data doesn't reconcile (review) ([63cb19a](https://github.com/Panandika/kasir-pos/commit/63cb19aa8f774bc1defa66b24a9995d7f830ff18))
* **accounting:** tender-split GL posting + weighted-avg COGS + fail-closed accounts (F16, F20, F40) ([80eb151](https://github.com/Panandika/kasir-pos/commit/80eb151eaf26ef591856bd45f1afbbee4fe13a07))
* **accounting:** tender-split GL posting, weighted-avg COGS, fail-closed accounts (F16, F20, F40) ([3fbd71d](https://github.com/Panandika/kasir-pos/commit/3fbd71d6210b22ef4d6cdd9dd5903ab8336a3eae))
* **accounting:** use actual register id in journal numbering, not hardcoded 01 (F37) ([b93d840](https://github.com/Panandika/kasir-pos/commit/b93d8408bc20f78d070c811a967c52e1ac502408))
* **auth:** persist + escalate + monotonic-guard login lockout (F34) ([733187f](https://github.com/Panandika/kasir-pos/commit/733187fa80ab951b6f650682bdb663f6189a702c))
* DEEP-CODE-ANALYSIS remediation (F01–F52) ([6b91a76](https://github.com/Panandika/kasir-pos/commit/6b91a7660e4573821bce1f8e0ef5cb240e29d79e))
* **inventory:** wrap stock-opname document + movements in a transaction (F21) ([0135274](https://github.com/Panandika/kasir-pos/commit/01352744edf538e679e299fabad0a950955e9a0f))
* **pos:** allocate sale journal number inside the sale transaction (F52) ([9edb23a](https://github.com/Panandika/kasir-pos/commit/9edb23a382ae48e63fa3f9519896a9301555b232))
* **pos:** only give change from cash, reject card/voucher overpayment (F38) ([fbe683d](https://github.com/Panandika/kasir-pos/commit/fbe683dafb6e71041accbaf96fd8cd0e0da8433e))
* **pos:** scope shift cash expectation to the shift time window (F03) ([8f1e75e](https://github.com/Panandika/kasir-pos/commit/8f1e75ee59715d8b38096c52ea078fbe54910025))
* **pos:** stamp sales.card_type from card master instead of hardcoded 'C' ([#53](https://github.com/Panandika/kasir-pos/issues/53)) ([50bf697](https://github.com/Panandika/kasir-pos/commit/50bf69796e3cc41f66d1d7651444318c0654d9fd))
* **pos:** void sale returns stock and blocks voiding posted sales (F13, F35) ([b989372](https://github.com/Panandika/kasir-pos/commit/b9893725d6b648745a8ada68add8ad70d2c5deaf))
* **purchasing:** wrap goods-receipt/invoice/return writes in a transaction (F19) ([5925027](https://github.com/Panandika/kasir-pos/commit/59250275e4cdfa2c9962810e0a6e2b41fdffb369))
* **sync:** bundle child detail rows (sale_items etc.) with their parent (F25) ([fa46792](https://github.com/Panandika/kasir-pos/commit/fa46792ef550eca911fc15763e6e8f3ff48285b2))
* **sync:** key transaction tables on journal_no; mark unresolved events failed (F04, F24) ([a8e8440](https://github.com/Panandika/kasir-pos/commit/a8e8440a912b946e9af19551034bb963e28bccb3))
* **sync:** map transaction tables to journal_no key column (F04) ([2383d1e](https://github.com/Panandika/kasir-pos/commit/2383d1e1bfe33e74f6bb44035192c5ee0896a0e1))
* **sync:** quarantine poison inbox files so they can't starve the pull window (F44) ([8eb9267](https://github.com/Panandika/kasir-pos/commit/8eb9267f388ee6937a025c525c09644e216b3315))
* **sync:** retry failed queue rows under a cap instead of stranding them (F05, F14) ([9ee70d9](https://github.com/Panandika/kasir-pos/commit/9ee70d96ec09ab48f4a5254388bfd69e362c29af))
* **sync:** transaction tables use journal_no key column (F04) ([a656e51](https://github.com/Panandika/kasir-pos/commit/a656e51e1c4e95384de1e4d528cd849c7accc3ad))
* **update:** enforce manifest completeness to block planted-file RCE (F23) ([84361fe](https://github.com/Panandika/kasir-pos/commit/84361feefb236db00de0cbd5c1c89c3a00eb80df))
* **update:** verify update packages with a dedicated key, not the sync key (F42) ([cc98a8b](https://github.com/Panandika/kasir-pos/commit/cc98a8b0943b34c731a97ea4b5c9fa03f647dcde))

## [2.6.0](https://github.com/Panandika/kasir-pos/compare/v2.5.1...v2.6.0) (2026-05-17)


### Features

* Cloud Import via Pairing Code (POS side) ([e89ab0a](https://github.com/Panandika/kasir-pos/commit/e89ab0a73bce16288eb9fc1732e7b35840e1a907))
* **cloud-import:** BootstrapTokenClient + CloudSnapshotRestorer (POS side) ([e030fce](https://github.com/Panandika/kasir-pos/commit/e030fcec91ea94006857087cf6ee927f16d9f2f0))
* **cloud-import:** CI gates + GHA fallback workflow ([60f1329](https://github.com/Panandika/kasir-pos/commit/60f1329809cbd75959e1d41b25b2b2b7630dd7e5))
* **cloud-import:** CloudImportView + FirstRunView 4th button ([38da841](https://github.com/Panandika/kasir-pos/commit/38da841cbf3c2f0a8c5059274d272152033affb8))
* **cloud-import:** SnapshotBuilder + ReverseRowMapper (hub side) ([acaae25](https://github.com/Panandika/kasir-pos/commit/acaae25f5aa0240afbe0cfca49e99834f64321d5))

## [2.5.1](https://github.com/Panandika/kasir-pos/compare/v2.5.0...v2.5.1) (2026-05-16)


### Bug Fixes

* **shift:** correct expected drawer cash reconciliation ([dc98084](https://github.com/Panandika/kasir-pos/commit/dc980848ab66f91e7de9d55b161ff0776cb7f81b))
* **shift:** tighten transaction isolation and date boundaries ([3e1e467](https://github.com/Panandika/kasir-pos/commit/3e1e467b34c9d9d464bf69bd47027dd5d511217e))

## [2.5.0](https://github.com/Panandika/kasir-pos/compare/v2.4.0...v2.5.0) (2026-05-11)


### Features

* **chrome:** cloud sync setup screen + creds service + dev README ([5960c94](https://github.com/Panandika/kasir-pos/commit/5960c949843256bc0f31268511bebaf0d53c9806))
* **chrome:** footer status badges — printer + cloud + version ([cee97cf](https://github.com/Panandika/kasir-pos/commit/cee97cfbc2635382b5973ec493f97eaac602bde9))
* **chrome:** footer status badges — printer + cloud sync + version ([3951bcd](https://github.com/Panandika/kasir-pos/commit/3951bcd8d9ab8568392697d5582a5a3413db3d0c))
* **help:** add Ctrl+/ Bantuan hint to status bar (G4) ([3184210](https://github.com/Panandika/kasir-pos/commit/3184210c64047919d38555376bb838547fe8b1a7))
* **help:** Bantuan glass strip overlay + 15 states (Phases 6-8) ([5f825bb](https://github.com/Panandika/kasir-pos/commit/5f825bb7b781b498213534cf6ae184190240d09f))
* **help:** Bantuan inline-glass help assistant (15 states) ([49c51e8](https://github.com/Panandika/kasir-pos/commit/49c51e88f8bc741fc44d52218ce2289f080319b8))
* **help:** Bantuan schema + repos + plan (Phase 1) ([81fdc95](https://github.com/Panandika/kasir-pos/commit/81fdc9525f4854310255e72c66c5b3dae9084811))
* **help:** DocIngester + HelpIngest CLI + starter FAQ (Phase 2) ([1d28ac6](https://github.com/Panandika/kasir-pos/commit/1d28ac6b5e9d371b5489e4a7218182fd53e1e6de))
* **help:** retrieval + PII scrub + ticket numbering + service (Phase 5) ([56678bc](https://github.com/Panandika/kasir-pos/commit/56678bc076e946bec2306d5367648a0e716ece3e))
* **help:** standalone sync drainer + Edge Function client (Phase 4) ([fbca82a](https://github.com/Panandika/kasir-pos/commit/fbca82a071d7d515c8cdaaed8f268e0629aa6910))
* **help:** Supabase schema + Edge Functions (Phase 3) ([81bafad](https://github.com/Panandika/kasir-pos/commit/81bafada88c1e9b5d102ebdd5aaf64b8bc030e5f))
* **help:** SupabaseMachineAuth + wire HttpHelpAskClient + auto-start sync ([6b40b1f](https://github.com/Panandika/kasir-pos/commit/6b40b1f5946e7ef4cbccaca3766f72bc076fd1a3))
* legacy gap closure (sisyphus plan) — POS density, purchase invoice fields, theme polish ([da73d1c](https://github.com/Panandika/kasir-pos/commit/da73d1c9bdabd5368ffe69a48d6129020e8c1140))
* **migration:** add Migration_004 for purchases.terms and received_date ([43bf2dd](https://github.com/Panandika/kasir-pos/commit/43bf2dd492c4f62c9e38572418446cfb0dfc8447))
* **pos:** add kembalian banner, + quick-cash, numeric input behavior ([499e365](https://github.com/Panandika/kasir-pos/commit/499e365d5a9c2a8544401b573dab61c3adbf0817))
* **pos:** improve SaleView density, add Bank tile, add stock columns ([033d389](https://github.com/Panandika/kasir-pos/commit/033d389610e58159c7970b574a73ca6c41c11709))
* **pos:** P5b SaleView SUBTOTAL via tokens + footer Auto rows ([6872029](https://github.com/Panandika/kasir-pos/commit/6872029f1164c8fa256aef8bab5a2bc5fe7a257c))
* **purchase:** add missing invoice fields, negative stock colors, search density ([1d91cb0](https://github.com/Panandika/kasir-pos/commit/1d91cb08dbff8bdebc508bfb9d45b659a8c262cb))
* **release:** matrix-build 3 per-register ZIPs with baked help.json ([95de7ed](https://github.com/Panandika/kasir-pos/commit/95de7edc28d6e2c2ca5f74ede0b9532326bb350b))
* **release:** matrix-build 3 per-register ZIPs with baked help.json ([cfd147c](https://github.com/Panandika/kasir-pos/commit/cfd147cbf0fcccd22fb34842518a8edd9d4633f3))
* **schema,master:** drop barcode, single-screen ProductView, F8 wholesale tiers ([360c5bd](https://github.com/Panandika/kasir-pos/commit/360c5bdd48123e212102759bfb0a19156ef4be2b))
* **theme:** Design System v2 — modern retail-POS migration ([1d95c69](https://github.com/Panandika/kasir-pos/commit/1d95c698353b7ad14c0c35b92bb59bc36537c8b9))
* **theme:** improve DataGrid terminal density and POS footer ([839df4b](https://github.com/Panandika/kasir-pos/commit/839df4b3c008e52792831aa340948b32a00b1f89))
* **theme:** P1 dual-variant tokens — 20 colors x Dark/Light + density/radius/font tokens ([6bc20f6](https://github.com/Panandika/kasir-pos/commit/6bc20f6e524e091f5b86816a58dd0443dff576d8))
* **theme:** P2 bundle JetBrains Mono TTF as AvaloniaResource ([5d656a2](https://github.com/Panandika/kasir-pos/commit/5d656a258e49f1c38bd2533f32394e01f6f4399c))
* **theme:** P3 ThemeService + Ctrl+Shift+L toggle + persistence ([0016389](https://github.com/Panandika/kasir-pos/commit/00163897c697df67862c64342ef5a39964be8eeb))
* **theme:** P4 Lucide.Avalonia icons — theme toggle + main menu tiles ([df00f07](https://github.com/Panandika/kasir-pos/commit/df00f07d668dfcf49ba3c01980f3831b813c44c6))
* **theme:** P5c apply Classes="compact" to Reports/Master/Inventory grids ([3fa0824](https://github.com/Panandika/kasir-pos/commit/3fa08241adb2e50453b191c66dccbe709ff7b7f8))
* **theme:** P6 sync status badge + hint bar in ShellWindow ([18e73da](https://github.com/Panandika/kasir-pos/commit/18e73dabd1fd027c3cd1fd7f3546b7544a877cb7))
* **ui:** global footer status helper with auto-revert ([60bbd60](https://github.com/Panandika/kasir-pos/commit/60bbd6000fc68c128ad6ecf1eef82d4c02a85309))
* **ui:** show update badge and footer toast on main menu ([36fd68f](https://github.com/Panandika/kasir-pos/commit/36fd68f77c61e7391932aad718f9d4e94180db74))
* user_review[#1](https://github.com/Panandika/kasir-pos/issues/1) — POS payment flow, footer hints, ProductView redesign ([9c7b74e](https://github.com/Panandika/kasir-pos/commit/9c7b74eefd373210bf96ee48267abc2b654ad164))


### Bug Fixes

* **chrome:** PrinterStatusModel use CreateConnection (background-thread-safe) ([c00f033](https://github.com/Panandika/kasir-pos/commit/c00f03389a475d9c00d0b9aa708fb842eaac5755))
* **cloudsync:** align cloud schema with main (drop barcode + product_barcodes, update purchases cols) ([ad49c1d](https://github.com/Panandika/kasir-pos/commit/ad49c1de869fadedd561731420ed6da69703e7cd))
* **master:** use location codes T/G not display names in ProductView stok grid ([6ccfee0](https://github.com/Panandika/kasir-pos/commit/6ccfee0db5e19533a1e62563b7a263dd781328f6))
* **money:** accept id-ID rupiah format (100.000) in currency inputs ([fb0359b](https://github.com/Panandika/kasir-pos/commit/fb0359bcc03bfe65b51534f2810a752e16f68b45))
* **pos:** address Slice 1 review findings ([2609388](https://github.com/Panandika/kasir-pos/commit/26093886780148b697bc9f8701a49b73e6d5dbbc))
* **purchase:** widen UnitPrice cast to long, set GrossAmount in Save() ([f7dad82](https://github.com/Panandika/kasir-pos/commit/f7dad82041c430feabcd67fba4f0c7bde9036801))
* **purchasing:** auto-size header borders to prevent DataGrid overlap ([614ca0f](https://github.com/Panandika/kasir-pos/commit/614ca0f395c16c0bb22494e02c81b824b2f5197e))
* **theme:** remove redundant ShellWindow hint bar (forms own their own) ([2adbc09](https://github.com/Panandika/kasir-pos/commit/2adbc09c6ad170e53c8a729822ae6e309b83b7d7))
* **theme:** ShellWindow Background uses Bg0Brush (was hardcoded [#000000](https://github.com/Panandika/kasir-pos/issues/000000)) ([7b74d4e](https://github.com/Panandika/kasir-pos/commit/7b74d4e4e2d1df0f82dda63d3cd194b38947461d))
* **theme:** use variant-aware ThemeResources for FindResource calls ([e61aa19](https://github.com/Panandika/kasir-pos/commit/e61aa19e095d7c6bb05ee1624902f1daa6f91256))
* **ui:** InputDialog full-screen scrim + add macOS .app bundle script ([c10d4ee](https://github.com/Panandika/kasir-pos/commit/c10d4eed87f78b80efd73106d6efce1a6c6617c1))
* **ui:** InputDialog title + autofocus + live rupiah formatting; add app icon ([ecfaa1d](https://github.com/Panandika/kasir-pos/commit/ecfaa1d409540d331f3ecc902ae6054f74783bd1))
* **ui:** use registered default in SaleView warning, remove dead field ([1d5c0c7](https://github.com/Panandika/kasir-pos/commit/1d5c0c789a7f89eb88c0b3a78dcef6016013c860))


### Refactoring

* **theme:** P5a migrate hardcoded brushes to DynamicResource ([78f0823](https://github.com/Panandika/kasir-pos/commit/78f082371bd6ff366c2c8b1730810b8447cab6d7))
* **ui:** convert MsgBox/Calculator/Payment/Wholesale windows to overlays ([8b9c20f](https://github.com/Panandika/kasir-pos/commit/8b9c20f73b2be8fef8662f53faeee9e86b863963))
* **ui:** InputDialog to in-window overlay (no separate OS window) ([7b6725e](https://github.com/Panandika/kasir-pos/commit/7b6725e451f9e42999235b30dab01233e123d343))

## [2.4.0](https://github.com/Panandika/kasir-pos/compare/v2.3.0...v2.4.0) (2026-04-25)


### Features

* **printer:** improve NullRawPrinter error message with config hint ([fd86933](https://github.com/Panandika/kasir-pos/commit/fd8693326f01f57fe0b56fb4d1e7e6f673a2547d))
* **printer:** improve NullRawPrinter error message with config hint ([5479f2f](https://github.com/Panandika/kasir-pos/commit/5479f2f62ea87fca4d60299bba183134f3747f94))

## [2.3.0](https://github.com/Panandika/kasir-pos/compare/v2.2.3...v2.3.0) (2026-04-25)


### Features

* **printer:** add picker UI, Windows spooler driver, and error surfacing ([763eeae](https://github.com/Panandika/kasir-pos/commit/763eeae7711be6b01639f653c011efc249cb1ac1))
* **printer:** picker UI, Windows spooler driver, error surfacing ([d0786b8](https://github.com/Panandika/kasir-pos/commit/d0786b8afbebfb820a6d4b14e272925a044c6fd7))

## [2.2.3](https://github.com/Panandika/kasir-pos/compare/v2.2.2...v2.2.3) (2026-04-18)


### Bug Fixes

* **release:** use PAT so release events trigger downstream workflows ([65eaa18](https://github.com/Panandika/kasir-pos/commit/65eaa180792c617d066453aefa603d8e3a3a17d1))

## [2.2.2](https://github.com/Panandika/kasir-pos/compare/v2.2.1...v2.2.2) (2026-04-18)


### Bug Fixes

* **release:** add workflow_dispatch for manual triggering ([5fe511d](https://github.com/Panandika/kasir-pos/commit/5fe511dab75935b157228df6c3c3ed895c00efb4))
* **release:** pass tag_name explicitly so workflow_dispatch can upload assets ([818ba0d](https://github.com/Panandika/kasir-pos/commit/818ba0dd9db3de635032f8f49460fe2fd21c4b40))

## [2.2.1](https://github.com/Panandika/kasir-pos/compare/v2.2.0...v2.2.1) (2026-04-18)


### Bug Fixes

* **release:** trigger on release published event, not tag push ([88dcae1](https://github.com/Panandika/kasir-pos/commit/88dcae1e0db9e21e57b0d4742779d9db47c53887))

## [2.2.0](https://github.com/Panandika/kasir-pos/compare/v2.1.0...v2.2.0) (2026-04-18)


### Features

* compact header, responsive bento tiles, shared CurrentSession ([e2c4f35](https://github.com/Panandika/kasir-pos/commit/e2c4f35df0e4a31af16cc770cc4302cb6cfd9d6b))
* drill-down bento + tunneled shortcuts + responsive shell + macOS DevTools skip ([16a5716](https://github.com/Panandika/kasir-pos/commit/16a5716328f83376bb3bda46a355deacb9345e39))
* **infra:** add ViewShortcuts helper for grid Enter interception and auto-focus ([f377de3](https://github.com/Panandika/kasir-pos/commit/f377de3931c971f1a575c61e4f3aaf98120a4898))
* **pos:** add Barang Tanpa Kode misc-item flow ([73350b3](https://github.com/Panandika/kasir-pos/commit/73350b32d7c4836c8ef7b8904cda2570f94a26d3))
* **pos:** Penjualan UX overhaul and deterministic navigation ([74f8ca2](https://github.com/Panandika/kasir-pos/commit/74f8ca2a3d79129a6e3b46300dce0b9ab6fb9762))


### Bug Fixes

* **accounting:** apply live search, auto-focus, and Enter-edit to Accounting views ([4f1cd73](https://github.com/Panandika/kasir-pos/commit/4f1cd73d3dff63aeaa5d61ff3a6b4f136112b407))
* add SQLitePCLRaw.bundle_e_sqlite3 for native DLL ([c8cd41e](https://github.com/Panandika/kasir-pos/commit/c8cd41e2f1346fc2c0e9da49069529b899be2065))
* add System.Data.SQLite.Core for native e_sqlite3.dll in test output ([ac391cc](https://github.com/Panandika/kasir-pos/commit/ac391cc454ab5b23e4b4a924f9d7a539ad1d1af9))
* **admin:** apply live search, auto-focus, and Enter-edit to UserView ([7385d4c](https://github.com/Panandika/kasir-pos/commit/7385d4c8ee97fb28e6239ab3489f9a424f28709f))
* align DatabaseValidator columns with actual schema ([37f7d00](https://github.com/Panandika/kasir-pos/commit/37f7d00ec6898139315bd67b46200253a08bdf27))
* bento home layout — fill width, header/footer breathing room, direct-letter shortcuts ([be885e8](https://github.com/Panandika/kasir-pos/commit/be885e80afcd5a26a697752fc8e58fe84f6878b8))
* bump ExpectedSchemaVersion to 2 to match Migration_002 ([200a7a5](https://github.com/Panandika/kasir-pos/commit/200a7a5e96a352dd9850062d4bc7ffa036c06ca9))
* copy e_sqlite3.dll native DLL to test output in CI ([0919df5](https://github.com/Panandika/kasir-pos/commit/0919df5c5cbd9f9b75d7380c6a0b5ba0a9256869))
* explicit Height=180 on each bento tile button ([735ca6c](https://github.com/Panandika/kasir-pos/commit/735ca6c0097724268716d95faad3ac70524b4a8b))
* force fullscreen imperatively in OnOpened (macOS ignored XAML state) ([c4d3061](https://github.com/Panandika/kasir-pos/commit/c4d306124229abbd2d2413a5cc84860fd65a529e))
* **inventory:** wire Enter-edit on OpnameView grid ([da01750](https://github.com/Panandika/kasir-pos/commit/da0175028c2cc8c3ecba31f2e3050180376ba987))
* macOS fullscreen via Dispatcher.UIThread.Post + remove WindowDecorations=None ([8223840](https://github.com/Panandika/kasir-pos/commit/822384071f5ce3e997b0ee00d2b0c67c398b7e68))
* **master:** apply live search, auto-focus, and Enter-edit to all Master views ([a716626](https://github.com/Panandika/kasir-pos/commit/a716626c7da268334ee0ffdade66c22f63a17417))
* **money:** widen Int32 to Int64 on price/value fields ([d7a940c](https://github.com/Panandika/kasir-pos/commit/d7a940caf3225f8c27ba20e4e4454d295ada00e0))
* **nav:** Esc hijack from leaked tunneled handler + auto-focus on swap ([ea7dd4e](https://github.com/Panandika/kasir-pos/commit/ea7dd4e9733ea9c68fcc38c0acbdc3a32bcdf4f7))
* **nav:** make UserControl root focusable so Esc fires without click ([328571c](https://github.com/Panandika/kasir-pos/commit/328571cd202c82f7d0f7a7179754028b29faf11b))
* **release:** use plain v* tags for single-package repo ([68f25fd](https://github.com/Panandika/kasir-pos/commit/68f25fdcc9b880901c389e6fd97e55b5785e9ea4))
* remove non-existent SQLite.Core 2.0.3, add CopyLocalLockFileAssemblies for native DLLs ([30b1342](https://github.com/Panandika/kasir-pos/commit/30b1342c33ede10635d56585011570c608e393df))
* startup shows login window, macOS fullscreen, nullable warnings, vuln packages ([5af5270](https://github.com/Panandika/kasir-pos/commit/5af5270eaa938fafb79c04d93d0681041b4f7cae))
* tight bento tile height — fixed 180px rows, top-aligned grid, padding 16,20 to prevent underline clipping ([17697f9](https://github.com/Panandika/kasir-pos/commit/17697f9bc1119ab4c302b7c73ab4bed8dd0e29aa))
* tight-pack bento — Grid with 4px ColumnSpacing/RowSpacing, edge-to-edge ([a6b6175](https://github.com/Panandika/kasir-pos/commit/a6b6175c82ef360379d73c50dfac2589c954fd14))
* widen all money fields from int to long to prevent overflow on prices &gt;= Rp 21.5M ([379bb30](https://github.com/Panandika/kasir-pos/commit/379bb30276d53189c9dcfa7931dc0ce8c6133875))


### Refactoring

* add NavigationService, ShellWindow infrastructure ([22f08bb](https://github.com/Panandika/kasir-pos/commit/22f08bb53647926b54ab119979383afadf7f4f2f))
* convert FirstRunWindow to in-window UserControl, no popup on first run ([26b03e5](https://github.com/Panandika/kasir-pos/commit/26b03e5d659d9c7a3b5814c2997b5ac213c15ab4))
* LoginView + MainMenuView as UserControls ([1c4e37a](https://github.com/Panandika/kasir-pos/commit/1c4e37a250eda3a1161829b364655d8991afffe4))
* **schema:** derive ExpectedSchemaVersion from MigrationRunner ([879a639](https://github.com/Panandika/kasir-pos/commit/879a639c3feb0d9988b2cd3d3f2baced3db5dff0))
* single-window navigation — replace 35 Windows with ShellWindow + UserControls ([2d3a6b3](https://github.com/Panandika/kasir-pos/commit/2d3a6b3d0c3d9927566ad11ad8210625f2215b62))


### Performance

* **reports:** defer ProductReport initial load off navigation path ([6647202](https://github.com/Panandika/kasir-pos/commit/66472021406d02f9fa17212053023893d29cbb41))

## [2.1.0](https://github.com/Panandika/kasir-pos/compare/kasir-v2.0.0...kasir-v2.1.0) (2026-04-18)


### Features

* compact header, responsive bento tiles, shared CurrentSession ([e2c4f35](https://github.com/Panandika/kasir-pos/commit/e2c4f35df0e4a31af16cc770cc4302cb6cfd9d6b))
* drill-down bento + tunneled shortcuts + responsive shell + macOS DevTools skip ([16a5716](https://github.com/Panandika/kasir-pos/commit/16a5716328f83376bb3bda46a355deacb9345e39))
* **infra:** add ViewShortcuts helper for grid Enter interception and auto-focus ([f377de3](https://github.com/Panandika/kasir-pos/commit/f377de3931c971f1a575c61e4f3aaf98120a4898))
* **pos:** add Barang Tanpa Kode misc-item flow ([73350b3](https://github.com/Panandika/kasir-pos/commit/73350b32d7c4836c8ef7b8904cda2570f94a26d3))
* **pos:** Penjualan UX overhaul and deterministic navigation ([74f8ca2](https://github.com/Panandika/kasir-pos/commit/74f8ca2a3d79129a6e3b46300dce0b9ab6fb9762))


### Bug Fixes

* **accounting:** apply live search, auto-focus, and Enter-edit to Accounting views ([4f1cd73](https://github.com/Panandika/kasir-pos/commit/4f1cd73d3dff63aeaa5d61ff3a6b4f136112b407))
* add SQLitePCLRaw.bundle_e_sqlite3 for native DLL ([c8cd41e](https://github.com/Panandika/kasir-pos/commit/c8cd41e2f1346fc2c0e9da49069529b899be2065))
* add System.Data.SQLite.Core for native e_sqlite3.dll in test output ([ac391cc](https://github.com/Panandika/kasir-pos/commit/ac391cc454ab5b23e4b4a924f9d7a539ad1d1af9))
* **admin:** apply live search, auto-focus, and Enter-edit to UserView ([7385d4c](https://github.com/Panandika/kasir-pos/commit/7385d4c8ee97fb28e6239ab3489f9a424f28709f))
* align DatabaseValidator columns with actual schema ([37f7d00](https://github.com/Panandika/kasir-pos/commit/37f7d00ec6898139315bd67b46200253a08bdf27))
* bento home layout — fill width, header/footer breathing room, direct-letter shortcuts ([be885e8](https://github.com/Panandika/kasir-pos/commit/be885e80afcd5a26a697752fc8e58fe84f6878b8))
* bump ExpectedSchemaVersion to 2 to match Migration_002 ([200a7a5](https://github.com/Panandika/kasir-pos/commit/200a7a5e96a352dd9850062d4bc7ffa036c06ca9))
* copy e_sqlite3.dll native DLL to test output in CI ([0919df5](https://github.com/Panandika/kasir-pos/commit/0919df5c5cbd9f9b75d7380c6a0b5ba0a9256869))
* explicit Height=180 on each bento tile button ([735ca6c](https://github.com/Panandika/kasir-pos/commit/735ca6c0097724268716d95faad3ac70524b4a8b))
* force fullscreen imperatively in OnOpened (macOS ignored XAML state) ([c4d3061](https://github.com/Panandika/kasir-pos/commit/c4d306124229abbd2d2413a5cc84860fd65a529e))
* **inventory:** wire Enter-edit on OpnameView grid ([da01750](https://github.com/Panandika/kasir-pos/commit/da0175028c2cc8c3ecba31f2e3050180376ba987))
* macOS fullscreen via Dispatcher.UIThread.Post + remove WindowDecorations=None ([8223840](https://github.com/Panandika/kasir-pos/commit/822384071f5ce3e997b0ee00d2b0c67c398b7e68))
* **master:** apply live search, auto-focus, and Enter-edit to all Master views ([a716626](https://github.com/Panandika/kasir-pos/commit/a716626c7da268334ee0ffdade66c22f63a17417))
* **money:** widen Int32 to Int64 on price/value fields ([d7a940c](https://github.com/Panandika/kasir-pos/commit/d7a940caf3225f8c27ba20e4e4454d295ada00e0))
* **nav:** Esc hijack from leaked tunneled handler + auto-focus on swap ([ea7dd4e](https://github.com/Panandika/kasir-pos/commit/ea7dd4e9733ea9c68fcc38c0acbdc3a32bcdf4f7))
* **nav:** make UserControl root focusable so Esc fires without click ([328571c](https://github.com/Panandika/kasir-pos/commit/328571cd202c82f7d0f7a7179754028b29faf11b))
* remove non-existent SQLite.Core 2.0.3, add CopyLocalLockFileAssemblies for native DLLs ([30b1342](https://github.com/Panandika/kasir-pos/commit/30b1342c33ede10635d56585011570c608e393df))
* startup shows login window, macOS fullscreen, nullable warnings, vuln packages ([5af5270](https://github.com/Panandika/kasir-pos/commit/5af5270eaa938fafb79c04d93d0681041b4f7cae))
* tight bento tile height — fixed 180px rows, top-aligned grid, padding 16,20 to prevent underline clipping ([17697f9](https://github.com/Panandika/kasir-pos/commit/17697f9bc1119ab4c302b7c73ab4bed8dd0e29aa))
* tight-pack bento — Grid with 4px ColumnSpacing/RowSpacing, edge-to-edge ([a6b6175](https://github.com/Panandika/kasir-pos/commit/a6b6175c82ef360379d73c50dfac2589c954fd14))
* widen all money fields from int to long to prevent overflow on prices &gt;= Rp 21.5M ([379bb30](https://github.com/Panandika/kasir-pos/commit/379bb30276d53189c9dcfa7931dc0ce8c6133875))


### Refactoring

* add NavigationService, ShellWindow infrastructure ([22f08bb](https://github.com/Panandika/kasir-pos/commit/22f08bb53647926b54ab119979383afadf7f4f2f))
* convert FirstRunWindow to in-window UserControl, no popup on first run ([26b03e5](https://github.com/Panandika/kasir-pos/commit/26b03e5d659d9c7a3b5814c2997b5ac213c15ab4))
* LoginView + MainMenuView as UserControls ([1c4e37a](https://github.com/Panandika/kasir-pos/commit/1c4e37a250eda3a1161829b364655d8991afffe4))
* **schema:** derive ExpectedSchemaVersion from MigrationRunner ([879a639](https://github.com/Panandika/kasir-pos/commit/879a639c3feb0d9988b2cd3d3f2baced3db5dff0))
* single-window navigation — replace 35 Windows with ShellWindow + UserControls ([2d3a6b3](https://github.com/Panandika/kasir-pos/commit/2d3a6b3d0c3d9927566ad11ad8210625f2215b62))


### Performance

* **reports:** defer ProductReport initial load off navigation path ([6647202](https://github.com/Panandika/kasir-pos/commit/66472021406d02f9fa17212053023893d29cbb41))
<!-- trigger -->
