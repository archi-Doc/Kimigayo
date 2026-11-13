// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G272, the shared acceptance rows (problem statement r2 §3; the owner's 「G272: (1)」): a part past MirTypes' walk bound
/// of 32 gives one <c>OwnershipDepthLimit_Kd</c> record (an Error of category Resource, SPEC 23.1.1, IMPL 21.3.2) where a check's
/// verdict rests on it, no Language record, and no record where no check rests on it. One theory per rule, over A5's forms; the
/// TypeDagScalingTest and MirEffectsTest partials hold the rows of their own programs and A4's. Each row reads the MIR records of
/// module 0 as the first line of the subject and the code.</summary>
public sealed partial class MirBorrowsTest
{
    /// <summary>The owner's code of the limit record, by its text: dev declares no such code, so these rows compile there and fail.</summary>
    internal const string DepthLimit = "OwnershipDepthLimit_Kd";

    // The stores of the own-step rows (problem r2's a6-own): a buffer with a drop (OwnStepShapes' Store), one beside a growing Nest<i32>,
    // whose destruction passes the walk's bound, one beside an Array, and one beside an integer.
    private const string DepthStores = """
        struct DeepStore
            internal var data: raw/i32 during self
            internal var n: Nest<i32>
            public init()
                unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)
                self.n = Nest<i32>.init(value: 1)
        struct ArrStore
            internal var data: raw/i32 during self
            internal var n: Array<i32> = []
            public init()
                unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)
        struct FlatStore
            internal var data: raw/i32 during self
            internal var n: i32
            public init()
                unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)
                self.n = 1
        func shallowAtReturn(s: Store) -> raw/i32 => s.data
        func flatAtReturn(s: FlatStore) -> raw/i32 => s.data
        func arrAtReturn(s: ArrStore) -> raw/i32 => s.data
        func deepAtReturn(s: DeepStore) -> raw/i32 => s.data

        """;

    // SPEC 8.15.3, IMPL 21.3.2 (R8-A1d): owning iteration's storage-height bound, one operation per program: the last accepted depth of an Array's
    // and a fixed array's nest, and one deeper the located resource records. An exceeded limit is no invalid program; the bound is where the
    // compiler-supplied boundary held it. A fixed array's owning iterator is its loop's (SPEC 22.1: its entry is no member; PLAN G776-b).
    [Theory]
    [InlineData("    for x in a@move\n        Console.writeLine(\"x\")\n", false, 61, 3)]
    [InlineData("    var it = a@move.intoIterator()\n    _ = it.next()\n", false, 61, 4)]
    [InlineData("    var it = a@move.intoIterator()\n    Console.writeLine(\"x\")\n", false, 62, 3)]
    [InlineData("    for x in a@move\n        Console.writeLine(\"x\")\n", true, 60, 3)]
    public void OwningIterationsBoundIsLocated(string operation, bool inline, int last, int line)
    {
        Assert.Empty(DiagnosticCorpus.Check(Program(last)).Diagnostics);
        var source = Program(last + 1);
        var rejected = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(rejected, static x => x.Code == DepthLimit);
        Assert.Contains(rejected, static x => x.Code == nameof(DiagnosticCode.Unsupported_Kd));
        Assert.All(rejected, x => Assert.Equal(line, source.AsSpan(0, x.Span!.Value.Start).Count('\n') + 1));

        string Program(int depth)
        {
            var (type, value) = ("i32", "1");
            for (var i = 0; i < depth; i++)
            {
                (type, value) = inline ? ($"[1 of {type}]", $"[{value}]") : ($"Array<{type}>", "[]");
            }

            return $"public func main()\n    var a: {type} = {value}\n{operation}";
        }
    }

