// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class OwnershipStorageLimitTest
{
    // R6-check H10a D10 (restored coverage; T-04 N3): the MIR's own arena limit publishes end to end. MirAnalysisTest's A5 program, 2,000
    // reference locals live across 2,000 branches, fits A1's arena but not A5's Loans table, so A5 records one OwnershipStorageLimit_Kd at
    // `func f` with its arguments and stops there (its Loan conflict at `x = 2` is not judged); an independent error elsewhere publishes
    // beside it, and the program is refused and generates nothing (R4-M15a, R4-M132, R5-M185).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMirsArenaLimitPublishesBesideIndependentErrors(bool independent)
    {
        var source = MirAnalysisTest.ArenaWorkload(2_000, MirAnalysisTest.References) +
            (independent ? "func invalid()\n    let value = 1\n    let moved = value@move\n    let invalid = value\n" : string.Empty) + "public func main() => ()\n";
        var path = Path.GetFullPath("ownership-storage-mir.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        using var module = new StringWriter();
        Assert.False(c.Generate(module));
        Assert.Empty(module.ToString());

        c.ReportAnalysis();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var limit = (nameof(DiagnosticCode.OwnershipStorageLimit_Kd), source.IndexOf("f(flag: bool)", StringComparison.Ordinal)); // At its name.
        var moved = (nameof(DiagnosticCode.MovedPlace_Kd), source.LastIndexOf("value", StringComparison.Ordinal));
        Assert.Equal(independent ? [moved, limit] : [limit], result.Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start)).Order());
        var reason = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.OwnershipStorageLimit_Kd)).Reason!;
        Assert.Equal(["requiredBytes", "limitBytes", "table"], reason.Select(static x => x.Name));
        Assert.Equal(("67108864", "Loans"), (reason[1].Value, reason[2].Value)); // 64 MiB, MirDataflow's ByteLimit.
        Assert.True(long.Parse(reason[0].Value!, CultureInfo.InvariantCulture) > 64L * 1024 * 1024, reason[0].Value);
    }
}
