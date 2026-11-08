// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// IMPL A.4 (DECISIONS U-D2's entry; the owner's O-80 (b)): no Type higher than H = 8,192 is formed. A Type that would pass it is one
// Resource record TypeHeightLimit_Kd at the site whose Type it would be, never a Language record or a crash; a node reading the refused
// one rests on it, and an independent record stays. `D_n<T>.Item` doubles per declaration (height 2^n, no expression), so D13 is
// exactly H. Fail-before: dev accepts the H + 1 forms, reports `InvalidAssociatedType_Kd` at D14's conformance, and crashes at D15.
public class TypeHeightLimitTest(ITestOutputHelper output)
{
    private const string Main = "public func main() => ()\n";

    [Fact]
    public void AWrittenTypeAtTheLimitIsKeptAndOneLayerMoreIsRecordedAtIt()
    {
        // The instance collector caps a type-driven key's height at 128 (R5-M170): a Resource record of its own, not this limit's.
        var kept = this.Compile(Doubling(13) + "func p(x: D13<i32>.Item) => ()\n" + Main);
        Assert.Equal([nameof(DiagnosticCode.GenerationResourceLimit_Kd)], kept.Records.Select(static x => x.Code));

        var source = Doubling(13) + "func p(x: ref/D13<i32>.Item) => ()\n" + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "ref/D13<i32>.Item");
    }

    [Fact]
    public void AnExpressionPastTheLimitIsRecordedAtItsNode()
    {
        // A local of a Type this deep may still meet the instance collector's own Resource limits; no height record and no Language one.
        var at = this.Compile(Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = x@move\n    return\n" + Main);
        Assert.All(at.Records, static x => Assert.Equal(DiagnosticCategory.Resource, x.Category));
        Assert.DoesNotContain(at.Records, static x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd));

        var source = Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = (x@move, 1)\n    return\n" + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "(x@move, 1)");
    }

    // The declaration phase's site: D14's associated Type would have height 16,384 when its conformance is prepared.
    [Fact]
    public void AnAssociatedTypePastTheLimitIsRecordedAtItsConformance()
    {
        var source = Doubling(14) + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "Self is HasItem", source.IndexOf("struct D14", StringComparison.Ordinal));

        // A use of the refused associated Type is its own formation past the limit, recorded at its written Type.
        var use = Doubling(14) + "func p(x: ref/D14<i32>.Item) => ()\n" + Main;
        var records = this.Compile(use).Records;
        Assert.All(records, static x => Assert.Equal(nameof(DiagnosticCode.TypeHeightLimit_Kd), x.Code));
        Assert.Equal(2, records.Length);
    }

    // An invalid twin keeps its independent record (q) beside the height record; the mismatch that reads the refused `y` rests on it.
    // Under the limit both mismatches are reported (a shallow twin: the adapter's Type text of a deep Type is outside this unit).
    [Fact]
    public void TheIndependentRecordStaysAndTheDependentOneRests()
    {
        const string q = "func q() -> ()\n    let z: i32 = true\n    return\n";
        var under = this.Compile(Doubling(4) + "func p(x: D4<i32>.Item) -> ()\n    let y = x@move\n    let b: i64 = y@move\n    return\n" + q + Main);
        Assert.Equal([nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], under.Records.Select(static x => x.Code));

        var source = Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = (x@move, 1)\n    let b: i64 = y@move\n    return\n" + q + Main;
        var over = this.Compile(source).Records;
        Assert.Equal([nameof(DiagnosticCode.TypeHeightLimit_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], over.Select(static x => x.Code));
        AssertHeightRecord(over[0], source, "(x@move, 1)");
        Assert.True(over[1].Span.Start > source.IndexOf("func q", StringComparison.Ordinal));
    }

    // D0's Item is (T, i32); each D_i's is D_{i-1}<D_{i-1}<T>.Item>.Item, so D_n<i32>.Item has height 2^n.
    private static string Doubling(int n)
    {
        var s = "contract HasItem\n    associate Item\nstruct D0<T>\n    Self is HasItem\n    associate HasItem.Item is (T, i32)\n";
        for (var i = 1; i <= n; i++)
        {
            s += $"struct D{i}<T>\n    Self is HasItem\n    associate HasItem.Item is D{i - 1}<D{i - 1}<T>.Item>.Item\n";
        }

        return s;
    }

    private static void AssertHeightRecord(DiagnosticEntryView record, string source, string at, int from = 0)
    {
        Assert.Equal((nameof(DiagnosticCode.TypeHeightLimit_Kd), DiagnosticCategory.Resource), (record.Code, record.Category));
        Assert.Contains("8192", record.Message, StringComparison.Ordinal);
        var start = source.IndexOf(at, from, StringComparison.Ordinal);
        Assert.InRange(record.Span.Start, start, start + at.Length - 1);
    }

    private (DiagnosticEntryView[] Records, bool Accepted) Compile(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Deep.kimi", source));
        var checkedAll = c.Bind().IsComplete && c.Binding.CheckStartup(OutputKind.Application).IsComplete && c.Analyze().IsVerified;
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var accepted = checkedAll && c.Accepts;
        var records = c.Diagnostics.Finalize(rejected: !accepted).Diagnostics.Select(static x => new DiagnosticEntryView(x.Code, x.Category, x.Message, x.Span ?? default)).ToArray();
        output.WriteLine(string.Join(", ", records.Select(static x => $"{x.Code} {x.Span}")));
        return (records, accepted);
    }

    private readonly record struct DiagnosticEntryView(string Code, DiagnosticCategory Category, string Message, SourceSpan Span);
}
