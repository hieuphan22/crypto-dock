# Crypto Dock Agent Context

This document captures the project history, architecture, implementation decisions, and operational notes for future AI agents or developers working on Crypto Dock.

## Project Summary

Crypto Dock is a local PowerToys Command Palette extension that adds realtime crypto price tickers to Command Palette Dock.

The extension is built as a packaged C# Command Palette extension and is intended for local use on the developer's Windows machine. It does not need Microsoft Store or winget distribution.

GitHub repository:

- `https://github.com/hieuphan22/crypto-dock`

Local repository:

- `E:\source\CryptoDock`

## Current Tech Stack

- .NET: `net10.0-windows10.0.26100.0`
- Windows App SDK: `2.2.0`
- Command Palette SDK: `Microsoft.CommandPalette.Extensions` `0.9.260303001`
- WinRT server helper: `Shmuelie.WinRTServer` `2.2.1`
- CsWin32: `Microsoft.Windows.CsWin32` `0.3.275`
- Runtime identifier: `win-x64`

PowerToys reference state when checked:

- Installed PowerToys on the machine: `0.100.0`
- PowerToys `main` used `Microsoft.CommandPalette.Extensions` `0.9.260303001`
- PowerToys `main` used `.NET 10` for Command Palette-related projects
- PowerToys `main` used Windows App SDK `2.0.1`

Crypto Dock intentionally keeps Windows App SDK `2.2.0` because it builds and runs correctly and is newer than the PowerToys source reference.

## Repository Layout

```text
E:\source\CryptoDock
|-- AGENTS.md
|-- README.md
|-- CryptoDock.sln
|-- Directory.Build.props
|-- Directory.Packages.props
|-- nuget.config
|-- .gitignore
`-- CryptoDock
    |-- CryptoDock.csproj
    |-- Package.appxmanifest
    |-- app.manifest
    |-- Program.cs
    |-- CryptoDockExtension.cs
    |-- CryptoDockCommandsProvider.cs
    |-- SettingsManager.cs
    |-- MarketModels.cs
    |-- Commands
    |   |-- AddSymbolCommand.cs
    |   `-- RemoveSymbolCommand.cs
    |-- Pages
    |   |-- CryptoDockBand.cs
    |   |-- CryptoDockPage.cs
    |   `-- CryptoTickerDockItem.cs
    |-- Services
    |   |-- CryptoTicker.cs
    |   `-- CryptoTickerService.cs
    `-- Assets
