using System;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>Sanity checks on the <see cref="FtDiagnostics"/> snapshot.</summary>
public class FtDiagnosticsTests
{
    [Fact]
    public void DefaultSnapshot_IsNotRegisteredAndHasEmptyCollections()
    {
        var d = new FtDiagnostics();
        Assert.False(d.Registered);
        Assert.Empty(d.MatchingAssemblies);
        Assert.Empty(d.Notes);
        Assert.Null(d.NeedsTransformationAck);
        Assert.Equal(string.Empty, d.FoundAssembly);
        Assert.Equal(string.Empty, d.CallbackAssemblyFullName);
        Assert.Equal(string.Empty, d.CallbackClass);
        Assert.Equal(string.Empty, d.CallbackMethod);
    }

    [Fact]
    public void Notes_AreAppendable()
    {
        var d = new FtDiagnostics();
        d.Notes.Add("first");
        d.Notes.Add("second");
        Assert.Equal(2, d.Notes.Count);
        Assert.Equal("first", d.Notes[0]);
    }

    [Fact]
    public void Timestamp_IsAssignable()
    {
        var d = new FtDiagnostics { Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(DateTimeKind.Utc, d.Timestamp.Kind);
    }

    [Fact]
    public void LastDiagnostics_StaticFieldIsInitialized()
    {
        Assert.NotNull(PluginEntryPoint.LastDiagnostics);
    }
}