    // SPEC 8.15.3, IMPL 21.3.2 (R8-K1a): a Dictionary nest's owning iteration keeps dev's storage-height bound though the owned remainder
    // now holds the consumed Dictionary's Handle whole: 62 levels accepted, one deeper the resource records.
    [Fact]
    public void ADictionaryOwningIterationsBoundIsDevs()
    {
        Assert.Empty(DiagnosticCorpus.Check(Program(62)).Diagnostics);
        var rejected = DiagnosticCorpus.Check(Program(63)).Diagnostics;
        Assert.Contains(rejected, static x => x.Code == DepthLimit);
        Assert.Contains(rejected, static x => x.Code == nameof(DiagnosticCode.Unsupported_Kd));

        static string Program(int depth)
        {
            var type = "i32";
            for (var i = 0; i < depth; i++)
            {
                type = $"Dictionary<i32, {type}>";
            }

            return $"public func main()\n    var d: {type} = [:]\n    for (k, v) in d@move\n        Console.writeLine(\"x\")\n";
        }
    }

    // The program of AnUnclassifiedDestructionOrCallIsUnsupported.
    private const string UnclassifiedPin = "func deep(n: Nest<ref/i32 during a>)\n    let r = n@ref\nfunc unborrowed(n: Nest<ref/i32 during a>)\n    let r = 1\n" +
        "func made()\n    var y = 1\n    let n = Nest<ref/i32>.init(value: y@ref)\nfunc unlent(x: ref/i32)\n    let n = Nest<ref/i32>.init(value: x)\n";

    // The program of AnOwnStepViewIsAProducerOnlyWhereAnAccessMayDepart.
    private const string OwnStepPin = """
        struct DeepBuf
            internal var data: raw/i32 during self
            internal var n: Nest<i32>
            public init()
                unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)
                self.n = Nest<i32>.init(value: 1)
        func ownLimit(b: uniq/Buf<i32>, x: ref/i32)
            let p = b.data
            let n = Nest<ref/i32>.init(value: x)
            usep(p)
        func ownLimitLocal(x: ref/i32)
            var b = Buf<i32>.init(4)
            let p = b.data
            let n = Nest<ref/i32>.init(value: x)
            usep(p)
        func ownDeep(d: uniq/DeepBuf)
            let p = d.data
            d.n = Nest<i32>.init(value: 2)
            usep(p)
        func ownWriteThrough(b: uniq/Buf<i32>)
            let p = b.data
            b@follow = Buf<i32>.init(1)
            usep(p)

        """;

    // Problem r2 §1, §3 a5-w: d = (x@ref, r@ref, z) with z's referent a 40-deep Tuple; the guard of d passes the bound, and rule W
    // from it reached r, giving three Language conflicts (dev). None remains.
    [Theory]
    [InlineData("a5-w-40")]
    public void APartPastTheBoundGivesNoLanguageRecord(string form)
        => Assert.Empty(DepthRows(form).Where(static x => IsLanguage(x.Code)).Select(static x => $"{x.Line} {x.Code}"));

    // Problem r2 §3 a5-w: rule W's verdict at the store rests on the guard past the bound, so one limit record; where it lies is left to
    // the design (TheLimitRecordsLocationLeftToTheDesign).
    [Theory]
    [InlineData("a5-w-40")]
    public void ACheckRestingOnAPartPastTheBoundGivesOneLimitRecord(string form)
    {
        var rows = DepthRows(form);
        Assert.True(rows.Count(static x => x.Code.ToString() == DepthLimit) == 1, string.Join("\n", rows.Select(static x => $"{x.Line} {x.Code}")));
        AssertTheDepthLimitIsAResourceError();
    }

    // Problem r2 §3 (design review 1, B3 and N3): no record where no check rests on a part past the bound: a5-w's twin at depth 10; p5,
    // whose deep-Typed d no Loan flows into; p6, a deep Tuple of i32 beside a borrow; a by-value store's destruction at return that is
    // shallow (Store's drop, an Array) keeps only its own-step Warning, and an integer's gives nothing. A parameter's Drop is located at
    // its Type occurrence (DECISIONS BU-48's locations, settled (a), with dev 855c1ee31's G158 (3)).
    [Theory]
    [InlineData("a5-w-10", "")]
    [InlineData("p5", "")]
    [InlineData("p6", "")]
    [InlineData("shallowAtReturn", "Store OwnStepDeparture_Kd")]
    [InlineData("arrAtReturn", "ArrStore OwnStepDeparture_Kd")]
    [InlineData("flatAtReturn", "")]
    public void NoCheckRestingOnTheBoundGivesNoRecord(string form, string expected)
        => Assert.Equal(Rows(expected), DepthRows(form).Select(static x => $"{x.Line} {x.Code}"));

