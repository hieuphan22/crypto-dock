using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Storage.Streams;
using System;

namespace CryptoDock;

internal sealed partial class CryptoTickerDockItem : ListItem, IDisposable
{
    private readonly CopyTextCommand _copyCommand = new(string.Empty) { Name = "Copy price" };
    private readonly WatchedSymbol _symbol;
    private CryptoTicker? _lastTicker;

    public CryptoTickerDockItem(WatchedSymbol symbol)
    {
        _symbol = symbol;
        Command = new NoOpCommand() { Id = $"com.hieuphan.cmdpal.cryptodock.{symbol.Key}" };
        Title = $"{DisplaySymbol} loading";
        Subtitle = _symbol.MarketLabel;
        Icon = new IconInfo("\uE8D7");
        MoreCommands =
        [
            new CommandContextItem(_copyCommand),
            new CommandContextItem(new OpenUrlCommand(BinanceUrl(_symbol)) { Name = "Open Binance chart" }),
        ];
    }

    public void Dispose()
    {
    }

    public void Update(CryptoTicker ticker, VolatilityAlert? alert = null)
    {
        _lastTicker = ticker;
        Title = $"{ticker.Pair} {ticker.DirectionArrow} {ticker.PriceText}";
        Subtitle = alert?.Label ?? $"{ticker.ShortMarketLabel} {ticker.ChangeText}";
        
        if (ticker.HasLogo && !string.IsNullOrEmpty(ticker.LogoUrl))
        {
            try
            {
                var iconData = new IconData(RandomAccessStreamReference.CreateFromUri(new Uri(ticker.LogoUrl)));
                Icon = new IconInfo(iconData);
            }
            catch
            {
                Icon = new IconInfo(alert?.Icon ?? ticker.DirectionIcon);
            }
        }
        else
        {
            Icon = new IconInfo(alert?.Icon ?? ticker.DirectionIcon);
        }

        _copyCommand.Text = ticker.Summary;
    }

    public void MarkOffline()
    {
        Title = _lastTicker is null ? $"{DisplaySymbol} offline" : $"{DisplaySymbol} {_lastTicker.PriceText}";
        Subtitle = _lastTicker is null ? "Binance unavailable" : $"Last {_lastTicker.UpdatedAt:HH:mm:ss}";
        _copyCommand.Text = _lastTicker?.Summary ?? $"{DisplaySymbol} price unavailable";
    }

    private string DisplaySymbol => _symbol.DisplaySymbol;

    private static string BinanceUrl(WatchedSymbol symbol)
    {
        return symbol.Market == MarketKind.Futures
            ? $"https://www.binance.com/vi/futures/{symbol.Symbol}"
            : $"https://www.binance.com/vi/trade/{symbol.Symbol.Replace("USDT", "_USDT", StringComparison.OrdinalIgnoreCase)}";
    }
}
