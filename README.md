# Crypto Dock

Crypto Dock is a PowerToys Command Palette extension that shows realtime Binance spot and futures prices in Command Palette Dock.

Features:

- Dock tickers for watched USDT symbols.
- Binance spot and futures support, including symbols such as `BTCUSDT`, `ETHUSDT`, `XAUUSDT`, and `XAGUSDT`.
- Search Command Palette for Binance USDT symbols and add them to the dock.
- Configurable refresh interval: 10 seconds, 30 seconds, 1 minute, or 5 minutes.
- Configurable search market: Spot + Futures, Spot only, or Futures only.

The extension uses Binance's public REST APIs:

- Spot: `https://api.binance.com/api/v3`
- Futures: `https://fapi.binance.com/fapi/v1`
- Default dock refresh interval: 10 seconds
- Default symbols: `BTCUSDT`, `ETHUSDT`, `BNBUSDT`, `SOLUSDT`, `XRPUSDT`

## Build and test

1. Open `CryptoDock.sln` in Visual Studio 2022.
2. Restore NuGet packages.
3. Deploy the `CryptoDock` project, not only build it.
4. In PowerToys Command Palette, run `Reload Command Palette Extension`.
5. Enable Dock in Command Palette settings.
6. Pin the `Crypto Tickers` band from Dock settings or from the Command Palette context menu.

## Notes

The Command Palette Dock API is still new. This project follows the current Microsoft samples that use `CommandProvider.GetDockBands()` and `WrappedDockItem`.
