# Crypto Dock Agent Context

This document captures the project architecture, build environment, implementation decisions, and operational notes for developers and AI agents working on Crypto Dock.

## Project Summary

Crypto Dock is a local PowerToys Command Palette extension written in C# that adds realtime crypto price tickers to the Command Palette Dock.

It is packaged as a local MSIX COM/WinRT extension intended for local sideloading on Windows rather than Microsoft Store or winget distribution.

Repository:

- `https://github.com/hieuphan22/crypto-dock`

## Current Tech Stack

- **.NET SDK**: `.NET 10` pinned via `global.json` (`10.0.403`, `rollForward: latestFeature`)
- **Target Framework**: `net10.0-windows10.0.26100.0` (`AssetTargetFallback`: `net8.0-windows10.0.22000.0`)
- **Windows App SDK**: `2.2.0`
- **Command Palette SDK**: `Microsoft.CommandPalette.Extensions` `0.9.260303001`
- **WinRT Server Helper**: `Shmuelie.WinRTServer` `2.2.1`
- **CsWin32**: `Microsoft.Windows.CsWin32` `0.3.275`
- **Runtime Identifier**: `win-x64` (`Platforms`: `x64;arm64`)

Reference environment note:

- Verified against PowerToys `0.103.3062.0` on Windows.
- `CryptoDock` intentionally keeps `Microsoft.CommandPalette.Extensions` `0.9.260303001` and `Microsoft.WindowsAppSDK` `2.2.0` on `.NET 10` because the combination builds cleanly and runs reliably against the installed Command Palette host.
- `global.json` is critical on machines that also have `.NET 11` preview/RC SDKs installed, because `.NET 11` breaks CsWinRT `IIDOptimizer` during build unless SDK selection stays on `.NET 10`.

## Repository Layout

```text
.
|-- .github
|   `-- workflows
|       |-- ci.yml
|       `-- release.yml
|-- AGENTS.md
|-- README.md
|-- CryptoDock.sln
|-- Directory.Build.props
|-- Directory.Packages.props
|-- global.json
|-- nuget.config
|-- .gitignore
|-- CryptoDock
|   |-- CryptoDock.csproj
|   |-- Package.appxmanifest
|   |-- app.manifest
|   |-- Program.cs
|   |-- CryptoDockExtension.cs
|   |-- CryptoDockCommandsProvider.cs
|   |-- SettingsManager.cs
|   |-- MarketModels.cs
|   |-- Commands
|   |   |-- AddSymbolCommand.cs
|   |   |-- MoveDownCommand.cs
|   |   |-- MoveUpCommand.cs
|   |   `-- RemoveSymbolCommand.cs
|   |-- Pages
|   |   |-- CryptoDockBand.cs
|   |   |-- CryptoDockPage.cs
|   |   `-- CryptoTickerDockItem.cs
|   |-- Services
|   |   |-- CryptoTicker.cs
|   |   |-- CryptoTickerService.cs
|   |   `-- VolatilityAlertTracker.cs
|   `-- Assets
`-- CryptoDock.Tests
    |-- CryptoDock.Tests.csproj
    |-- CryptoTickerTests.cs
    |-- SettingsManagerTests.cs
    |-- VolatilityAlertTrackerTests.cs
    `-- WatchedSymbolTests.cs
```

## Build Configuration Files

### `global.json`

Pins the .NET SDK to `10.0.403` with `rollForward: latestFeature` so CLI and IDE builds stay on the `.NET 10` toolchain.

### `CryptoDock/CryptoDock.csproj`

Defines the packaged `WinExe` Command Palette extension:

- `TargetFramework`: `net10.0-windows10.0.26100.0`
- `EnableMsixTooling`: enabled
- `GenerateAppxPackageOnBuild`: defaults to `true` (overridable via CLI)
- Local test certificate signing (`CryptoDock_TemporaryKey.pfx`) is enabled automatically when the `.pfx` exists locally and disabled by default on clean clones/CI unless signing properties are passed explicitly
- Both `Debug` and `Release` disable `PublishTrimmed`, `PublishSingleFile`, and `PublishAot` (Windows App SDK packaged MSIX apps do not support single-file publishing and ILLink trimming conflicts with packaged WinRT builds) while keeping trim/single-file/AOT static analyzers enabled (`0` warnings)

