// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// R4-M111 (R4-M108; R6-check H10a D4, D12): Binding's Origin relation records of a node whose relation is also a row A5 judges are its
// twins, which the MIR side leaves out while A5's twin stands for each (until R7).
public partial class OriginRelationDiagnosticTest
{
    // A fit's twin (SPEC 15.6.5's `bad`), a mixed fit whose first recorded chain is fixed-fixed (BU's `fncomp`) and an all-fixed-fixed fit.
    private const string TwinFits = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n" +
        "func mixed(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> (ref/((ref/i32 during x) -> i32), ref/i32 during x)\n" +
        "    let local = 1\n    let t = (g, local@ref)\n    return t\n" +
        "func fixed(x: ref/i32, y: ref/i32) -> ref/i32 during y => x\n";

    // TwinFits with the mixed Tuple's parts swapped: the same records at `t` in either order (G216 (a) U2.1, design-r2 Amendment 1).
    private const string TwinFitsSwapped = "func mixed(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> (ref/i32 during x, ref/((ref/i32 during x) -> i32))\n" +
        "    let local = 1\n    let t = (local@ref, g)\n    return t\n";

    // A local's clause whose record is a body chain (finite over fixed) and one whose record is fixed-fixed.
    private const string TwinClauses = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n" +
        "func twin(a: ref/i32) -> i32\n    let local = 3\n    let h: H{x} = H.init(local@ref)\n        origin x.s outlives a\n    return h.item@follow\n" +
        "func fixed(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return 0\n";

    // A call argument with a fixed-fixed position (y over x, fixed by `s`) and a body-chain position (the local's borrow over x).
    private const string CallPositions = "func store(slot: uniq/(ref/i32 during a), pair: (ref/i32 during a, ref/i32 during a)) => ()\n" +
        "func caller(s: uniq/(ref/i32 during x), y: ref/i32)\n    let local = 1\n    let t = (y, local@ref)\n    store(s, t)\n";

    // R4-M111 (R4-M108): Binding's Origin relation records are twins, left out of the MIR side (compared with A5, `A5`), only where the
    // node's relation is also a row A5 judges: at a fit every record of the node, the mixed fit's fixed-fixed first chain included, and at
    // a local's clause its body-chain record. An all-fixed-fixed node's records stay on it (`-`), Binding's until R7; a Closure result over
    // call-local storage is a twin, its Refuted source at the returned value (DECISIONS R4-M152); a call argument's fixed-fixed record
    // beside a body-chain position of the same call, a row without a record at the same node (BU's review N6). Each row has a record that
    // stays. The mixed fit's Function component is one UnprovenOriginContract_Kd (G216 (a) U2.1, design-r2 §2 and Amendment 1; SPEC
    // 15.6.1; it was an Origin relation record compared as `return t`), so only its borrow's record is a twin, and only when the borrow
    // comes first (no row after the differing Function component until R7).
    [Theory]
    [InlineData(TwinFits, "UnsatisfiedOriginRelation_Kd A5 return local@ref", "UnprovenOriginRelation_Kd - x")]
    [InlineData(TwinFitsSwapped, "UnsatisfiedOriginRelation_Kd A5 return t", "")]
    [InlineData(TwinClauses, "UnsatisfiedOriginRelation_Kd A5 origin x.s outlives a", "UnprovenOriginRelation_Kd - origin x.s outlives b")]
    [InlineData("public func main()\n    let f = func (x: i32) => x@ref\n    let r = f(1)\n", "UnsatisfiedOriginRelation_Kd A5 x@ref", "")]
    [InlineData(CallPositions, "", "UnprovenOriginRelation_Kd - t")]
    public void OnlyTwinsOfBindingsOriginRecordsAreCompared(string source, string compared, string passed)
    {
        var c = MirBuildTest.Compile(source);
        var requirement = DiagnosticRequirement.Binding(BindingFailure.OriginRelation);
        var side = MirTargetSide.MirSide(c).Where(x => x.Requirement == requirement).Select(static x => (x.Code, x.Node)).ToHashSet();
        var records = c.Binding.Issues.Where(static x => x.Failure == BindingFailure.OriginRelation)
            .Select(x => (Twin: !side.Contains((x.Code, x.Node)), Text: $"{x.Code} {(side.Contains((x.Code, x.Node)) ? "-" : "A5")} {source.Substring(x.Node.Span.Start, x.Node.Span.Length)}"))
            .ToArray();
        Assert.Equal(compared.Split('|', StringSplitOptions.RemoveEmptyEntries), Texts(records.Where(static x => x.Twin)));
        Assert.Equal(passed.Split('|', StringSplitOptions.RemoveEmptyEntries), Texts(records.Where(static x => !x.Twin)));

        // The records `code analysis subject` by the subject's source text, in ordinal order.
        static string[] Texts(IEnumerable<(bool Twin, string Text)> rows) => [.. rows.Select(static x => x.Text).Order(StringComparer.Ordinal)];
    }
}