    // Problem r2 §3 p4, own steps and the existing pins: the bound's located Unsupported_Kd becomes the limit record at the same subject,
    // and every other record stays: p4's borrowed deep root d at its `let d` (dev: Unsupported_Kd there; its twin is p5); deepAtReturn
    // keeps its Warning; AnUnclassifiedDestructionOrCallIsUnsupported's made (`let n`'s call) and deep (p4's form on a parameter, at its
    // function), and AnOwnStepViewIsAProducerOnlyWhereAnAccessMayDepart's ownLimitLocal (`let n`'s call), ownDeep's two records and DeepBuf's
    // init (the call ownDeep's first record makes). Those pins keep their other functions' records; a lane updates their bound records to
    // these verdicts. A parameter's Drop is located at its Type occurrence (BU-48's locations, settled (a): deepAtReturn's and deep's).
    [Theory]
    [InlineData("p4", "let d = mk(value@ref) OwnershipDepthLimit_Kd")]
    [InlineData("deepAtReturn", "DeepStore OwnStepDeparture_Kd|DeepStore OwnershipDepthLimit_Kd")]
    [InlineData("made", "Nest<ref/i32>.init(value: y@ref) OwnershipDepthLimit_Kd")]
    [InlineData("deep", "Nest<ref/i32 during a> OwnershipDepthLimit_Kd")]
    [InlineData("ownLimitLocal", "Nest<ref/i32>.init(value: x) OwnershipDepthLimit_Kd")]
    [InlineData("ownDeep", "Nest<i32>.init(value: 2) OwnershipDepthLimit_Kd|d.n = Nest<i32>.init(value: 2) OwnershipDepthLimit_Kd")]
    [InlineData("init", "Nest<i32>.init(value: 1) OwnershipDepthLimit_Kd")]
    public void TheBoundsLocatedRecordsBecomeTheLimitRecord(string form, string expected)
    {
        Assert.Equal(Rows(expected), DepthRows(form).Select(static x => $"{x.Line} {x.Code}"));
        AssertTheDepthLimitIsAResourceError();
    }

    // Problem r2 §3, a row a design may decide (scored apart): a5-w's limit record at the call whose rule W reads the guard past the bound
    // (IMPL 21.3.2: the affected call).
    [Theory]
    [InlineData("a5-w-40", "store(d@uniq, value@ref) OwnershipDepthLimit_Kd")]
    public void TheLimitRecordsLocationLeftToTheDesign(string form, string expected)
        => Assert.Equal(Rows(expected), DepthRows(form).Where(static x => x.Code.ToString() == DepthLimit).Select(static x => $"{x.Line} {x.Code}"));