```

## Important Files

### `CryptoDock/CryptoDock.csproj`

Defines the packaged Command Palette extension.

Important settings:

- `TargetFramework` is `net10.0-windows10.0.26100.0`.
- `EnableMsixTooling` is enabled.
- `GenerateAppxPackageOnBuild` is enabled.
- The project is signed with a local development certificate.
- Debug builds disable trimming, single-file, and AOT.
- Release has trim/AOT-related properties, but Release publishing has not been the main path used during development.

### `Directory.Packages.props`

Central package versions:

- `Microsoft.CommandPalette.Extensions` `0.9.260303001`
- `Microsoft.WindowsAppSDK` `2.2.0`
- `Microsoft.Windows.CsWin32` `0.3.275`
- `Shmuelie.WinRTServer` `2.2.1`

### `Directory.Build.props`

Build output is redirected away from OneDrive/Unicode paths:

- `BaseIntermediateOutputPath`: `C:\tmp\CryptoDockBuildNet10\obj\`
- `BaseOutputPath`: `C:\tmp\CryptoDockBuildNet10\bin\`
- `RestorePackagesPath`: `C:\tmp\CryptoDockPackages`

This was added because CsWinRT/MSBuild had issues when building under the original long OneDrive path containing Vietnamese characters.

### `.gitignore`

Ignores local/generated files:

- `.dotnet-home/`
- `CryptoDock/AppPackages/`
- `CryptoDock/bin/`
- `CryptoDock/obj/`
- local signing certificates: `CryptoDock/*.cer`, `CryptoDock/*.pfx`

Do not commit local certificate files or generated MSIX packages.

## Command Palette Architecture

### `CryptoDockCommandsProvider`

Main `CommandProvider`.

Responsibilities:

- Sets provider metadata:
  - `DisplayName`: `Crypto Dock`
  - `Id`: `com.hieuphan.cmdpal.cryptodock`
  - icon: ticker-style glyph
- Creates shared singletons for:
  - `CryptoTickerService`
  - `SettingsManager`
  - `CryptoDockPage`
  - `CryptoDockBand`
- Exposes:
  - `TopLevelCommands()`: management/search page titled `Manage Crypto Dock`
  - `GetDockBands()`: dock band titled `Crypto Tickers`
- Disposes the dock band and ticker service.

### `CryptoDockPage`

Inherits `DynamicListPage`.

Responsibilities:

- Shows current watched symbols when search text is empty.
- Searches Binance symbols when the user types.
- Uses `AddSymbolCommand` and `RemoveSymbolCommand`.
- Provides context commands:
  - copy price
  - copy details
  - open Binance
  - remove from dock

Reason for `DynamicListPage`:

- A previous version used `ListPage`, but search text updates were not flowing correctly.
- `DynamicListPage.UpdateSearchText()` is used to call `RaiseItemsChanged()`.

Known limitation:

- `GetItems()` is synchronous by SDK design, so it currently calls async Binance APIs with `.GetAwaiter().GetResult()`.
- This is acceptable for the current small watchlist, but a future optimization could add a local cache and background refresh for the management page.

### `CryptoDockBand`

Inherits `WrappedDockItem`.

Responsibilities:

- Owns the dock ticker list.
- Runs an async refresh loop.
- Rebuilds dock items when symbols change.
- Refreshes immediately when:
  - symbols are added/removed
  - refresh interval settings change
- Uses semaphores to avoid overlapping refresh operations.
- Marks items offline when a refresh fails.

Important behavior:

- `GetDockBands()` makes the extension appear as a dock band.
- The visible dock band name is `Crypto Tickers`.
- The management command is separate from the dock band and is titled `Manage Crypto Dock`.

Why two items could appear in Dock:

- Command Palette can show both the top-level command and the dock band.
- The top-level command is for management/search/settings.
- The dock band is the actual ticker row.
- Users should pin `Crypto Tickers` for dock monitoring and avoid pinning `Manage Crypto Dock` unless they want quick access to settings/search.

### `CryptoTickerDockItem`

Represents one ticker in the dock.

Displays:

- base symbol, e.g. `BTC`
- price
- short market label:
  - `S` for Spot
  - `F` for Futures
- 24h change
- direction icon

Keeps the last successful ticker so offline state can show the last known price/time.

## Data Model

### `MarketKind`

```csharp
public enum MarketKind
{
    Spot,
    Futures,
}
```

### `MarketSource`

Used by settings to control search scope:

```csharp
public enum MarketSource
{
    Spot,
    Futures,
    Both,
}
```

### `WatchedSymbol`

Stores one tracked symbol with market type.

Examples:

- `spot:BTCUSDT`
- `futures:XAUUSDT`

Legacy plain symbols migrate to Spot.

Symbol normalization:

- trims whitespace
- removes `/`
- uppercases

## Settings and Persistence

Implemented in `SettingsManager`, which inherits `JsonSettingsManager`.

Settings file:

- `%LOCALAPPDATA%\Microsoft.CmdPal\cryptoDock.settings.json`

Watched symbols file:

- `%LOCALAPPDATA%\Microsoft.CmdPal\cryptoDock.symbols.txt`

Default symbols:

- `spot:BTCUSDT`
- `spot:ETHUSDT`
- `spot:BNBUSDT`
- `spot:SOLUSDT`
- `spot:XRPUSDT`

Settings:

- Refresh interval:
  - `10 seconds`
  - `30 seconds`
  - `1 minute`
  - `5 minutes`
- Search market:
  - `Spot + Futures`
  - `Spot only`
  - `Futures only`

Events:

- `SymbolsChanged`: raised after add/remove.
- `SettingsChanged`: raised when Command Palette settings change.

`CryptoDockBand` subscribes to both events.

## Binance Integration

Implemented in `CryptoTickerService`.

HTTP clients:

- Spot:
  - base URL: `https://api.binance.com`
  - ticker path: `/api/v3/ticker/24hr`
  - exchange info path: `/api/v3/exchangeInfo`
- Futures:
  - base URL: `https://fapi.binance.com`
  - ticker path: `/fapi/v1/ticker/24hr`
  - exchange info path: `/fapi/v1/exchangeInfo`

Timeout:

- 8 seconds

JSON:

- Uses `System.Text.Json` source generation via `CryptoDockJsonContext`.
- Response records use `[JsonPropertyName]` because Binance JSON fields are camelCase.

Search:

- Calls Binance `exchangeInfo`.
- Caches symbols in memory per market.
- Only returns symbols where:
  - `status == TRADING`
  - `quoteAsset == USDT`
  - symbol/base asset are not empty

Ticker loading:

- Spot tickers are fetched in batch with the `symbols` parameter.
- Futures tickers are fetched individually.

Reason:

- During development, Binance futures ticker batch behavior was not reliable for filtered symbol requests and could return large/unfiltered data. Individual requests were more predictable for the current small watchlist.

## Feature History

Implemented features:

- Initial PowerToys Command Palette extension scaffold.
- Local MSIX packaging.
- Local test certificate generation and signing.
- Binance realtime price loading.
- JSON source generation and Binance field mapping.
- Command Palette Dock band support with `GetDockBands()`.
- Multiple default tickers.
- Copy price and copy details commands.
- Open Binance command.
- Search/add symbols from Command Palette.
- Persistent watchlist.
- Remove symbols from dock.
- Refresh interval setting.
- Spot/Futures search setting.
- Futures support for symbols such as `XAUUSDT` and `XAGUSDT`.
- Renamed management page to `Manage Crypto Dock`.
- Renamed actual dock band to `Crypto Tickers`.
- Upgraded project to `.NET 10`.
- Moved source from OneDrive path to `E:\source\CryptoDock`.
- Moved git repo to `E:\source\CryptoDock`.
- Created GitHub repo and pushed to `hieuphan22/crypto-dock`.

## Build

Recommended build command:

```powershell
cd E:\source\CryptoDock
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
dotnet build .\CryptoDock.sln --nologo --verbosity minimal
```

Expected output:

- DLL output:
  - `C:\tmp\CryptoDockBuildNet10\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\CryptoDock.dll`
- MSIX package:
  - `E:\source\CryptoDock\CryptoDock\AppPackages\CryptoDock_1.0.0.0_x64_Debug_Test\CryptoDock_1.0.0.0_x64_Debug.msix`

Known warning:

- Build may warn that `mspdbcmf.exe` is missing.
- This only means no symbols package is generated.
- The MSIX itself still builds and signs correctly.

## Install Locally

Build first, then run:

```powershell
cd E:\source\CryptoDock\CryptoDock\AppPackages\CryptoDock_1.0.0.0_x64_Debug_Test
.\Install.ps1 -Force
```

If certificate trust is needed:

```powershell
Import-Certificate -FilePath .\CryptoDock_1.0.0.0_x64_Debug.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
```

Then reload Command Palette:

- Open PowerToys Command Palette.
- Run `Reload Command Palette Extension`.
- Enable Dock if needed.
- Pin `Crypto Tickers`.

## Git

Repository:

```text
https://github.com/hieuphan22/crypto-dock
```

Local branch:

```text
main
```

Remote:

```text
origin  https://github.com/hieuphan22/crypto-dock.git
```

Local git identity:

```text
user.name  Hieu Phan
user.email phantrunghieu2210@gmail.com
```

## Naming Decision

Chosen repository name:

```text
crypto-dock
```

Reasoning:

- Short and easy to remember.
- Easy to search.
- Matches the PowerToys Command Palette Dock feature.
- Does not lock the project to Binance forever.
- Leaves room for future providers such as TradingView, CoinGecko, or other exchanges.

Alternative names considered:

- `crypto-binance-dock`
- `binance-price-dock`
- `command-palette-crypto-dock`
- `crypto-ticker-dock`

## Known Good Runtime Behavior

The extension has been validated locally with PowerToys Command Palette Dock:

- Dock displays multiple ticker items.
- BTC, ETH, BNB, SOL, XRP spot prices show correctly.
- Search/add works for additional symbols.
- Futures search was added for symbols not available on Spot.
- Refresh interval settings are available.
- Spot/Futures search scope settings are available.

## Known Limitations and Future Work

### Management page async behavior

`CryptoDockPage.GetItems()` currently blocks on async network calls because Command Palette list item generation is synchronous.

Potential future improvement:

- Add a ticker/search cache.
- Return cached results immediately.
- Refresh page data in the background.
- Call `RaiseItemsChanged()` after background refresh.

### Error handling granularity

Current dock refresh marks all items offline if the whole refresh operation fails.

Potential future improvement:

- Track failures per symbol.
- Keep successful symbols updated even if one symbol fails.
- Add subtle per-symbol stale/offline status.

### Provider abstraction

Currently the data provider is Binance-specific.

Potential future improvement:

- Introduce `IMarketDataProvider`.
- Add provider settings.
- Support TradingView/CoinGecko/other exchanges.

### Release publishing

The current workflow is local debug MSIX installation.

Potential future improvement:

- Validate Release build.
- Validate trimming/AOT behavior.
- Decide whether to keep self-contained/single-file release settings.

## Development Rules for Future Agents

- Prefer Microsoft Command Palette Toolkit types over raw interfaces unless there is a clear reason.
- Keep the dock band and management page separate.
- Do not commit local signing certificates or generated MSIX packages.
- Do not hard-code new symbols into source unless they are intentional defaults.
- Persist user-selected symbols through `SettingsManager`.
- Preserve Spot/Futures market labels because the same symbol may exist in both markets.
- Avoid frequent polling beyond user-selected interval.
- Keep Binance API calls minimal:
  - batch Spot requests where possible
  - keep Futures predictable for the small watchlist
- After changing code, run:

```powershell
cd E:\source\CryptoDock
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
dotnet build .\CryptoDock.sln --nologo --verbosity minimal
```

- After rebuilding, reinstall from:

```text
E:\source\CryptoDock\CryptoDock\AppPackages\CryptoDock_1.0.0.0_x64_Debug_Test
```

## Useful Microsoft Docs

- Command Palette extension creation:
  - `https://learn.microsoft.com/windows/powertoys/command-palette/creating-an-extension`
- Command Palette SDK namespaces:
  - `https://learn.microsoft.com/windows/powertoys/command-palette/sdk-namespaces`
- Command Palette Toolkit:
  - `https://learn.microsoft.com/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/`

