// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// R7-O1 H (superseding R4-M111, R4-M108's twins and R6-check H10a D4, D12): Binding judges no Origin relation of a body, so no record of
// a body's relation is Binding's and the MIR side compares no twin.
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

    // A Type whose one Field borrows under its slot, for the omitted-result returns.
    private const string OmittedBox = "struct Box {s}\n    public var r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n";

    // A shared layer over that Type, and a return that moves through it.
    private const string OmittedWrap = OmittedBox + "struct Wrap {w}\n    public let box: ref/(Box during w) during w\n" +
        "    public init(box: ref/(Box during w) during w) => self.box = box\nfunc f(x: ref/i32? during a, b: Wrap during a) -> ref/i32\n    return b.box.r@move\n";

    // R7-O1 H: no body relation is Binding's record (R4-M111's twins were every record of a fit, a local's clause's body-chain record and
    // a Closure result over call-local storage; the all-fixed-fixed records stayed Binding's until R7): each is a row A5 judges, the
    // fixed-fixed ones included, and the MIR side holds each relation's one record. A call position's relation was a row since R7-O1 b1
    // (MirOriginsTest.ACallPositionsRelationIsTheStratums). The mixed fit's Function component stays Binding's contract record (OJ4),
    // which the MIR side lists at `return t`.
    [Theory]
    [InlineData(TwinFits, "UnprovenOriginContract_Kd return t|UnprovenOriginRelation_Kd x|UnsatisfiedOriginRelation_Kd local@ref|UnsatisfiedOriginRelation_Kd t")]
    [InlineData(TwinFitsSwapped, "UnprovenOriginContract_Kd return t|UnsatisfiedOriginRelation_Kd t")]
    [InlineData(TwinClauses, "UnprovenOriginRelation_Kd h0@move|UnsatisfiedOriginRelation_Kd H.init(local@ref)")]
    [InlineData("public func main()\n    let f = func (x: i32) => x@ref\n    let r = f(1)\n", "UnsatisfiedOriginRelation_Kd x@ref")]
    [InlineData(CallPositions, "UnprovenOriginRelation_Kd t|UnsatisfiedOriginRelation_Kd t")]
    // R7-M95 E4d (ruling 45): a return under an omitted result that SPEC 15.4.3 rule 2 makes `static`, whose Origin is an Option input's,
    // is A5's row, so no Binding record carries the elision Note (P5o); a Move through a shared layer there is its take record alone (P6o).
    [InlineData(OmittedBox + "func f(x: ref/i32? during a, b: Box during a) -> ref/i32\n    return b.r\n", "UnprovenOriginRelation_Kd b.r")]
    [InlineData(OmittedWrap, "")]
    public void NoBodyRelationIsBindingsRecord(string source, string records)
    {
        var c = MirBuildTest.Compile(source);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Failure == BindingFailure.OriginRelation);
        var origins = string.Join('|', MirTargetSide.MirSide(c).Where(static x => x.Code.ToString().Contains("Origin", StringComparison.Ordinal))
            .Select(x => $"{x.Code} {source.Substring(x.Node.Span.Start, x.Node.Span.Length)}").Order(StringComparer.Ordinal));
        Assert.True(records == origins, origins);
    }
}
