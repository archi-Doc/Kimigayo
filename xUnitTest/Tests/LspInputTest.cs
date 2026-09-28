// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class LspInputTest
{
    [Fact]
    public void AdoptionDoesNotMutateTheWorkersRecordedInputs()
    {
        var key = InputKey.File(Path.Combine(Path.GetTempPath(), "kimi-recorded.kimi"));
        var state = new InputState { Content = SourceContent.FromText("text", false) };
        RecordedInput[] observed = [new(key, 0, state)];
        var item = new DiscoveryRecord { Inputs = observed };
        var store = new InputStore();
        Assert.True(store.TryRegister(item, store.Begin()));
        Assert.Equal(0, observed[0].Revision);
        Assert.Same(state, observed[0].FirstRead);
        Assert.NotSame(observed, item.Inputs);
        Assert.Equal(store.Find(key)!.Revision, item.Inputs[0].Revision);
        Assert.Null(item.Inputs[0].FirstRead);

        var reused = new DiscoveryRecord { Inputs = item.Inputs };
        Assert.True(store.TryRegister(reused, store.Begin()));
        Assert.Same(item.Inputs, reused.Inputs); // Committed records need no copy.
    }

    [Fact]
    public void AnExistingOverlayReusesTheDiskListing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-listing");
        var file = Path.Combine(directory, "File.kimi");
        var key = InputKey.Listing(directory, InputKey.SourcePattern);
        string[] names = [file];
        var disk = new InputState { Names = names, DiskNames = names };
        Assert.Same(disk, DiskReader.MergeListing(key, disk, [file, Path.Combine(directory, "Other.txt")]));
        if (OperatingSystem.IsWindows())
        {
            Assert.Same(disk, DiskReader.MergeListing(key, disk, [file.ToUpperInvariant()]));
        }
    }

    [Fact]
    public void NewOverlaysAreDistinctSortedAndRemovedOnClose()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-listing");
        var key = InputKey.Listing(directory, InputKey.SourcePattern);
        var paths = new[] { "A.kimi", "B.kimi", "C.kimi", "D.kimi" }.Select(x => Path.Combine(directory, x)).ToArray();
        string[] names = [paths[1], paths[3]];
        var disk = new InputState { Names = names, DiskNames = names, Stamp = 42 };
        var merged = DiskReader.MergeListing(key, disk, [paths[2], paths[0], paths[1], paths[2]]);
        Assert.Equal(paths, merged.Names);
        Assert.Same(names, merged.DiskNames);
        Assert.Equal(42, merged.Stamp);

        var closed = DiskReader.MergeListing(key, merged, []);
        Assert.Same(names, closed.Names);
        Assert.Equal(paths, merged.Names); // Earlier snapshots stay immutable.
    }

    [Fact]
    public void PlacementSortsDeduplicatesAndKeepsCheckedSourcesWithoutDiagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-place");
        var clean = SourceIdentity.FromPath(Path.Combine(directory, "Clean.kimi"));
        var broken = SourceIdentity.FromPath(Path.Combine(directory, "Broken.kimi"));
        var project = SourceIdentity.FromPath(Path.Combine(directory, "App.kimiproj"));
        var builtIn = SourceIdentity.FromPath(SourceIdentity.BuiltInPrefix + "Kimi/Core.kimi");
        var later = new SourceRange(new(2, 0), new(2, 1));
        CheckDiagnostic[] diagnostics =
        [
            new("B_Kd", DiagnosticSeverity.Error, "second", broken, later),
            new("A_Kd", DiagnosticSeverity.Error, "first", broken, new SourceRange(new(0, 0), new(0, 1))),
            new("B_Kd", DiagnosticSeverity.Error, "second", broken, later),
            new("D_Kd", DiagnosticSeverity.Warning, "built-in", builtIn, later),
            new("C_Kd", DiagnosticSeverity.Error, "unlocated", default, null),
        ];
        var output = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, diagnostics);

        var reports = WorkspaceCheck.Place(output, [clean, broken, clean, builtIn], project);
        Assert.Equal(3, reports.Count);
        Assert.Empty(reports[clean]);
        Assert.Equal(["first", "second"], reports[broken].Select(static x => x.Message));
        Assert.Equal(["unlocated", "built-in"], reports[project].Select(static x => x.Message));
        Assert.All(reports[project], static x => Assert.Equal(default, x.Range));
        Assert.Same(reports[clean], WorkspaceCheck.Place(new(CheckOutcome.Completed, true, TestPresence.No, []), [clean], project)[clean]);
    }

    [Fact]
    public void AnUnreadableListingStaysUnestablished()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-listing");
        var disk = InputState.Unestablished("unreadable");
        Assert.Same(disk, DiskReader.MergeListing(InputKey.Listing(directory, InputKey.SourcePattern), disk, [Path.Combine(directory, "A.kimi")]));
    }
}
