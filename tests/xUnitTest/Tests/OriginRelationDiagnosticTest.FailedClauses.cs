// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.3.3, 23.1.2 (DECISIONS BU-68): a local's clauses are checks that assume nothing, so a failed one leaves Unbound only the
// inference variables it names, through the local's equalities; the other slots complete as they would without it, and its bound clauses
// are judged. A clause that names no bound Origin, or a contract's failed clause, makes the whole declaration unavailable.
public partial class OriginRelationDiagnosticTest
{
    private const string Pair = "struct P {s, t}\n    public let r: ref/i32 during s\n    public let q: ref/i32 during t\n" +
        "    public init(r: ref/i32 during s, q: ref/i32 during t)\n        self.r = r\n        self.q = q\n";

    private const string PairLocal = Pair + "func f(a: ref/i32) -> ref/i32 during a\n    let l = 5\n    let p: P{x} = P.init(a, l@ref)\n";

    private const string CellLocal = Cell + "func f(a: ref/i32) -> ref/i32 during a\n    let h0 = C.init(a)\n    let h: C{x} = h0@move\n";

    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) + " ";

    /// <summary>Gets each local with a failed clause and its Origin records, and the repair that makes its valid twin with the twin's
    /// records.</summary>
    public static TheoryData<string, string[], string, string, string[]> LocalFailedClauses => new()
    {
        // The slots the failed clause does not name complete, so a valid clause beside it and a use of another slot are judged.
        {
            PairLocal + "        origin x.t outlives a\n        origin x.s == zz\n    return a\n", [Unsatisfied + "P.init(a, l@ref)"],
            "x.s == zz", "x.s == a", [Unsatisfied + "P.init(a, l@ref)"]
        },
        { PairLocal + "        origin x.s outlives zz\n    return p.q\n", [Unsatisfied + "p.q"], "x.s outlives zz", "x.s outlives a", [Unsatisfied + "p.q"] },
        {
            PairLocal + "        origin x.zz outlives a\n    return p.q\n", [Unsatisfied + "p.q"], "x.zz", "x.t",
            [Unsatisfied + "P.init(a, l@ref)", Unsatisfied + "p.q"]
        },

        // The slot it names stays Unbound, resting on it, and so does the equality class of that slot.
        {
            PairLocal + "        origin x.t outlives zz\n    return p.q\n", [Unproven + "p.q"], "x.t outlives zz", "x.t outlives a",
            [Unsatisfied + "P.init(a, l@ref)", Unsatisfied + "p.q"]
        },
        {
            PairLocal + "        origin x.t outlives zz\n    let k: ref/i32 during a = p.q\n    return k\n", [], "x.t outlives zz", "x.t outlives a",
            [Unsatisfied + "P.init(a, l@ref)"]
        },
        { PairLocal + "        origin zz outlives x.t\n    return p.q\n", [Unproven + "p.q"], "zz outlives x.t", "a outlives x.t", [Unsatisfied + "p.q"] },
        {
            PairLocal + "        origin x.t == x.s\n        origin x.s == zz\n    return a\n", [], "x.s == zz", "x.s == a",
            [Unsatisfied + "P.init(a, l@ref)"]
        },
        { CellLocal + "        origin x.s outlives zz\n    let w = h.r@follow@ref\n    return w\n", [Unproven + "w"], "x.s outlives zz", "x.s outlives a", [] },
        {
            Cell + "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    let h0 = C.init(b)\n    let h: C{x} = h0@move\n" +
            "        origin x.s outlives zz\n    let m: ref/i32 during (x.s and b) = b\n    return m\n", [Unproven + "m"], "x.s outlives zz",
            "x.s outlives b", [Unproven + "m"]
        },
    };

    [Theory]
    [MemberData(nameof(LocalFailedClauses))]
    public void ALocalsFailedClauseRestsOnlyTheSlotsItNames(string source, string[] records, string written, string repair, string[] twinRecords)
    {
        Assert.Equal(records, OriginRecords(source + Main));
        Assert.Equal(twinRecords, OriginRecords(source.Replace(written, repair, StringComparison.Ordinal) + Main));
    }

    // A warm rebind of a local whose failed clause rests one slot reuses the failed clauses' storage.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfALocalsFailedClauseDoesNotAllocate()
    {
        var c = MirBuildTest.Compile(PairLocal + "        origin x.t outlives a\n        origin x.s == zz\n    return a\n" + Main);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
