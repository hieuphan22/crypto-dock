# Crypto Dock

Local PowerToys Command Palette extension for monitoring realtime Binance **Spot** and **Futures** crypto price tickers directly in the Command Palette Dock.

## Features

- **Live Dock Band (`Crypto Tickers`)**: Displays realtime prices, direction arrows, 24h percentage changes, market tags (`S` for Spot, `F` for Futures), and coin logos directly in the Command Palette Dock.
- **Management & Search Page (`Manage Crypto Dock`)**:
  - View tracked pairs with 24h high/low stats and coin icons.
  - Search live Binance Spot and Futures `USDT` trading pairs and add/remove symbols immediately.
- **Context Menu Actions** (available on Dock items and in `Manage Crypto Dock`):
  - Copy formatted price summary or full 24h ticker details (`Copy price`, `Copy details`).
  - Open the pair on Binance (`Open Binance chart` / `Open Binance`).
  - Reorder items (`Move Up`, `Move Down`) and remove symbols (`Remove from dock`).
- **3-Tier Coin Logo Resolution**: Resolves coin logos and names automatically with in-memory caching via Binance BAPI asset metadata, CoinGecko exact-symbol search fallback, and `spothq/cryptocurrency-icons` PNG fallback (falling back to status glyphs when offline or unavailable).
- **Flicker-Free Updates & Offline Resilience**: Diffs title, subtitle, copy text, and icon keys before updating Dock items, preserves existing ticker items cleanly during reordering (`Move Up` / `Move Down`), formats sub-cent meme coins dynamically up to 10 decimals, polls Futures pairs concurrently, and requires 3 consecutive full-refresh failures before switching items to an offline state (while isolating single-symbol Futures errors when other tickers succeed).
- **Configurable Refresh & Market Scope**:
  - Refresh interval: `10 seconds`, `30 seconds`, `1 minute`, or `5 minutes`.
  - Search scope: `Spot + Futures`, `Spot only`, or `Futures only`.
- **Optional Rolling Volatility Alerts** (disabled by default):
  - Tracks up to ~1 hour of in-memory 1-minute OHLC bars per tracked symbol.
  - Configurable analysis window (`3 minutes`, `5 minutes`, `15 minutes`, `1 hour`) and separate percentage thresholds for **Dump** (`🔴 DUMP -x%/window`), **Pump** (`🟢 PUMP +x%/window`), and **Swing** (`⚡ SWING x%/window`).

## Default Watchlist & Persistence

On first launch (when no saved watchlist exists), Crypto Dock initializes with:

- `spot:BTCUSDT`
- `spot:ETHUSDT`

Persistence paths depend on build configuration:

- **Debug builds** (`#if DEBUG`): stored in `%USERPROFILE%\.cryptoDock` (`cryptoDock.settings.json` and `cryptoDock.symbols.txt`) so settings survive local MSIX reinstalls during development.
- **Release builds**: stored in Command Palette's standard `Microsoft.CmdPal` local settings directory via `Utilities.BaseSettingsPath("Microsoft.CmdPal")`.

## Tech Stack & Build Prerequisites

- **.NET SDK**: .NET 10 (`net10.0-windows10.0.26100.0`), pinned via [`global.json`](./global.json) (`10.0.403`, `rollForward: latestFeature`) to avoid `.NET 11` preview/RC CsWinRT `IIDOptimizer` incompatibilities.
- **Target Platform**: Windows (`win-x64`, project supports `x64;arm64`).
- **Packages** (managed centrally in [`Directory.Packages.props`](./Directory.Packages.props)):
  - `Microsoft.CommandPalette.Extensions` `0.9.260303001`
  - `Microsoft.WindowsAppSDK` `2.2.0`
  - `Microsoft.Windows.CsWin32` `0.3.275`
  - `Shmuelie.WinRTServer` `2.2.1`
  - `Microsoft.NET.Test.Sdk` `18.12.0`, `xunit` `2.9.3`, `xunit.runner.visualstudio` `4.0.1` (`CryptoDock.Tests`)
- **Build Path Redirection**: [`Directory.Build.props`](./Directory.Build.props) redirects local (`CI != true`) `obj`, `bin`, and NuGet restore folders to `C:\tmp\CryptoDockBuildNet10\$(MSBuildProjectName)\` and `C:\tmp\CryptoDockPackages` to avoid long-path and non-ASCII path issues with CsWinRT/MSBuild while letting GitHub Actions CI use standard runner paths.

## Install from GitHub Releases (Prebuilt MSIX)

1. Download the `CryptoDock-v*-win-x64-msix.zip` archive from the repository's **Releases** page and extract it.
2. Open PowerShell inside the extracted folder and run:

   ```powershell
   Get-AppxPackage *CryptoDock* | Remove-AppxPackage
   .\Install.ps1 -Force
   ```

   If Windows does not trust the certificate automatically, import the included `.cer` into `Cert:\CurrentUser\TrustedPeople` before re-running `.\Install.ps1 -Force`.
3. Open **PowerToys Command Palette**, run **Reload Command Palette Extension**, enable the Dock if needed, and pin **Crypto Tickers** to the Dock.

## Build from Source & Run Tests

> **Note:** Local signing uses `CryptoDock\CryptoDock_TemporaryKey.pfx` when present (git-ignored). When that file is absent (such as on fresh clones or CI), builds automatically skip signing unless `-p:AppxPackageSigningEnabled=true` and certificate properties are supplied.

1. Run unit tests (`CryptoDock.Tests`, covering price formatting, symbol parsing/URLs, volatility alert detection, and isolated watchlist persistence):

   ```powershell
   $env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
   dotnet test .\CryptoDock.Tests\CryptoDock.Tests.csproj --nologo --verbosity minimal
   ```

2. Build the solution and generate the local MSIX package:

   ```powershell
   $env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
   dotnet build .\CryptoDock.sln --nologo --verbosity minimal
   ```

3. Trust the test certificate (first time on a machine) and install the MSIX package:

   ```powershell
   cd .\CryptoDock\AppPackages\CryptoDock_1.0.0.0_x64_Debug_Test
   Import-Certificate -FilePath .\CryptoDock_1.0.0.0_x64_Debug.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
   Get-AppxPackage *CryptoDock* | Remove-AppxPackage
   .\Install.ps1 -Force
   ```

## CI & Release Automation

- **[`.github/workflows/ci.yml`](./.github/workflows/ci.yml)**: Runs on pushes/PRs to `main` (and `workflow_dispatch`) on `windows-latest`, restoring packages and verifying both `Release` and `Debug` builds plus all `CryptoDock.Tests` xUnit tests.
- **[`.github/workflows/release.yml`](./.github/workflows/release.yml)**: Runs on version tags (`v*`) or `workflow_dispatch`, runs `CryptoDock.Tests`, stamps the MSIX version in `Package.appxmanifest`, builds and signs the `Release` x64 MSIX package (using `MSIX_CERT_BASE64` / `MSIX_CERT_PASSWORD` secrets if configured, or a generated `CN=CryptoDock` self-signed certificate), uploads the `.zip` artifact, and publishes a GitHub Release on `v*` tags.

## Project Documentation

For architectural details, conventions, and developer/agent context, see [`AGENTS.md`](./AGENTS.md).
