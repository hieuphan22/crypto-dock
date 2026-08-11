# Crypto Dock

Crypto Dock is a PowerToys Command Palette extension that shows realtime Binance spot and futures prices in Command Palette Dock.

Features:

- Dock tickers for watched USDT symbols.
- Binance spot and futures support, including symbols such as `BTCUSDT`, `ETHUSDT`, `XAUUSDT`, and `XAGUSDT`.
- Search Command Palette for Binance USDT symbols and add them to the dock.
- Configurable refresh interval: 10 seconds, 30 seconds, 1 minute, or 5 minutes.
- Configurable search market: Spot + Futures, Spot only, or Futures only.
- Optional rolling volatility alerts for sudden dump, rebound, or high-low range moves.

The extension uses Binance's public REST APIs:

- Spot: `https://api.binance.com/api/v3`
- Futures: `https://fapi.binance.com/fapi/v1`
- Default dock refresh interval: 10 seconds
- Default symbols: `BTCUSDT`, `ETHUSDT`, `BNBUSDT`, `SOLUSDT`, `XRPUSDT`

## Volatility alerts

Volatility alerts are disabled by default. When enabled in settings, Crypto Dock keeps a bounded in-memory OHLC history per watched symbol and can replace the normal dock subtitle with an alert such as `DUMP -5%/15m`, `REBOUND +5%/15m`, or `VOLATILE 10%/1h`.

The history is aggregated by minute. With a 10-second or 30-second refresh interval, all samples inside the current minute update that minute's high, low, and close, so short intra-minute spikes can still be represented. With a 1-minute or 5-minute refresh interval, alerts are based on the prices actually observed at that interval.

Settings include the analysis window, dump threshold, rebound threshold, and high-low range threshold. History is kept only while alerts are enabled and is cleared when alerts are turned off.

## Build and test

1. Open `CryptoDock.sln` in Visual Studio 2022.
2. Restore NuGet packages.
3. Deploy the `CryptoDock` project, not only build it.
4. In PowerToys Command Palette, run `Reload Command Palette Extension`.
5. Enable Dock in Command Palette settings.
6. Pin the `Crypto Tickers` band from Dock settings or from the Command Palette context menu.

## Notes

The Command Palette Dock API is still new. This project follows the current Microsoft samples that use `CommandProvider.GetDockBands()` and `WrappedDockItem`.
