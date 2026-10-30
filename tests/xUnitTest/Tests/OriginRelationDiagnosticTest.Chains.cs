// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.6.5, 23.1.2 (DECISIONS R4-M118, BU-62): a declaration's rows through an Origin its declaration's failure left unbound (a failed
// clause, or its Type, alias target or Case payload that failed first) are judged by R4-M118's rule. Such a row is never judged itself; a
// chain through it is judged between its Fixed ends at its last edge, once per source and last edge; and one that starts or ends at it
// records nothing. A row the published premises at its use entail forms no edge.
public partial class OriginRelationDiagnosticTest
{
    private const string Cell = "struct C {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n";

    private const string OpenBase = "open struct B<T>\n    public init() => ()\n";

    // b >= x.s >= a.
    private const string Chain = "ref/C{x} during a, ref/(ref/i32 during b) during x.s";

    // c >= x.s >= y.s >= a.
    private const string Hops = "C{x}, C{y}, ref/(ref/i32 during c) during x.s, ref/(ref/i32 during x.s) during y.s, ref/(ref/i32 during y.s) during a";

    // b >= x.s >= a and a >= y.s >= b: the pair (b, a) reached both ways.
    private const string Cycle = "C{x}, C{y}, ref/(ref/i32 during b) during x.s, ref/(ref/i32 during x.s) during a, " +
        "ref/(ref/i32 during a) during y.s, ref/(ref/i32 during y.s) during b";

    // b >= x.s >= a and b >= y.s >= a: one pair, two last edges.
    private const string Paths = "C{x}, C{y}, ref/(ref/i32 during b) during x.s, ref/(ref/i32 during b) during y.s, " +
        "ref/(ref/i32 during x.s) during a, ref/(ref/i32 during y.s) during a";

    // b >= x.s >= a and, only in s's borrow cases, b >= y.s >= a.
    private const string Nested = "C{x}, C{y}, ref/(ref/i32 during b) during x.s, ref/(s/T during b) during y.s, " +
        "ref/(ref/i32 during x.s) during a, ref/(ref/i32 during y.s) during a";

    // c >= (x.s and k) >= a: one Dependent meet at two uses.
    private const string Shared = "C{x}, ref/(ref/i32 during c) during (x.s and k), ref/(ref/i32 during (x.s and k)) during a";

    private const string Unproven = nameof(DiagnosticCode.UnprovenOriginRelation_Kd) + " ";

