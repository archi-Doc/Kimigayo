// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// IMPL A.4 (DECISIONS U-D2's entry; the owner's O-80 (b)): no Type higher than H = 8,192 is formed. A Type that would pass it is one
// Resource record TypeHeightLimit_Kd at the site whose Type it would be, never a Language record or a crash; a node reading the refused
// one rests on it, and an independent record stays. `D_n<T>.Item` doubles per declaration (height 2^n, no expression), so D13 is
// exactly H. Fail-before: dev accepts the H + 1 forms, crashes its 64 MB stack at D15, and the first head (64d0a22d8) recorded
// `InvalidConstraint_Kd` (Language) where D15's associated Type reads D14's refused one.
public class TypeHeightLimitTest(ITestOutputHelper output)
{
    private const string Main = "public func main() => ()\n";

    private const string IndependentMismatch = "func q() -> ()\n    let z: i32 = true\n    return\n";

    // A requirement over the associated Type, and W, whose associated Type is D13<i32>.Item (= H); W's members follow.
    private const string Pair = "contract HasPair\n    associate Item\n    func take(self: ref/Self, x: (Self.Item, i32))\n" +
        "struct W\n    Self is HasPair\n    associate HasPair.Item is D13<i32>.Item\n";

    public static TheoryData<int> Chains => [15, 16, 17];

    // SPEC 23.1.2, 8.3.3 (Limits): every check that reads an associated Type the limit refused rests on its conformance's record, so a
    // chain past the limit (D15 over D14, and so on) gives only height records, with a use or without one.
    [Theory]
    [MemberData(nameof(Chains))]
    public void AChainPastTheLimitGivesOnlyHeightRecords(int n)
    {
        Assert.All(this.Compile(Doubling(n) + $"func p(x: ref/D{n}<i32>.Item) => ()\n" + Main).Records, AssertHeightCode);
        Assert.All(this.Compile(Doubling(n) + Main).Records, AssertHeightCode);
    }

