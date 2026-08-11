namespace CryptoDock;

internal sealed class VolatilityAlertTracker
{
    private const int HistoryCapacityMinutes = 61;
    private readonly Dictionary<string, PriceHistory> _histories = new(StringComparer.OrdinalIgnoreCase);

    public VolatilityAlert? Update(CryptoTicker ticker, SettingsManager settings)
    {
        string key = new WatchedSymbol(ticker.Market, ticker.Symbol).Key;
        if (!_histories.TryGetValue(key, out PriceHistory? history))
        {
            history = new PriceHistory();
            _histories.Add(key, history);
        }

        history.Add(ticker.LastPrice, ticker.UpdatedAt);
        return history.CreateAlert(settings);
    }

    public void KeepSymbols(IEnumerable<string> symbolKeys)
    {
        HashSet<string> activeKeys = symbolKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _histories.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            _histories.Remove(key);
        }
    }

    public void Clear() => _histories.Clear();

    private sealed class PriceHistory
    {
        private readonly List<MinuteBar> _bars = new(HistoryCapacityMinutes);

        public void Add(decimal price, DateTimeOffset timestamp)
        {
            DateTimeOffset minute = new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, 0, timestamp.Offset);
            if (_bars.Count == 0 || _bars[^1].Minute != minute)
            {
                _bars.Add(new MinuteBar(minute, price, price, price, price));
                if (_bars.Count > HistoryCapacityMinutes)
                {
                    _bars.RemoveAt(0);
                }

                return;
            }

            MinuteBar current = _bars[^1];
            _bars[^1] = current with
            {
                High = Math.Max(current.High, price),
                Low = Math.Min(current.Low, price),
                Close = price,
            };
        }

        public VolatilityAlert? CreateAlert(SettingsManager settings)
        {
            if (_bars.Count == 0)
            {
                return null;
            }

            int windowMinutes = Math.Clamp(settings.VolatilityWindowMinutes, 1, HistoryCapacityMinutes - 1);
            DateTimeOffset windowStart = _bars[^1].Minute.AddMinutes(-windowMinutes);
            if (_bars[0].Minute > windowStart)
            {
                return null;
            }

            MinuteBar[] window = _bars.Where(bar => bar.Minute >= windowStart).ToArray();
            decimal current = window[^1].Close;
            decimal baseline = window[0].Open;
            decimal high = window.Max(bar => bar.High);
            decimal low = window.Min(bar => bar.Low);
            if (current <= 0 || baseline <= 0 || high <= 0 || low <= 0)
            {
                return null;
            }

            decimal netChangePercent = Percentage(current - baseline, baseline);
            decimal dumpPercent = Percentage(high - current, high);
            decimal reboundPercent = Percentage(current - low, low);
            decimal rangePercent = Percentage(high - low, low);
            bool dump = dumpPercent >= settings.DumpThresholdPercent;
            bool rebound = reboundPercent >= settings.ReboundThresholdPercent;
            bool netDrop = netChangePercent <= -settings.DumpThresholdPercent;
            bool netRise = netChangePercent >= settings.ReboundThresholdPercent;
            bool volatileRange = rangePercent >= settings.RangeThresholdPercent;

            if (dump && rebound)
            {
                return new VolatilityAlert(VolatilityAlertKind.Volatile, rangePercent, windowMinutes);
            }

            if (dump || netDrop)
            {
                return new VolatilityAlert(VolatilityAlertKind.Dump, Math.Max(dumpPercent, Math.Abs(netChangePercent)), windowMinutes);
            }

            if (rebound || netRise)
            {
                return new VolatilityAlert(VolatilityAlertKind.Rebound, Math.Max(reboundPercent, netChangePercent), windowMinutes);
            }

            return volatileRange
                ? new VolatilityAlert(VolatilityAlertKind.Volatile, rangePercent, windowMinutes)
                : null;
        }

        private static decimal Percentage(decimal change, decimal baseline) => change / baseline * 100m;
    }

    private readonly record struct MinuteBar(DateTimeOffset Minute, decimal Open, decimal High, decimal Low, decimal Close);
}

internal enum VolatilityAlertKind
{
    Dump,
    Rebound,
    Volatile,
}

internal sealed record VolatilityAlert(VolatilityAlertKind Kind, decimal Percent, int WindowMinutes)
{
    public string Label => Kind switch
    {
        VolatilityAlertKind.Dump => $"DUMP -{Percent:0.##}%/{WindowLabel}",
        VolatilityAlertKind.Rebound => $"REBOUND +{Percent:0.##}%/{WindowLabel}",
        _ => $"VOLATILE {Percent:0.##}%/{WindowLabel}",
    };

    public string Icon => Kind switch
    {
        VolatilityAlertKind.Dump => "\uE74B",
        VolatilityAlertKind.Rebound => "\uE74A",
        _ => "\uE7C4",
    };

    private string WindowLabel => WindowMinutes >= 60 ? $"{WindowMinutes / 60}h" : $"{WindowMinutes}m";
}