    // IMPL 21.3.2, SPEC 23.1.1 (R4-M153's rule W read through R4-M154's ways, `MirBorrows.Changes`): W leaves out the step past a holder
    // whose guard is Deep, and the call records the depth limit exactly where that step would change what W writes. It would where it
    // reaches a holder W reaches only at its first step (first: the argument names g and the deep h, and h holds g.1@uniq), one W reaches
    // past a holder by another Place's Type (otherPlace: t holds r.0@uniq and the deep d, and d holds r.1@uniq, of Type i32), or E where W
    // does not reach it (escaped: d holds a parameter). A step that changes nothing decides nothing: each holder it reaches is reached past
    // at the same Place Type (samePlace: r.1 of Type ref/i32 as r.0), or E is reached by either way, past t (escapedReached: t holds a
    // parameter too) or at the first step (escapedFirst: the argument holds it). otherPlace's and samePlace's d holds r.1's Loan innermost,
    // past the bound at 40, so d's guard is Deep for both Types (an exclusive reference to ref/i32 beside the nested part would give the
    // write fact) and the two differ only in the Place Type. A shared Loan names no holder the step writes (SPEC 15.6.4 item 1, DECISIONS
    // R4-M177): first, otherPlace and samePlace holding g.1@ref and r.1@ref (Ref) record no limit. The write after the call conflicts
    // where a holder the store reached is read after it (Bad), with the record beside it; at depth 10 no guard is Deep. The rows read the
    // call's and the write's records (the holders' own drop-tier and borrow records are other rules'). An address names its Place as a
    // Loan does (SPEC 5.2.1 condition 1, DECISIONS R4-M154 Revision 8; PLAN G667a): t holds r.0@raw and d holds r.1@raw (Raw), so
    // otherPlaceRaw records the limit where samePlaceRaw, whose two Places share a Type, does not. Fails on dev by design: otherPlaceRaw
    // at 40 gave no record at the call (both addresses named r).
    [Theory]
    [InlineData("first", 40, "st((g.0@uniq, h.0@uniq), value@ref) OwnershipDepthLimit_Kd")]
    [InlineData("firstBad", 40, "st((g.0@uniq, h.0@uniq), value@ref) OwnershipDepthLimit_Kd|value = 99 ComparisonLoanConflict_Kd")]
    [InlineData("first", 10, "")]
    [InlineData("firstRef", 40, "")]
    [InlineData("firstRefBad", 40, "value = 99 ComparisonLoanConflict_Kd")]
    [InlineData("otherPlace", 40, "st(t@uniq, value@ref) OwnershipDepthLimit_Kd")]
    [InlineData("otherPlaceBad", 40, "st(t@uniq, value@ref) OwnershipDepthLimit_Kd|value = 99 ComparisonLoanConflict_Kd")]
    [InlineData("otherPlace", 10, "")]
    [InlineData("otherPlaceRef", 40, "")]
    [InlineData("otherPlaceRefBad", 40, "value = 99 ComparisonLoanConflict_Kd")]
    [InlineData("samePlace", 40, "")]
    [InlineData("samePlaceBad", 40, "value = 99 ComparisonLoanConflict_Kd")]
    [InlineData("samePlace", 10, "")]
    [InlineData("samePlaceRef", 40, "")]
    [InlineData("otherPlaceRaw", 40, "st(t@uniq, value@ref) OwnershipDepthLimit_Kd")]
    [InlineData("otherPlaceRaw", 10, "")]
    [InlineData("samePlaceRaw", 40, "")]
    [InlineData("escaped", 40, "st(t@uniq) OwnershipDepthLimit_Kd")]
    [InlineData("escaped", 10, "")]
    [InlineData("escapedReached", 40, "")]
    [InlineData("escapedFirst", 40, "")]
    public void ADeepHoldersStepRecordsTheLimitWhereItWouldChangeWhatWWrites(string form, int depth, string expected)
    {
        var bad = form.EndsWith("Bad", StringComparison.Ordinal);
        var name = bad ? form[..^3] : form;
        var loan = name.EndsWith("Ref", StringComparison.Ordinal) ? "ref" : name.EndsWith("Raw", StringComparison.Ordinal) ? "raw" : "uniq"; // The deep holder's.
        var source = (loan == "uniq" ? name : name[..^3]) switch
        {
            "first" => "func st(t: (uniq/(ref/i32 during a) during b, uniq/(ref/i32 during a) during e), v: ref/i32 during a) => ()\n" +
                $"func f() -> ()\n    var value = 42\n    var x = 1\n    let rx = x@ref\n    var g = (rx, 5)\n" +
                $"    var h = (rx, g.1@{loan}, {Nested("0", depth)})\n    st((g.0@uniq, h.0@uniq), value@ref)\n" + Tail("g"),
            "otherPlace" => Placed("i32", "5", loan),
            "samePlace" => Placed("ref/i32 during a", "y@ref", loan),
            "escaped" => Escaped(borrowed: true, "    var x = 1\n    var r = (x@ref, 5)\n    var t = (r.0@uniq, d@uniq)\n    st(t@uniq)\n"),
            "escapedReached" => Escaped(borrowed: true, "    var t = (q, d@uniq)\n    st(t@uniq)\n"),
            "escapedFirst" => Escaped(borrowed: false, "    st((q, d@uniq))\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var c = CompileDepthForm(Prelude + source + "    return\n");
        Assert.Equal(
            Rows(expected),
            DepthRecords(c, Analyze(c), "f").Where(static x => x.Line.StartsWith("st(", StringComparison.Ordinal) || x.Line == "value = 99")
                .Select(static x => $"{x.Line} {x.Code}"));

        string Tail(string read) => bad ? $"    value = 99\n    inspect({read}@ref)\n" : $"    inspect({read}@ref)\n    value = 99\n";

        // t holds r.0's Loan, or its address where d holds r.1's (a raw layer states no Origin).
        string Placed(string slot, string held, string mode)
        {
            var (first, part) = mode == "raw" ? ("raw", $"raw/(ref/i32 during a), uniq/({NestedType($"raw/({slot})", depth)})") :
                ("uniq", $"uniq/(ref/i32 during a) during b, uniq/({NestedType($"{mode}/({slot}) during g", depth)})");
            return $"func st(t: uniq/(({part} during c)) during e, v: ref/i32 during a) => ()\nfunc f() -> ()\n    var value = 42\n    var x = 1\n" +
                $"    var y = 2\n    var r = (x@ref, {held})\n    var d = {Nested($"r.1@{mode}", depth)}\n    var t = (r.0@{first}, d@uniq)\n" +
                "    st(t@uniq, value@ref)\n" + Tail("r");
        }

        // A store through t (borrowed: an exclusive reference to the Tuple, else the Tuple of exclusive references itself).
        string Escaped(bool borrowed, string body)
        {
            var tuple = $"(uniq/(ref/i32 during a) during b, uniq/((ref/i32 during g, {NestedType("i32", depth)})) during c)";
            return $"func st(t: {(borrowed ? $"uniq/({tuple}) during e" : tuple)}) => ()\n" +
                $"func f(p: ref/i32 during a, q: uniq/(ref/i32 during a) during b) -> ()\n    var d = (p, {Nested("0", depth)})\n" + body;
        }
    }

    /// <summary>Compiles a program of the depth-limit rows as the MIR tests do, after checking that it has no parse diagnostic.</summary>
    /// <param name="source">The program.</param>
    /// <returns>The compilation, which Binding accepted without a record.</returns>
    internal static Compilation CompileDepthForm(string source)
    {
        ParseTestHelper.ParseSuccess(source);
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        return c;
    }

    /// <summary>Gets the MIR records of module 0, of one function or of any, as the first line of the subject and the code, ordered by both
    /// (the order the analyses record them in is no part of these rules).</summary>
    /// <param name="c">The compilation.</param>
    /// <param name="program">Its analyzed program.</param>
    /// <param name="function">The enclosing function's name, or null for every record of module 0.</param>
    /// <returns>The records.</returns>
    internal static (string Line, DiagnosticCode Code)[] DepthRecords(Compilation c, MirProgram program, string? function)
        => [.. program.Problems.All.ToArray().Where(x => !x.IsRecord && x.Subject.Module == 0 && (function is null || Function(c.SyntaxOf(x.Subject)) == function))
            .Select(x => (Line(c, x.Subject), x.Code)).OrderBy(static x => $"{x.Item1} {x.Code}", StringComparer.Ordinal)];

    /// <summary>Gets whether a code's catalog category is Language (SPEC 23.1.1), Warnings included.</summary>
    /// <param name="code">The code.</param>
    /// <returns>Whether it is.</returns>
    internal static bool IsLanguage(DiagnosticCode code) => DiagnosticEntries.TryGet(code, out var entry) && entry.Category == DiagnosticCategory.Language;

    /// <summary>Asserts the owner's answer on the record: one catalog code, an Error of category Resource.</summary>
    internal static void AssertTheDepthLimitIsAResourceError()
    {
        Assert.True(Enum.TryParse<DiagnosticCode>(DepthLimit, out var code), $"{DepthLimit} is not a code");
        Assert.True(DiagnosticEntries.TryGet(code, out var entry), $"{DepthLimit} is not in the catalog");
        Assert.Equal((DiagnosticCategory.Resource, DiagnosticSeverity.Error), (entry.Category, entry.Severity));
    }

    /// <summary>Gets the expected rows of a '|'-separated text, ordered as <see cref="DepthRecords"/> orders.</summary>
    /// <param name="expected">The text; empty for none.</param>
    /// <returns>The rows.</returns>
    internal static string[] Rows(string expected) => expected.Length == 0 ? [] : [.. expected.Split('|').Order(StringComparer.Ordinal)];

    // A form's records after A5 over every view (Analyze), in its function.
    private static (string Line, DiagnosticCode Code)[] DepthRows(string form)
    {
        var (source, function) = DepthForm(form);
        var c = CompileDepthForm(source);
        return DepthRecords(c, Analyze(c), function);
    }

    // The programs of problem r2's A5 forms (from its probes in-a; the pins' programs) and the function a row reads.
    private static (string Source, string Function) DepthForm(string form) => form switch
    {
        "a5-w-10" => (GuardForm(10), "f"),
        "a5-w-40" => (GuardForm(40), "f"),
        "p4" => (MadeForm("    let r = d@ref\n"), "f"),
        "p5" => (MadeForm(string.Empty), "f"),
        "p6" => ($"func f() -> i32\n    var value = 42\n    let r = value@ref\n    let d = {Nested("0", 40)}\n    value = 99\n    return 0\n", "f"),
        "shallowAtReturn" or "flatAtReturn" or "arrAtReturn" or "deepAtReturn" => (Prelude + RawShapes + OwnStepShapes + DepthStores, form),
        "made" or "deep" => (Prelude + UnclassifiedPin, form),
        "ownLimitLocal" or "ownDeep" or "init" => (Prelude + RawShapes + OwnStepShapes + OwnStepPin, form),
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
    };

    // a5-w at a depth: rule W's guard of d = (x@ref, r@ref, z), z's referent a Tuple nested that deep; the stored Loan of value lands in
    // the holders the guard reaches, and r is read after the write.
    private static string GuardForm(int depth)
    {
        var deep = NestedType("i32", depth);
        return "func inspect<T>(value: ref/T) => ()\n" +
            $"func store(t: uniq/((ref/i32 during a, ref/(ref/i32 during b) during c, uniq/{deep} during e)), v: ref/i32 during a) => ()\n" +
            $"func f(z: uniq/{deep}) -> ()\n    var value = 42\n    var x = 1\n    var r = x@ref\n    var d = (x@ref, r@ref, z)\n" +
            "    store(d@uniq, value@ref)\n    value = 99\n    inspect(r@ref)\n    return\nfunc main() => ()\n";
    }

    // p4 and p5: d, a 40-deep Tuple a call makes from a borrow it does not keep, then a write of the borrowed local.
    private static string MadeForm(string borrow)
        => $"func mk(r: ref/i32) -> {NestedType("i32", 40)} => {Nested("0", 40)}\nfunc f() -> ()\n    var value = 42\n    let d = mk(value@ref)\n" +
            borrow + "    value = 99\n    return\n";

    // A value nested in Tuples to a depth, paired with the levels' indices, and its Type (the innermost one's given), paired with i32.
    private static string Nested(string inner, int levels) => new string('(', levels) + inner + string.Concat(Enumerable.Range(0, levels).Select(static i => $", {i})"));

    private static string NestedType(string inner, int levels) => new string('(', levels) + inner + string.Concat(Enumerable.Repeat(", i32)", levels));
}