### `Directory.Packages.props`

Central package versions (`ManagePackageVersionsCentrally` enabled in `Directory.Build.props`):

- `Microsoft.CommandPalette.Extensions` `0.9.260303001`
- `Microsoft.WindowsAppSDK` `2.2.0`
- `Microsoft.Windows.CsWin32` `0.3.275`
- `Shmuelie.WinRTServer` `2.2.1`
- `Microsoft.NET.Test.Sdk` `18.12.0`, `xunit` `2.9.3`, `xunit.runner.visualstudio` `4.0.1` (`CryptoDock.Tests`)

### `Directory.Build.props`

Redirects local build artifacts outside the workspace tree (`Condition="'$(CI)' != 'true'"`):

- `BaseIntermediateOutputPath`: `C:\tmp\CryptoDockBuildNet10\$(MSBuildProjectName)\obj\`
- `BaseOutputPath`: `C:\tmp\CryptoDockBuildNet10\$(MSBuildProjectName)\bin\`
- `RestorePackagesPath`: `C:\tmp\CryptoDockPackages`

Separating by `$(MSBuildProjectName)` avoids `obj`/`project.assets.json` collisions between `CryptoDock` and `CryptoDock.Tests` while avoiding CsWinRT/MSBuild path-length and non-ASCII/OneDrive path issues on developer machines. In CI (`CI=true`), standard workspace paths are used.

### `.gitignore`

Ignores local/generated artifacts:

- `.dotnet-home/`
- `CryptoDock/AppPackages/`
- `CryptoDock/bin/`
- `CryptoDock/obj/`
- `CryptoDock/*.cer`, `CryptoDock/*.pfx`
- `CryptoDock.Tests/bin/`, `CryptoDock.Tests/obj/`, `TestResults/`

Never commit local certificates, private keys, or generated MSIX packages.

## Command Palette Architecture

### `Program.cs` & `CryptoDockExtension.cs`

- `Program.cs` handles `-RegisterProcessAsComServer` activation using `Shmuelie.WinRTServer.ComServer`.
- `CryptoDockExtension` exposes COM CLSID `106E39F7-BFDD-4C88-A92C-99FE3F765150` (matching `Package.appxmanifest`) and returns `CryptoDockCommandsProvider`.

### `CryptoDockCommandsProvider.cs`

Main `CommandProvider`:

- Provider metadata:
  - `DisplayName`: `Crypto Dock`
  - `Id`: `com.hieuphan.cmdpal.cryptodock`
- Instantiates shared instances of:
  - `CryptoTickerService`
  - `SettingsManager`
  - `CryptoDockPage`
  - `CryptoDockBand`
- Exposes:
  - `TopLevelCommands()`: `Manage Crypto Dock` page plus settings context entry
  - `GetDockBands()`: `CryptoDockBand` (`Crypto Tickers`)
- Disposes the management page, dock band, and ticker service on shutdown.

### `CryptoDockPage.cs`

Inherits `DynamicListPage` and implements `IDisposable` (`Title`: `Manage Crypto Dock`):

- Subscribes to `SettingsManager.SymbolsChanged` so the tracked list or search status updates immediately when symbols are added, removed, or reordered.
- When search text is empty, loads tracked tickers (including 24h high/low formatted via `CryptoTicker.FormatPrice` and coin logos via `CryptoTickerDockItem.CreateLogoOrFallbackIcon`) with commands:
  - Default action: `Copy price` (`ticker.Summary`)
  - `MoreCommands`: `Copy details`, `Open Binance` (`WatchedSymbol.BinanceUrl`), `Move Up`, `Move Down`, `Remove from dock`
- When search text is entered, searches cached Binance `exchangeInfo` pairs across the configured `MarketSource` scope (up to 25 results) and presents `AddSymbolCommand` or `RemoveSymbolCommand`.
- Uses `DynamicListPage.UpdateSearchText()` -> `RaiseItemsChanged()`.

Known limitation:

- `GetItems()` is synchronous by Command Palette SDK design, so it calls async `CryptoTickerService` methods with `.GetAwaiter().GetResult()`.

### `CryptoDockBand.cs`

Inherits `WrappedDockItem` and implements `IDisposable` (`Id`: `com.hieuphan.cmdpal.cryptodock.band`, display title: `Crypto Tickers`):

- Owns the ordered collection of `CryptoTickerDockItem` instances (guarded by `_itemsLock`).
- Reuses existing `CryptoTickerDockItem` instances across `RebuildItems()` when symbols are reordered, added, or removed, avoiding transient `"loading"` flickers and icon reloads.
- Runs an async background polling loop using the user-configured refresh interval and disposes `_refreshLock`, `_refreshSignal`, and `_cancellation` safely when the loop exits.
- Uses semaphore synchronization to avoid overlapping refresh cycles.
- Triggers immediate rebuild/refresh on `SettingsManager.SymbolsChanged` and `SettingsManager.SettingsChanged`.
- Feeds `VolatilityAlertTracker` when volatility alerts are enabled and passes any triggered `VolatilityAlert` to each dock item.
- Tolerates transient network blips: requires `_consecutiveFailures >= 3` before marking dock items offline.

Why two items exist in Command Palette:

- The top-level command (`Manage Crypto Dock`) is for search, watchlist management, reordering, and settings.
- The dock band (`Crypto Tickers`) is the live ticker strip intended to be pinned in the Command Palette Dock.

### `CryptoTickerDockItem.cs`

Represents one ticker item in `CryptoDockBand`:

- Displays:
  - `Title`: `Pair DirectionArrow PriceText` (e.g. `BTC ▲ 95,000`)
  - `Subtitle`: triggered volatility alert label, or `ShortMarketLabel ChangeText` (`S +2.15%` / `F -1.04%`)
  - `Icon`: remote coin logo loaded via shared `CreateLogoOrFallbackIcon(...)` (`RandomAccessStreamReference.CreateFromUri`) when `HasLogo` is true, falling back to alert or direction glyph if missing/failing
- Prevents UI flicker on 10-second polling by diffing `_appliedIconKey`, `Title`, `Subtitle`, and `_copyCommand.Text` before assigning properties.
- Retains `_lastTicker` so offline transitions preserve the last known price and timestamp.
- Context menu (`MoreCommands`):
  - `Copy price`
  - `Open Binance chart` (`_symbol.BinanceUrl`)
  - `Move Up` (`MoveUpCommand`)
  - `Move Down` (`MoveDownCommand`)
  - `Remove from dock` (`RemoveSymbolCommand`)

## Data Models & Reordering Commands

### `MarketModels.cs`

- `MarketKind`: `Spot`, `Futures`
- `MarketSource`: `Spot`, `Futures`, `Both`
- `WatchedSymbol`:
  - Normalized storage keys: `spot:SYMBOL` / `futures:SYMBOL`
  - Legacy plain symbols without prefix default to `MarketKind.Spot`
  - `NormalizeSymbol`: trims whitespace, removes `/`, uppercases
  - `BinanceUrl`: shared helper generating `https://www.binance.com/en/futures/{Symbol}` for Futures or `https://www.binance.com/en/trade/{BASE}_USDT` for Spot (using `EndsWith("USDT")` / `DisplaySymbol`)

### `Commands/`

- `AddSymbolCommand` / `RemoveSymbolCommand`: add or remove a `WatchedSymbol` via `SettingsManager` and return `CommandResult.KeepOpen()`.
- `MoveUpCommand` / `MoveDownCommand`: reorder a `WatchedSymbol` in the watchlist via `SettingsManager.MoveSymbolUp` / `MoveSymbolDown`, returning `CommandResult.KeepOpen()` so both Dock items and Management page order update immediately.

## Settings & Persistence (`SettingsManager.cs`)

Inherits `JsonSettingsManager`:

- Storage paths:
  - Constructor accepts optional `settingsDirectoryOverride` (used by unit tests for isolated temporary directories).
  - `#if DEBUG`: `%USERPROFILE%\.cryptoDock` (`cryptoDock.settings.json` and `cryptoDock.symbols.txt`) so developer MSIX reinstalls preserve local test configs.
  - Release builds: `Utilities.BaseSettingsPath("Microsoft.CmdPal")` (`cryptoDock.settings.json` and `cryptoDock.symbols.txt`).
- Default symbols (when `cryptoDock.symbols.txt` does not exist):
  - `spot:BTCUSDT`
  - `spot:ETHUSDT`
- Settings:
  - **Refresh interval**: `10 seconds`, `30 seconds`, `1 minute`, `5 minutes`
  - **Search market**: `Spot + Futures`, `Spot only`, `Futures only`
  - **Volatility alerts enabled**: `false` (disabled by default)
  - **Volatility analysis window**: `3 minutes`, `5 minutes`, `15 minutes`, `1 hour`
  - **Dump threshold** (% drop from window high): `1%`, `3%`, `5%`, `10%`
  - **Pump warning threshold** (% gain from window low): `1%`, `3%`, `5%`, `10%`
  - **Swing range threshold** (% high-low range in window): `1%`, `2%`, `5%`, `10%`, `15%`
- Events:
  - `SymbolsChanged`: raised after add, remove, move up, or move down
  - `SettingsChanged`: raised when Command Palette saves settings updates

## Market Data, Logo Fallback & Volatility Tracking

### `CryptoTickerService.cs` & `CryptoTicker.cs`

- **Price Formatting (`CryptoTicker.FormatPrice`)**: Formats `>= 100` with `#,0.##`, `>= 1` with `#,0.####`, and sub-`$1` tokens dynamically up to 10 decimal places so low-priced meme coins remain accurate in `PriceText`, `DetailsText`, and `CryptoDockPage`.
- **HTTP Clients & Timeouts**:
  - Spot client: `https://api.binance.com` (`/api/v3/ticker/24hr`, `/api/v3/exchangeInfo`, 8s timeout)
  - Futures client: `https://fapi.binance.com` (`/fapi/v1/ticker/24hr`, `/fapi/v1/exchangeInfo`, 8s timeout)
  - BAPI client (`get-all-asset`, 10s timeout)
  - CoinGecko client (`/api/v3/search`, 5s timeout, `User-Agent: CryptoDock/2.0`)
  - GitHub icon probe client (`spothq/cryptocurrency-icons`, 5s timeout, `User-Agent: CryptoDock/2.0`)
  - All `HttpClient` instances and `_bapiLock` (`SemaphoreSlim`) are disposed in `CryptoTickerService.Dispose()`.
- **JSON Serialization**:
  - Uses `System.Text.Json` source generation (`CryptoDockJsonContext`) with explicit `[JsonPropertyName]` mappings.
- **Ticker Polling**:
  - Spot tickers use batched `?symbols=[...]` requests.
  - Futures tickers are requested concurrently per symbol (`Task.WhenAll`) with per-symbol exception isolation when any ticker succeeds; if every requested ticker fails (`results.Count == 0` with an error), `GetTickersAsync` rethrows the failure so `CryptoDockBand` can enter offline mode even on Futures-only watchlists.
- **Symbol Search**:
  - Caches Binance `exchangeInfo` in memory per market and filters to `status == "TRADING"` with `quoteAsset == "USDT"`.
- **3-Tier Logo & Asset Name Resolution** (transient network errors are not cached permanently):
  1. Binance BAPI (`get-all-asset`, loaded under `_bapiLock` and cached in `_bapiCache`; retries after a 1-minute cooldown if loading fails)
  2. CoinGecko `/api/v3/search` matching exact symbol (`StringComparison.OrdinalIgnoreCase`, cached in `_coingeckoCache` only on successful HTTP responses)
  3. GitHub `spothq/cryptocurrency-icons` PNG URL availability check (cached in `_githubLogoCache` on HTTP 200 or 404 NotFound)

### `VolatilityAlertTracker.cs`

- Thread-safe (`_lock`) storage of up to 61 minutes of in-memory 1-minute OHLC bars (`MinuteBar`) per tracked `WatchedSymbol` while alerts are enabled.
- Evaluates `VolatilityAlertThresholds` (`WindowMinutes`, `DumpThresholdPercent`, `PumpThresholdPercent`, `SwingThresholdPercent`) across the selected rolling window (`3m`, `5m`, `15m`, `1h`).
- Alert labels (`VolatilityAlertKind`):
  - `Dump`: `🔴 DUMP -{Percent:0.##}%/{WindowLabel}` (glyph `\uE74B`)
  - `Pump`: `🟢 PUMP +{Percent:0.##}%/{WindowLabel}` (glyph `\uE74A`)
  - `Swing`: `⚡ SWING {Percent:0.##}%/{WindowLabel}` (glyph `\uE7C4`)

## Testing (`CryptoDock.Tests`)

- Uses xUnit (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`).
- Links domain/service source files directly (`MarketModels.cs`, `SettingsManager.cs`, `Services/CryptoTicker.cs`, `Services/VolatilityAlertTracker.cs`) without referencing `Microsoft.WindowsAppSDK`, avoiding unpackaged `WindowsAppRuntime` module-initializer crashes inside `testhost.exe` while testing the exact production source code.
- Covers `CryptoTicker.FormatPrice` / properties, `WatchedSymbol.Parse` / `BinanceUrl`, `VolatilityAlertTracker` threshold & rolling-window behavior, and isolated `SettingsManager` CRUD/migration.

## Build, Test & Release Automation

### Unit Tests

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
dotnet test .\CryptoDock.Tests\CryptoDock.Tests.csproj --nologo --verbosity minimal
```

### Build

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
dotnet build .\CryptoDock.sln --nologo --verbosity minimal
```

Expected outputs:

- DLL output: `C:\tmp\CryptoDockBuildNet10\CryptoDock\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\CryptoDock.dll`
- MSIX package: `CryptoDock\AppPackages\CryptoDock_1.0.0.0_x64_Debug_Test\CryptoDock_1.0.0.0_x64_Debug.msix`

### GitHub Actions Workflows

- `.github/workflows/ci.yml`: builds `Release` & `Debug` (`GenerateAppxPackageOnBuild=false`) and executes `CryptoDock.Tests` on `windows-latest`.
- `.github/workflows/release.yml`: triggered by `v*` tags or `workflow_dispatch`; runs unit tests, updates `Package.appxmanifest` version, builds and signs the Release x64 MSIX (using `MSIX_CERT_BASE64`/`MSIX_CERT_PASSWORD` secrets when present or an ephemeral `CN=CryptoDock` self-signed cert), uploads the `.zip` artifact, and publishes a GitHub Release on `v*` tags.

Known harmless build notes:

- Missing `mspdbcmf.exe` warning only prevents `.appxsym` symbol-package generation; the signed MSIX package still builds cleanly.
- If a background VBCSCompiler build server locks `CryptoDock.dll` in `obj\`, run `dotnet build-server shutdown` and rebuild.

### Install Locally

```powershell
cd .\CryptoDock\AppPackages\CryptoDock_1.0.0.0_x64_Debug_Test
Get-AppxPackage *CryptoDock* | Remove-AppxPackage
.\Install.ps1 -Force
```

If certificate trust is needed on a fresh machine:

```powershell
Import-Certificate -FilePath .\CryptoDock_1.0.0.0_x64_Debug.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
```

Then open PowerToys Command Palette, run **Reload Command Palette Extension**, enable Dock if needed, and pin **Crypto Tickers**.

## Development Rules for Future Agents

- Keep personal information (emails, usernames, private machine paths) out of tracked repository docs and source files.
- Keep `global.json` pinned to `.NET 10` so CsWinRT builds remain stable when newer preview SDKs are installed.
- Prefer Microsoft Command Palette Toolkit types over raw WinRT/COM interfaces unless clearly required.
- Keep the dock band (`CryptoDockBand`) and management page (`CryptoDockPage`) separate.
- Never commit local `.cer`/`.pfx` signing certificates or generated `AppPackages/` MSIX files.
- Do not hard-code extra watchlist symbols beyond the intentional `BTCUSDT`/`ETHUSDT` defaults; persist user choices in `SettingsManager`.
- Preserve Spot/Futures market labels and shared helpers (`WatchedSymbol.BinanceUrl`, `SettingsManager`).
- Avoid unnecessary API polling beyond the user-selected interval; keep Spot requests batched and Futures failures isolated per symbol.
- After any code edit, rebuild `CryptoDock.sln` and reinstall the MSIX locally to verify packaging and runtime behavior.

## Useful Microsoft Docs

- Command Palette extension creation: `https://learn.microsoft.com/windows/powertoys/command-palette/creating-an-extension`
- Command Palette SDK namespaces: `https://learn.microsoft.com/windows/powertoys/command-palette/sdk-namespaces`
- Command Palette Toolkit: `https://learn.microsoft.com/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/`