    /// <summary>Gets each failed form with its Origin records, and the repair that makes its valid twin with the twin's records.</summary>
    public static TheoryData<string, string[], string, string, string[]> DependentChains => new()
    {
        // A failed Field clause (path a) and a failed Field Type (path c): b >= a holds whatever x.s is, so it is reported at its last edge.
        {
            Cell + "struct D {a, b}\n    public let c: (" + Chain + ")\n        origin x.s == zz\n", [Unproven + "ref/C{x} during a"],
            "x.s == zz", "x.s == a", [Unproven + "ref/(ref/i32 during b) during x.s"]
        },
        {
            Cell + "struct D {a, b}\n    public let c: (" + Chain + ", Missing)\n        origin x.s == a\n", [Unproven + "ref/C{x} during a"],
            "Missing", "i32", [Unproven + "ref/(ref/i32 during b) during x.s"]
        },

        // A row that only ends at the unbound Origin records nothing: a Field Type, a Case payload and an alias Type argument that failed.
        { Cell + "struct D {a}\n    public let c: (ref/C{x} during a, Missing)\n        origin x.s == a\n", [], "Missing", "i32", [] },
        { Cell + "enum E {a}\n    One(ref/C{x} during a, Missing)\n        origin x.s == a\n    Two\n", [], "Missing", "i32", [] },
        {
            "alias H => (Outer<(ref/C{y} during static, Missing)>.Helpers{h})\n    origin h.source == static\n    origin y.s == static\n" + Cell +
            "struct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value@move\n", [], "Missing", "i32", []
        },

        // Any number of hops, and a meet by MeetNode's rule: its fixed operand's edge is judged at once.
        {
            Cell + "struct D {a, c}\n    public let f: (" + Hops + ")\n        origin x.s == zz\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during y.s) during a"], "x.s == zz", "x.s == a", [Unproven + "ref/(ref/i32 during c) during x.s"]
        },
        {
            Cell + "struct D {a, c}\n    public let f: (" + Hops + ", Missing)\n        origin x.s == a\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during y.s) during a"], "Missing", "i32", [Unproven + "ref/(ref/i32 during c) during x.s"]
        },
        {
            Cell + "struct D {a, b, c}\n    origin b outlives a\n" +
            "    public let f: (C{x}, ref/(ref/i32 during c) during x.s, ref/(ref/i32 during (x.s and b)) during a)\n" +
            "        origin x.s == zz\n", [Unproven + "ref/(ref/i32 during (x.s and b)) during a"], "x.s == zz", "x.s == a",
            [Unproven + "ref/(ref/i32 during c) during x.s"]
        },
        {
            Cell + "struct D {a, b}\n    public let f: (C{x}, ref/(ref/i32 during (x.s and b)) during a, Missing)\n        origin x.s == static\n",
            [Unproven + "ref/(ref/i32 during (x.s and b)) during a"], "Missing", "i32", [Unproven + "ref/(ref/i32 during (x.s and b)) during a"]
        },
        {
            Cell + "struct D {a, c, k}\n    origin k outlives a\n    public let f: (" + Shared + ")\n        origin x.s == zz\n",
            [Unproven + "ref/(ref/i32 during (x.s and k)) during a"], "x.s == zz", "x.s == a", [Unproven + "ref/(ref/i32 during c) during (x.s and k)"]
        },
        {
            Cell + "struct D {a, c, k}\n    origin k outlives a\n    public let f: (" + Shared + ", Missing)\n        origin x.s == a\n",
            [Unproven + "ref/(ref/i32 during (x.s and k)) during a"], "Missing", "i32", [Unproven + "ref/(ref/i32 during c) during (x.s and k)"]
        },
        {
            Cell + "struct D {a, k}\n    public let c: (ref/(ref/C{x} during (a and k)) during a, Missing)\n        origin x.s == a\n",
            [Unproven + "ref/(ref/C{x} during (a and k)) during a"], "Missing", "i32", [Unproven + "ref/(ref/C{x} during (a and k)) during a"]
        },

        // One record per source and last edge, never a pair merged: two sources at one edge, a pair reached both ways, two paths, and two
        // paths under nested Semantics conditions (a chain's condition is the OR of its rows').
        {
            Cell + "struct D {a, b, k}\n    public let c: (" + Chain + ", ref/(ref/i32 during k) during x.s, Missing)\n        origin x.s == a\n",
            [Unproven + "ref/C{x} during a", Unproven + "ref/C{x} during a"], "Missing", "i32",
            [Unproven + "ref/(ref/i32 during b) during x.s", Unproven + "ref/(ref/i32 during k) during x.s"]
        },
        {
            Cell + "struct D {a, b}\n    origin b outlives a\n    public let f: (" + Cycle + ")\n        origin x.s == zz\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during y.s) during b"], "x.s == zz", "x.s == a", [Unproven + "ref/(ref/i32 during y.s) during b"]
        },
        {
            Cell + "struct D {a, b}\n    public let f: (" + Cycle + ")\n        origin x.s == zz\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during x.s) during a", Unproven + "ref/(ref/i32 during y.s) during b"], "x.s == zz", "x.s == a",
            [Unproven + "ref/(ref/i32 during b) during x.s", Unproven + "ref/(ref/i32 during y.s) during b"]
        },
        {
            Cell + "struct D {a, b}\n    public let f: (" + Paths + ")\n        origin x.s == zz\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during x.s) during a", Unproven + "ref/(ref/i32 during y.s) during a"], "x.s == zz", "x.s == a",
            [Unproven + "ref/(ref/i32 during b) during x.s", Unproven + "ref/(ref/i32 during b) during y.s"]
        },
        {
            Cell + "struct D<s/T> {a, b}\n    s is owner or ref\n    T is Copy\n    public let f: (" + Nested + ")\n" +
            "        origin x.s == zz\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during x.s) during a", Unproven + "ref/(ref/i32 during y.s) during a"], "x.s == zz", "x.s == a",
            [Unproven + "ref/(ref/i32 during b) during x.s", Unproven + "ref/(s/T during b) during y.s"]
        },
        {
            Cell + "struct D<s/T> {a, b}\n    s is owner or ref\n    T is Copy\n" +
            "    public let c: (ref/C{x} during a, ref/(s/T during b) during x.s, Missing)\n" +
            "        origin x.s == a\n", [Unproven + "ref/C{x} during a"], "Missing", "i32", [Unproven + "ref/(s/T during b) during x.s"]
        },

        // The failed declaration's bound clauses judge its chains (`b outlives a` proves this one), but never a row: the bound `x.s == a`
        // cannot cut b >= x.s >= a.
        {
            Cell + OpenBase + "struct D {a, b} : B<(" + Chain + ")>\n    origin b outlives a\n    origin x.s == zz\n    public init() : base() => ()\n", [],
            "x.s == zz", "x.s == a", []
        },
        {
            Cell + OpenBase + "struct D {a, b, c} : B<(" + Chain + ")>\n    origin x.s == a\n    origin c outlives zz\n    public init() : base() => ()\n",
            [Unproven + "ref/C{x} during a"], "c outlives zz", "c outlives a", [Unproven + "ref/(ref/i32 during b) during x.s"]
        },

        // A chain through two failed declarations' Origins, and a signature's well-formed input, a premise at its use, which forms no edge.
        {
            Cell + OpenBase + "struct D {a, b} : B<(C{x}, ref/(ref/i32 during b) during x.s, Missing)>\n    origin x.s == a\n" +
            "    public init() : base() => ()\n" +
            "    public let d: (C{y}, ref/(ref/i32 during x.s) during y.s, ref/(ref/i32 during y.s) during a, Missing)\n        origin y.s == a\n",
            [Unproven + "ref/(ref/i32 during y.s) during a"], ", Missing", string.Empty, [Unproven + "ref/(ref/i32 during b) during x.s"]
        },
        {
            Cell + OpenBase + "struct D {a, b} : B<(ref/C{x} during a, Missing)>\n    origin x.s == a\n    public init() : base() => ()\n" +
            "    public func m(self: ref/Self, p: ref/(ref/i32 during b) during x.s) => ()\n", [], "Missing", "i32", []
        },

        // An independent declaration row beside the failed one stays.
        {
            Cell + "struct D {a, b}\n    public let c: (ref/C{x} during a, Missing)\n        origin x.s == a\n" +
            "    public let d: ref/(ref/i32 during b) during a\n",
            [Unproven + "ref/(ref/i32 during b) during a"], "Missing", "i32", [Unproven + "ref/(ref/i32 during b) during a"]
        },
    };

    [Theory]
    [MemberData(nameof(DependentChains))]
    public void AChainThroughAnUnboundOriginIsJudgedBetweenItsFixedEnds(string source, string[] records, string written, string repair, string[] twinRecords)
    {
        Assert.Equal(records, OriginRecords(source + Main));
        Assert.Equal(twinRecords, OriginRecords(source.Replace(written, repair, StringComparison.Ordinal) + Main));
    }

    // A pair reached both ways with a premise one way is one record of the failing way, never an equality.
    [Fact]
    public void AChainRecordNamesTheRelationThatFails()
    {
        var source = Cell + "struct D {a, b}\n    origin b outlives a\n    public let f: (" + Cycle + ")\n" +
            "        origin x.s == zz\n        origin y.s == a\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Code == nameof(DiagnosticCode.UnprovenOriginRelation_Kd));
        Assert.Equal(("ref/(ref/i32 during y.s) during b", "requires self.a outlives self.b, which is not proven"), (Text(source, error.Span), error.Label));
    }

    // A warm rebind judges a failed declaration's chains from reused storage: a pair reached both ways, and two paths under nested
    // conditions. (A node with more than one record allocates its further records on every pass, as on dev; PLAN G321.)
    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData(Cell + "struct D {a, b}\n    public let f: (" + Cycle + ")\n        origin x.s == zz\n        origin y.s == a\n")]
    [InlineData(Cell + "struct D<s/T> {a, b}\n    s is owner or ref\n    T is Copy\n    public let f: (" + Nested + ")\n        origin x.s == zz\n" +
        "        origin y.s == a\n")]
    public void AWarmRebindJudgesDependentChainsWithoutAllocating(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnprovenOriginRelation_Kd);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // The Origin relation records and located limits of a source, as "code span".
    private static string[] OriginRecords(string source)
        => [.. DiagnosticCorpus.Check(source).Diagnostics
            .Where(static x => x.Code is nameof(DiagnosticCode.UnprovenOriginRelation_Kd) or nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) or
                nameof(DiagnosticCode.Unsupported_Kd))
            .Select(x => $"{x.Code} {Text(source, x.Span)}")];
}
