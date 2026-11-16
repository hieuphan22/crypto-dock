using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class CryptoDockBand : WrappedDockItem, IDisposable
{
    private readonly CryptoTickerService _tickerService;
    private readonly SettingsManager _settingsManager;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private readonly VolatilityAlertTracker _volatilityAlerts = new();
    private readonly object _itemsLock = new();
    private Dictionary<string, CryptoTickerDockItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private int _consecutiveFailures;
    private volatile bool _disposed;

    public CryptoDockBand(CryptoTickerService tickerService, SettingsManager settingsManager)
        : base([], "com.hieuphan.cmdpal.cryptodock.band", "Crypto Tickers")
    {
        _tickerService = tickerService;
        _settingsManager = settingsManager;
        Icon = new IconInfo("\uE8D7");

        RebuildItems();
        _settingsManager.SymbolsChanged += OnSymbolsChanged;
        _settingsManager.SettingsChanged += OnSettingsChanged;
        _ = RunRefreshLoopAsync(_cancellation.Token);
    }

    public void Dispose()
    {
        CryptoTickerDockItem[] itemsToDispose;
        lock (_itemsLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            itemsToDispose = _items.Values.ToArray();
            _items = new Dictionary<string, CryptoTickerDockItem>(StringComparer.OrdinalIgnoreCase);
        }

        _settingsManager.SymbolsChanged -= OnSymbolsChanged;
        _settingsManager.SettingsChanged -= OnSettingsChanged;
        _cancellation.Cancel();

        foreach (CryptoTickerDockItem item in itemsToDispose)
        {
            item.Dispose();
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshAsync(cancellationToken);
                await _refreshSignal.WaitAsync(_settingsManager.RefreshInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _refreshLock.Dispose();
            _refreshSignal.Dispose();
            _cancellation.Dispose();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return;
        }

        try
        {
            IReadOnlyList<CryptoTicker> tickers = await _tickerService.GetTickersAsync(
                _settingsManager.Symbols,
                cancellationToken);

            _consecutiveFailures = 0;

            foreach (CryptoTicker ticker in tickers)
            {
                string key = new WatchedSymbol(ticker.Market, ticker.Symbol).Key;
                CryptoTickerDockItem? item;
                lock (_itemsLock)
                {
                    _items.TryGetValue(key, out item);
                }

                if (item is not null)
                {
                    VolatilityAlert? alert = _settingsManager.VolatilityAlertsEnabled
                        ? _volatilityAlerts.Update(ticker, _settingsManager)
                        : null;
                    item.Update(ticker, alert);
                }
            }

            if (!_settingsManager.VolatilityAlertsEnabled)
            {
                _volatilityAlerts.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _consecutiveFailures++;
            // Only mark offline after 3 consecutive failures (prevent flickering on momentary network blips)
            if (_consecutiveFailures >= 3)
            {
                CryptoTickerDockItem[] snapshot;
                lock (_itemsLock)
                {
                    snapshot = _items.Values.ToArray();
                }

                foreach (CryptoTickerDockItem item in snapshot)
                {
                    item.MarkOffline();
                }
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void OnSymbolsChanged(object? sender, EventArgs e)
    {
        RebuildItems();
        SignalRefresh();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        SignalRefresh();
    }

    private void RebuildItems()
    {
        WatchedSymbol[] symbols = _settingsManager.Symbols.ToArray();
        List<CryptoTickerDockItem> removedItems = [];
        List<CryptoTickerDockItem> orderedItems = new(symbols.Length);
        Dictionary<string, CryptoTickerDockItem> nextItems = new(symbols.Length, StringComparer.OrdinalIgnoreCase);

        lock (_itemsLock)
        {
            if (_disposed)
            {
                return;
            }

            foreach (WatchedSymbol symbol in symbols)
            {
                if (!nextItems.ContainsKey(symbol.Key))
                {
                    CryptoTickerDockItem item = _items.TryGetValue(symbol.Key, out CryptoTickerDockItem? existing)
                        ? existing
                        : new CryptoTickerDockItem(symbol, _settingsManager);
                    nextItems[symbol.Key] = item;
                    orderedItems.Add(item);
                }
            }

            foreach ((string key, CryptoTickerDockItem oldItem) in _items)
            {
                if (!nextItems.ContainsKey(key))
                {
                    removedItems.Add(oldItem);
                }
            }

            _items = nextItems;
        }

        foreach (CryptoTickerDockItem removed in removedItems)
        {
            removed.Dispose();
        }

        Items = orderedItems.ToArray();
        _volatilityAlerts.KeepSymbols(nextItems.Keys);
    }

    private void SignalRefresh()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_cancellation.IsCancellationRequested && _refreshSignal.CurrentCount == 0)
            {
                _refreshSignal.Release();
            }
        }
        catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
        {
        }
    }
}
