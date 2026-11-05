// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 3.5, 7.2.3, 7.7.2, 8.15, 23.1.2 (R6-check H1, DECISIONS BU-84): Binding judges every bare acquisition that moves a Place, from
// the published columns (S1, a consumed child; S2, a discarded Place) and at closure binding (S3, an omitted capture list's capture);
// a Place rooted in a prepared parameter records DefaultArgumentMove_Kd, a pair-layer Place its cases. The old analysis's cascades
// after such a record are gone, and the forms SPEC lets through record nothing (Q14's (d) rows: an element followed in place, a
// default's Copy, a case-wise Reborrow).
public sealed class TransferJudgmentTest
{
    [Theory]
    [InlineData("public func main()\n    let text = Text.toString(1)\n    text\n    Console.writeLine(text)\n", "TransferRequired_Kd text repair")]
    [InlineData("public func main()\n    let text = Text.toString(1)\n    let f = func () -> string => text\n    Console.writeLine(f())\n", "TransferRequired_Kd func () -> string => text | The omitted capture list captures text only by Copy; it never infers a Move, a borrow or a Reborrow; TransferRequired_Kd text repair")]
    [InlineData("func g(r: uniq/i32)\n    let f = func () -> i32 => r@follow\n    _ = f()\npublic func main()\n    var x = 1\n    g(x@uniq)\n", "TransferRequired_Kd func () -> i32 => r@follow | The omitted capture list captures r only by Copy; it never infers a Move, a borrow or a Reborrow")]
    [InlineData("public func main()\n    let text = Text.toString(1)\n    let b = text\n    Console.writeLine(b)\n", "TransferRequired_Kd text repair")]
    [InlineData("func f<T>(value: T)\n    let g = func () -> T => value\n    _ = g()\npublic func main()\n    f(1)\n", "TransferRequired_Kd func () -> T => value | The omitted capture list captures value only by Copy; it never infers a Move, a borrow or a Reborrow; TransferRequired_Kd value repair")]
    [InlineData("func f<T>(value: T)\n    value\npublic func main()\n    f(1)\n", "TransferRequired_Kd value repair")]
    [InlineData("func k(a: string, b: string = a) => ()\npublic func main()\n    k(Text.toString(1))\n", "DefaultArgumentMove_Kd a")]
    [InlineData("func bad(x: string, y: string = x) => ()\npublic func main()\n    bad(Text.toString(1))\n", "DefaultArgumentMove_Kd x")]
    [InlineData("func bad(x: string, y: string = (x)) => ()\npublic func main()\n    bad(Text.toString(1))\n", "DefaultArgumentMove_Kd (x)")]
    [InlineData("func f<s/T>(value: s/T)\n    s is owner or ref\n    let b = value\npublic func main()\n    let n = 1\n    f(n)\n", "TransferRequired_Kd value repair case=s = owner | Found under the Semantics case s = owner (SPEC 8.15)")]
    [InlineData("func f<t/U, s/T>(value: s/t/U)\n    s is owner or uniq\n    t is owner or ref\n    let b = value\npublic func main() => ()\n", "TransferRequired_Kd value repair case=t = owner, s = owner | Found under the Semantics case t = owner, s = owner (SPEC 8.15)")]
    [InlineData("func run(b: uniq/i32)\n    var items = [b@move]\n    items[0]@follow = 7\n", "")]
    [InlineData("func run(b: uniq/i32)\n    var items = [b@move]\n    let r = items[0]@follow\n", "")]
    [InlineData("func peek<t/U>(value: t/U, seen: U = value@follow) -> U\n    t is owner or uniq\n    U is Copy\n    return seen\nfunc caller<s/T>(x: s/T) -> T\n    s is owner or uniq\n    T is Copy\n    return peek(x)\n", "")]
    [InlineData("func p<s/T>(z: s/(s/T during e) during g)\n    s is ref or uniq\n    let own = z@follow\n    return\n", "")]
    [InlineData("func p<s/T>(q: uniq/(s/T during h) during k)\n    s is ref or uniq\n    let through = q@follow\n", "")]
    [InlineData("func k<s/T>(a: s/T, b: s/T = a) -> ()\n    s is owner or ref\npublic func main() => ()\n", "DefaultArgumentMove_Kd a | Found under the Semantics case s = owner (SPEC 8.15)")]
    [InlineData("func a<T>(b: bool, x: T, y: T = if b => x else => 0) -> () => ()\n", "DefaultArgumentMove_Kd x")]
    public void ABareMoveIsBindingsRecord(string source, string expected) => Assert.Equal(expected, Records(source));

