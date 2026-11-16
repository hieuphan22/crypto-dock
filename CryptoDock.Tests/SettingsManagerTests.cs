using System;
using System.IO;
using Xunit;

namespace CryptoDock.Tests;

public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CryptoDockTests_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Constructor_InitializesDefaultSymbolsInIsolatedDirectory()
    {
        var manager = new SettingsManager(_tempDir);

        Assert.Equal(
            [WatchedSymbol.Spot("BTCUSDT"), WatchedSymbol.Spot("ETHUSDT")],
            manager.Symbols);
        Assert.True(File.Exists(Path.Combine(_tempDir, "cryptoDock.symbols.txt")));
    }

    [Fact]
    public void WatchlistOperations_AddMoveRemovePersistAndRaiseEvents()
    {
        var manager = new SettingsManager(_tempDir);
        int events = 0;
        manager.SymbolsChanged += (_, _) => events++;

        Assert.True(manager.AddSymbol(WatchedSymbol.Futures("sol/usdt")));
        Assert.False(manager.AddSymbol(WatchedSymbol.Futures("SOLUSDT")));
        Assert.Equal(1, events);

        Assert.False(manager.MoveSymbolUp(WatchedSymbol.Spot("BTCUSDT")));
        Assert.False(manager.MoveSymbolDown(WatchedSymbol.Futures("SOLUSDT")));
        Assert.Equal(1, events);

        Assert.True(manager.MoveSymbolUp(WatchedSymbol.Futures("SOLUSDT")));
        Assert.Equal(2, events);
        Assert.Equal(
            [WatchedSymbol.Spot("BTCUSDT"), WatchedSymbol.Futures("SOLUSDT"), WatchedSymbol.Spot("ETHUSDT")],
            manager.Symbols);

        Assert.True(manager.MoveSymbolDown(WatchedSymbol.Spot("BTCUSDT")));
        Assert.Equal(3, events);
        Assert.Equal(
            [WatchedSymbol.Futures("SOLUSDT"), WatchedSymbol.Spot("BTCUSDT"), WatchedSymbol.Spot("ETHUSDT")],
            manager.Symbols);

        Assert.True(manager.RemoveSymbol(WatchedSymbol.Spot("ETHUSDT")));
        Assert.False(manager.RemoveSymbol(WatchedSymbol.Spot("ETHUSDT")));
        Assert.Equal(4, events);

        var reloaded = new SettingsManager(_tempDir);
        Assert.Equal(
            [WatchedSymbol.Futures("SOLUSDT"), WatchedSymbol.Spot("BTCUSDT")],
            reloaded.Symbols);
    }

    [Fact]
    public void Constructor_MigratesLegacyPlainSymbolsAndDeduplicates()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllLines(
            Path.Combine(_tempDir, "cryptoDock.symbols.txt"),
            ["adausdt", "spot:ADAUSDT", "futures:pepe/usdt", "", "   "]);

        var manager = new SettingsManager(_tempDir);

        Assert.Equal(
            [WatchedSymbol.Spot("ADAUSDT"), WatchedSymbol.Futures("PEPEUSDT")],
            manager.Symbols);
    }
}
