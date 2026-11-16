using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Storage.Streams;
using System;

namespace CryptoDock;

internal sealed partial class CryptoTickerDockItem : ListItem, IDisposable
{
    private readonly CopyTextCommand _copyCommand = new(string.Empty) { Name = "Copy price" };
    private readonly WatchedSymbol _symbol;
    private CryptoTicker? _lastTicker;
    private string? _appliedIconKey;

    public CryptoTickerDockItem(WatchedSymbol symbol, SettingsManager settingsManager)
    {
        _symbol = symbol;
        Command = new NoOpCommand() { Id = $"com.hieuphan.cmdpal.cryptodock.{symbol.Key}" };
        Title = $"{DisplaySymbol} loading";
        Subtitle = _symbol.MarketLabel;
        Icon = new IconInfo("\uE8D7");
        MoreCommands =
        [
            new CommandContextItem(_copyCommand),
            new CommandContextItem(new OpenUrlCommand(_symbol.BinanceUrl) { Name = "Open Binance chart" }),
            new CommandContextItem(new MoveUpCommand(settingsManager, _symbol)),
            new CommandContextItem(new MoveDownCommand(settingsManager, _symbol)),
            new CommandContextItem(new RemoveSymbolCommand(settingsManager, _symbol)),
        ];
    }

    public void Dispose()
    {
    }

    public void Update(CryptoTicker ticker, VolatilityAlert? alert = null)
    {
        _lastTicker = ticker;

        string newTitle = $"{ticker.Pair} {ticker.DirectionArrow} {ticker.PriceText}";
        if (!string.Equals(Title, newTitle, StringComparison.Ordinal))
        {
            Title = newTitle;
        }

        string newSubtitle = alert?.Label ?? $"{ticker.ShortMarketLabel} {ticker.ChangeText}";
        if (!string.Equals(Subtitle, newSubtitle, StringComparison.Ordinal))
        {
            Subtitle = newSubtitle;
        }

        // Determine icon key to check if icon changed
        string iconKey = alert is not null
            ? $"alert:{alert.Icon}"
            : (ticker.HasLogo && !string.IsNullOrEmpty(ticker.LogoUrl) ? $"url:{ticker.LogoUrl}" : $"glyph:{ticker.DirectionIcon}");

        // Only update Icon if the icon source or glyph actually changed.
        // Re-assigning Icon unconditionally causes WinUI to destroy and re-decode the image stream on every tick,
        // which causes the entire dock band to flicker/blink.
        if (!string.Equals(_appliedIconKey, iconKey, StringComparison.Ordinal))
        {
            _appliedIconKey = iconKey;
            Icon = alert is null
                ? CreateLogoOrFallbackIcon(ticker, ticker.DirectionIcon)
                : new IconInfo(alert.Icon);
        }

        if (!string.Equals(_copyCommand.Text, ticker.Summary, StringComparison.Ordinal))
        {
            _copyCommand.Text = ticker.Summary;
        }
    }

    public void MarkOffline()
    {
        string newTitle = _lastTicker is null ? $"{DisplaySymbol} offline" : $"{DisplaySymbol} {_lastTicker.PriceText}";
        if (!string.Equals(Title, newTitle, StringComparison.Ordinal))
        {
            Title = newTitle;
        }

        string newSubtitle = _lastTicker is null ? "Binance unavailable" : $"Last {_lastTicker.UpdatedAt:HH:mm:ss}";
        if (!string.Equals(Subtitle, newSubtitle, StringComparison.Ordinal))
        {
            Subtitle = newSubtitle;
        }

        string summary = _lastTicker?.Summary ?? $"{DisplaySymbol} price unavailable";
        if (!string.Equals(_copyCommand.Text, summary, StringComparison.Ordinal))
        {
            _copyCommand.Text = summary;
        }
    }

    private string DisplaySymbol => _symbol.DisplaySymbol;

    internal static IconInfo CreateLogoOrFallbackIcon(CryptoTicker ticker, string fallbackGlyph)
    {
        if (ticker.HasLogo && !string.IsNullOrEmpty(ticker.LogoUrl))
        {
            try
            {
                var iconData = new IconData(RandomAccessStreamReference.CreateFromUri(new Uri(ticker.LogoUrl)));
                return new IconInfo(iconData);
            }
            catch
            {
                // Fall back to glyph if URI icon stream cannot be constructed
            }
        }

        return new IconInfo(fallbackGlyph);
    }
}