    // The old analysis's report sites that H1 deleted, one row per family (R5's condition (b)); the rows above cover a local, the omitted
    // capture list, a default's bare read of a preceding parameter (also parenthesized) and a pair layer, and the shared-layer rows below
    // a selected referent (`first(names)@follow`), a Slice element and a default past a borrow. The Take repair is carried where the path
    // does not refute Take (SPEC 23.1.3), and the omitted-capture Note with its default tail (SPEC 7.7.2, 7.2.3).
    [Theory]
    [InlineData("struct S\n    public let text: string\nfunc f(s: S)\n    let t = s.text\n", "TransferRequired_Kd s.text repair")]
    [InlineData("public func main()\n    let text = Text.toString(1)\n    let f = func [text] () -> string => text@move\n    Console.writeLine(f())\n", "TransferRequired_Kd text repair | The bare capture entry text initializes its environment binding as let text = text would; string is neither proven Copy nor an exclusive reference")]
    [InlineData("func runT(text: string, action: () -> bool = func () => text == \"a\") -> bool => action()\n", "TransferRequired_Kd func () => text == \"a\" | The omitted capture list captures text only by Copy; it never infers a Move, a borrow or a Reborrow, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return *pointer\n", "TransferRequired_Kd *pointer repair")]
    [InlineData("func f(values: Array<string>)\n    let v = values[0]\n", "TransferRequired_Kd values[0]")]
    [InlineData("func read(values: ref/Array<string>) -> string\n    return values[0]\n", "TransferRequired_Kd values[0]")]
    [InlineData("public func main()\n    let owner = Kimi.Intrinsics.makeObj(\"text\")\n    let value = owner@follow\n", "TransferRequired_Kd owner@follow")]
    public void EachDeletedSiteFamilyIsBindingsRecord(string source, string expected) => Assert.Equal(expected, Records(source));

    // SPEC 3.5.1, 8.14 (PLAN G262, DECISIONS BU-45; MirBuildTest's shared-layer rows before R6-check H1): a bare acquisition through a
    // shared layer is never a Reborrow, so it is the Move row's error: at a `let`, a `return`, a default, an Array literal element, a
    // Slice element, a pair layer through `ref` or another binder's layer, in the case that has no Reborrow. A borrowed referent offers
    // no Take (SPEC 15.1.5), so no transfer is offered, and a default's Place past a borrow is no prepared argument's owned contents
    // (SPEC 7.2.3): TransferRequired_Kd. The first two rows were MIR's StaticMovePathRequired_Kd (MirInitTest) before H1.
    [Theory]
    [InlineData("func lent(x: ref/string, y: string = x@follow) => ()\n", "TransferRequired_Kd x@follow")]
    [InlineData("func first(values: ref/Array<string>) -> ref/string during values => values[0]@ref\nfunc f(names: Array<string>)\n    let bare = first(names)@follow\n", "TransferRequired_Kd first(names)@follow")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b)\n    let y = r@follow\n", "TransferRequired_Kd r@follow")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b) -> uniq/i32 during a\n    return r@follow\n", "TransferRequired_Kd r@follow")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b, x: uniq/i32 during a = r@follow)\n    return\n", "TransferRequired_Kd r@follow")]
    [InlineData("func f(r: ref/(uniq/i32 during a) during b)\n    let values = [r@follow]\n", "TransferRequired_Kd r@follow")]
    [InlineData("func f(s: Slice<uniq/i32 during a> during b)\n    let y = s[0]\n", "TransferRequired_Kd s[0]")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b)\n    s is ref or uniq\n    let y = x@follow\n", "TransferRequired_Kd x@follow case=s = uniq | Found under the Semantics case s = uniq (SPEC 8.15)")]
    [InlineData("func f<s/T, t/U>(x: t/(s/T during a) during b)\n    s is ref or uniq\n    t is ref or uniq\n    let y = x@follow\n", "TransferRequired_Kd x@follow case=s = uniq, t = ref | Found under the Semantics case s = uniq, t = ref (SPEC 8.15)")]
    public void ABareAcquisitionPastASharedLayerIsTheMoveRowsError(string source, string expected) => Assert.Equal(expected, Records(source));

    // SPEC 3.5, 8.15.2 (R6-check (b2); HirFormsTest's Move rows before R6-check H1): a pair layer's owner case whose target has no bare
    // plan is the Move row's error in that case: an exclusive reference past a shared layer, a nested layer's case that neither Copies
    // nor Reborrows.
    [Theory]
    [InlineData("func f<s/T>(value: ref/(s/(uniq/i32 during a) during c) during b)\n    s is owner or ref\n    let local = value@follow\n    _ = local@move\n", "TransferRequired_Kd value@follow case=s = owner | Found under the Semantics case s = owner (SPEC 8.15)")]
    [InlineData("func f<t/U, s/T>(value: s/t/U)\n    s is owner or uniq\n    t is owner or rc\n    U is Copy\n    let local = value\n    _ = local@move\n", "TransferRequired_Kd value repair case=t = rc, s = owner | Found under the Semantics case t = rc, s = owner (SPEC 8.15)")]
    public void AnOwnerCaseWithoutATargetPlanIsTheMoveRowsError(string source, string expected) => Assert.Equal(expected, Records(source));

    // The transfer records of a source: code, located text, whether a repair is offered, the case fact and the Note.
    private static string Records(string source)
    {
        var records = DiagnosticCorpus.Check(source).Diagnostics
            .Where(static x => x.Code is "TransferRequired_Kd" or "DefaultArgumentMove_Kd" or "MovedPlace_Kd")
            .Select(x => $"{x.Code} {source.Substring(x.Span!.Value.Start, x.Span.Value.Length)}" + (x.Repairs is { Length: > 0 } ? " repair" : string.Empty) +
                string.Concat((x.Reason ?? []).Where(static y => y.Name == "case").Select(static y => " case=" + y.Value)) + (x.Note is { } note ? $" | {note}" : string.Empty));
        return string.Join("; ", records);
    }
}
