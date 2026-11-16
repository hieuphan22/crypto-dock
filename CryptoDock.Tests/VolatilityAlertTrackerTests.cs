using Xunit;

namespace CryptoDock.Tests;

public sealed class VolatilityAlertTrackerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private static CryptoTicker Ticker(string symbol, decimal price, int minuteOffset, MarketKind market = MarketKind.Spot) =>
        new(market, symbol, price, 0m, price, price, 100m, BaseTime.AddMinutes(minuteOffset));

    [Fact]
    public void Update_ReturnsNullWhenWithinThresholds()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(15, 3m, 3m, 5m);

        Assert.Null(tracker.Update(Ticker("BTCUSDT", 100m, 0), thresholds));
        Assert.Null(tracker.Update(Ticker("BTCUSDT", 101m, 1), thresholds));
        Assert.Null(tracker.Update(Ticker("BTCUSDT", 99m, 2), thresholds));
    }

    [Fact]
    public void Update_TriggersDumpWhenPriceFallsFromWindowHigh()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(15, 3m, 3m, 5m);

        tracker.Update(Ticker("BTCUSDT", 100m, 0), thresholds);
        tracker.Update(Ticker("BTCUSDT", 102m, 2), thresholds);
        VolatilityAlert? alert = tracker.Update(Ticker("BTCUSDT", 96m, 5), thresholds);

        Assert.NotNull(alert);
        Assert.Equal(VolatilityAlertKind.Dump, alert.Kind);
        Assert.Equal("\uE74B", alert.Icon);
        Assert.StartsWith("🔴 DUMP -", alert.Label);
        Assert.EndsWith("/5m", alert.Label);
    }

    [Fact]
    public void Update_TriggersPumpWhenPriceRisesFromWindowLow()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(15, 3m, 3m, 5m);

        tracker.Update(Ticker("BTCUSDT", 100m, 0), thresholds);
        VolatilityAlert? alert = tracker.Update(Ticker("BTCUSDT", 104m, 3), thresholds);

        Assert.NotNull(alert);
        Assert.Equal(VolatilityAlertKind.Pump, alert.Kind);
        Assert.Equal("\uE74A", alert.Icon);
        Assert.StartsWith("🟢 PUMP +4%", alert.Label);
        Assert.EndsWith("/3m", alert.Label);
    }

    [Fact]
    public void Update_TriggersSwingWhenBothDumpAndReboundOrWideRangeOccur()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(15, 3m, 3m, 5m);

        tracker.Update(Ticker("BTCUSDT", 100m, 0), thresholds);
        tracker.Update(Ticker("BTCUSDT", 105m, 1), thresholds);
        tracker.Update(Ticker("BTCUSDT", 95m, 2), thresholds);
        VolatilityAlert? swing = tracker.Update(Ticker("BTCUSDT", 100m, 3), thresholds);

        Assert.NotNull(swing);
        Assert.Equal(VolatilityAlertKind.Swing, swing.Kind);
        Assert.Equal("\uE7C4", swing.Icon);
        Assert.StartsWith("⚡ SWING ", swing.Label);
    }

    [Fact]
    public void Update_TriggersSwingFromRangeThresholdWithoutDumpOrPump()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(15, 10m, 10m, 5m);

        tracker.Update(Ticker("BTCUSDT", 100m, 0), thresholds);
        tracker.Update(Ticker("BTCUSDT", 106m, 1), thresholds);
        VolatilityAlert? alert = tracker.Update(Ticker("BTCUSDT", 102m, 2), thresholds);

        Assert.NotNull(alert);
        Assert.Equal(VolatilityAlertKind.Swing, alert.Kind);
    }

    [Fact]
    public void Update_ExcludesBarsOutsideRollingWindowAndSeparatesMarkets()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(5, 3m, 3m, 5m);

        tracker.Update(Ticker("BTCUSDT", 110m, 0, MarketKind.Spot), thresholds);
        tracker.Update(Ticker("BTCUSDT", 100m, 10, MarketKind.Spot), thresholds);
        VolatilityAlert? spotAlert = tracker.Update(Ticker("BTCUSDT", 100m, 11, MarketKind.Spot), thresholds);

        Assert.Null(spotAlert);

        tracker.Update(Ticker("BTCUSDT", 100m, 10, MarketKind.Futures), thresholds);
        VolatilityAlert? futuresAlert = tracker.Update(Ticker("BTCUSDT", 105m, 11, MarketKind.Futures), thresholds);

        Assert.NotNull(futuresAlert);
        Assert.Equal(VolatilityAlertKind.Pump, futuresAlert.Kind);
    }

    [Fact]
    public void KeepSymbolsAndClear_PruneTrackedHistories()
    {
        var tracker = new VolatilityAlertTracker();
        var thresholds = new VolatilityAlertThresholds(15, 3m, 3m, 5m);

        tracker.Update(Ticker("BTCUSDT", 100m, 0, MarketKind.Spot), thresholds);
        tracker.Update(Ticker("ETHUSDT", 100m, 0, MarketKind.Spot), thresholds);

        tracker.KeepSymbols(["spot:BTCUSDT"]);
        Assert.Null(tracker.Update(Ticker("ETHUSDT", 110m, 1, MarketKind.Spot), thresholds));

        VolatilityAlert? btcAlert = tracker.Update(Ticker("BTCUSDT", 105m, 1, MarketKind.Spot), thresholds);
        Assert.NotNull(btcAlert);

        tracker.Clear();
        Assert.Null(tracker.Update(Ticker("BTCUSDT", 120m, 2, MarketKind.Spot), thresholds));
    }
}
