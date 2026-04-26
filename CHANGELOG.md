# Changelog

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