    // The invalid twins past the limit (a written parameter Type refused: gap 2 for a written Type) keep q's independent mismatch; p's
    // mismatch reads the refused parameter and rests.
    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void AnInvalidTwinPastTheLimitKeepsOnlyItsIndependentRecord(int n)
    {
        var source = Doubling(n) + $"func p(x: D{n}<i32>.Item) -> ()\n    let y: i64 = x@move\n    return\n" + IndependentMismatch + Main;
        var records = this.Compile(source).Records;
        var mismatch = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.TypeHeightLimit_Kd));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.True(mismatch.Span.Start > source.IndexOf("func q", StringComparison.Ordinal));
    }

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
        Assert.All(records, AssertHeightCode);
        Assert.True(records.Length >= 2, "the conformance's record and the use's own");
    }

    // An invalid twin keeps its independent record (q) beside the height record; the mismatch that reads the refused `y` rests on it.
    // Under the limit both mismatches are reported (a shallow twin: the adapter's Type text of a deep Type is outside this unit).
    [Fact]
    public void TheIndependentRecordStaysAndTheDependentOneRests()
    {
        const string q = IndependentMismatch;
        var under = this.Compile(Doubling(4) + "func p(x: D4<i32>.Item) -> ()\n    let y = x@move\n    let b: i64 = y@move\n    return\n" + q + Main);
        Assert.Equal([nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], under.Records.Select(static x => x.Code));

        var source = Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = (x@move, 1)\n    let b: i64 = y@move\n    return\n" + q + Main;
        var over = this.Compile(source).Records;
        Assert.Equal([nameof(DiagnosticCode.TypeHeightLimit_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], over.Select(static x => x.Code));
        AssertHeightRecord(over[0], source, "(x@move, 1)");
        Assert.True(over[1].Span.Start > source.IndexOf("func q", StringComparison.Ordinal));
    }

    // SPEC 8.3.3 (Limits): a conformance whose member's written Type passes the limit rests on that member's record, whether the proof
    // would end Unproven or Invalid; the requirement takes (Self.Item, i32), height 8,193 over D13 (review 2's `req-13`, `req-13-self`).
    [Theory]
    [InlineData("(D13<i32>.Item, i32)")]
    [InlineData("(Self.Item, i32)")]
    public void AConformanceWhoseMemberPassesTheLimitGivesOnlyHeightRecords(string parameter)
    {
        var records = this.Compile(Doubling(13) + Pair + $"    public func take(self: ref/Self, x: {parameter}) => ()\n" + Main).Records;
        Assert.NotEmpty(records);
        Assert.All(records, AssertHeightCode);
    }

    // SPEC 23.1.2: an independent Language failure beside a member past the limit is reported beside the height record.
    [Fact]
    public void AnIndependentFailureBesideAMemberPastTheLimitIsKept()
    {
        var source = Doubling(13) + Pair + "    public func take(self: ref/Self, x: (D13<i32>.Item, i32)) => ()\n    public func bad(self) -> i32 => true\n" + Main;
        var records = this.Compile(source).Records;
        var mismatch = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.TypeHeightLimit_Kd));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.True(mismatch.Span.Start > source.IndexOf("func bad", StringComparison.Ordinal));
        Assert.Contains(records, static x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd));
    }

    // SPEC 8.6.2, 8.8.2 (PLAN G665; its review's `h41-u`, `hgt-u41`, `hgt-u44`): L(k)'s declaring layer L0<W^(200k)<X>> passes the limit
    // from k = 41, so a use of the marker at L(k)<i32> forms it and is refused at its own site, never a Language record or a fault
    // (fail-before: dev faults on the 44-level chain). The siteless InternalInvariant_Kd of the generic composition's refusal is PLAN G651's.
    [Theory]
    [InlineData(41, 41)]
    [InlineData(44, 41)]
    [InlineData(44, 44)]
    public void AUseThroughAnInheritedLayerPastTheLimitIsRecordedAtIt(int n, int k)
    {
        var source = Heirs("W", n, "Self is U") + $"func t(v: ref/L{k}<i32>) => useU(v)\n" + Main;
        var record = Assert.Single(this.Compile(source).Records, static x => x.Code != nameof(DiagnosticCode.InternalInvariant_Kd));
        AssertHeightRecord(record, source, "useU(v)");
    }

    // SPEC 8.8.2 (PLAN G665; the Front's ruling 3, item 2): a path with premises states them at its generic declaring layer, so where that
    // layer passes the limit the definition is refused at the first such heir's base clause and the use at its own site, valid (`oc41-u`,
    // `ocown-191`: Option<T> is Copy) or not (`h41-c`, `hgt-c41`, `hgt-c44`: W<T> is not); never a fault (fail-before: dev faults on all
    // five). The failed clauses' dependents beside them are PLAN G325's.
    [Theory]
    [InlineData("W", 41, 0, "L41<i32>", "struct L41")]
    [InlineData("W", 44, 0, "L41<i32>", "struct L41")]
    [InlineData("W", 44, 0, "L44<i32>", "struct L41")]
    [InlineData("Option", 41, 0, "L41<i32>", "struct L41")]
    [InlineData("Option", 40, 191, "M<i32>", "struct M")]
    public void APathWithPremisesPastTheLimitIsRecordedAtItsClauseAndItsUse(string wrapper, int n, int owner, string use, string refused)
    {
        var heir = owner > 0 ? $"open struct M<s/T> : L40<s/{Nest(wrapper, owner, "T")}>\n    s is owner\n" : string.Empty;
        var source = Heirs(wrapper, n, "Self is U when X is Copy") + heir + $"func t(v: ref/{use}) => useU(v)\n" + Main;
        var records = this.Compile(source).Records.Where(static x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd)).ToArray();
        var at = source.IndexOf(refused, StringComparison.Ordinal);
        AssertHeightRecord(Assert.Single(records, x => x.Span.Start > at && x.Span.Start < source.IndexOf('\n', at)), source, "L40<", at);
        var call = source.IndexOf("useU(v)", StringComparison.Ordinal);
        AssertHeightRecord(Assert.Single(records, x => x.Span.Start >= call), source, "useU(v)");
    }

    // DECISIONS BU-153: a Type's display does not recurse on the Type, so a record naming a Type under H, or a closure whose
    // display nests a chain of closure signatures (an edge the height does not count, BU-71), is reported on the caller's stack. The
    // CLI runs the display in tier-0 code: dev's recursion overflowed its 1.5 MB stack from about 820 layers (1,856 B a frame). This
    // host compiles optimized code (QuickJit off), whose frames hold H, so each row runs alone in a fresh process in tier-0 code.
    [Theory]
    [InlineData(nameof(ARecordNamingADeepTypeIsReported))]
    [InlineData(nameof(ARecordNamingAClosureChainIsReported))]
    public Task TierZeroCodeDisplaysADeepType(string row) => FreshProcess.RunExplicit(typeof(TypeHeightLimitTest), row, ("DOTNET_TC_QuickJit", "1"));

    [Fact(Explicit = true)]
    public void ARecordNamingADeepTypeIsReported()
    {
        var chain = Chain(8190, "i32");
        var mismatch = Assert.Single(this.Compile(Doubling(12) + $"func p() -> ()\n    let x: {chain} = true\n    return\n" + Main).Records);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.StartsWith("expected ((((", mismatch.Label, StringComparison.Ordinal); // A record elides a long text in its middle.
        Assert.EndsWith(", i32), i32), found bool", mismatch.Label, StringComparison.Ordinal);

        // The valid twin, and the Origin relation that fails at the outer layer of the same Type (its facts name both Types).
        var (c, d) = ($"ref/({chain}) during c", $"ref/({chain}) during d");
        Assert.Empty(this.Compile(Doubling(12) + $"func f(x: {c}, y: {d}) -> {c}\n    return x@move\n" + Main).Records);
        var relation = Assert.Single(this.Compile(Doubling(12) + $"func f(x: {c}, y: {d}) -> {d}\n    return x@move\n" + Main).Records);
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), relation.Code);
    }

    // A closure's display shows its signature (SPEC 7.7): a closure whose anonymous function takes the previous closure nests the
    // display four frames deeper at a height under 8.
    [Fact(Explicit = true)]
    public void ARecordNamingAClosureChainIsReported()
    {
        var source = new System.Text.StringBuilder("func pass<F, G>(f: ref/F, g: G) -> G\n    G is Callable<(ref/F) -> i32>\n    return g@move\n");
        source.Append("public func main()\n    let c0 = func [] () -> i32 => 1\n");
        for (var i = 1; i <= 512; i++)
        {
            source.Append($"    let c{i} = pass(c{i - 1}@ref, func [] (x) -> i32 => 1)\n");
        }

        var mismatch = Assert.Single(this.Compile(source.Append("    let z: i32 = c512\n").ToString()).Records);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.StartsWith("expected i32, found closure (ref/closure (ref/closure (", mismatch.Label, StringComparison.Ordinal);
        Assert.EndsWith(" -> i32) -> i32) -> i32", mismatch.Label, StringComparison.Ordinal);
    }

    // D0's Item is (T, i32); each D_i's is D_{i-1}<D_{i-1}<T>.Item>.Item, so D_n<i32>.Item has height 2^n.
    internal static string Doubling(int n)
    {
        var s = "contract HasItem\n    associate Item\nstruct D0<T>\n    Self is HasItem\n    associate HasItem.Item is (T, i32)\n";
        for (var i = 1; i <= n; i++)
        {
            s += $"struct D{i}<T>\n    Self is HasItem\n    associate HasItem.Item is D{i - 1}<D{i - 1}<T>.Item>.Item\n";
        }

        return s;
    }

    // A Tuple chain of the given height over `bottom`: D_n<T>.Item adds 2^n layers.
    private static string Chain(int height, string bottom)
    {
        for (var n = 0; n < 13; n++)
        {
            if ((height >> n & 1) != 0)
            {
                bottom = $"D{n}<{bottom}>.Item";
            }
        }

        return bottom;
    }

    // A marker conformance of L0 inherited through L(i)<X> : L(i-1)<wrapper^200<X>>, i = 1..n.
    private static string Heirs(string wrapper, int n, string marker)
    {
        var s = new System.Text.StringBuilder($"contract U\nfunc useU<T>(value: ref/T)\n    T is U\n    ()\nstruct W<T>\n    var v: T\nopen struct L0<X>\n    {marker}\n");
        for (var i = 1; i <= n; i++)
        {
            s.Append($"open struct L{i}<X> : L{i - 1}<{Nest(wrapper, 200, "X")}>\n");
        }

        return s.ToString();
    }

    private static string Nest(string wrapper, int k, string inner) => string.Concat(Enumerable.Repeat(wrapper + "<", k)) + inner + new string('>', k);

    private static void AssertHeightCode(DiagnosticEntryView record) => Assert.Equal(nameof(DiagnosticCode.TypeHeightLimit_Kd), record.Code);

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
        var records = c.Diagnostics.Finalize(rejected: !accepted).Diagnostics
            .Select(static x => new DiagnosticEntryView(x.Code, x.Category, x.Message, x.Span ?? default, x.Label)).ToArray();
        output.WriteLine(string.Join(", ", records.Select(static x => $"{x.Code} {x.Span}")));
        return (records, accepted);
    }

    private readonly record struct DiagnosticEntryView(string Code, DiagnosticCategory Category, string Message, SourceSpan Span, string? Label);
}
