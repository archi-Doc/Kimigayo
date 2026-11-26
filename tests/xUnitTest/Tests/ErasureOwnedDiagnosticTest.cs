// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.2.3, 7.7.4, 23.1: a common Function conversion whose signature matches and whose subject is not Owned fails a Constraint,
// never a Type: a Refuted proof (a capture that depends on a body-local Borrow) is UnsatisfiedConstraint_Kd and a proof still Unknown
// (a bound Type parameter) is UnprovenConstraint_Kd. The Reason names the subject, the member through which the Origin enters
// OwnedOrigins and, when one is displayable, that Origin, which is related at its syntax. These were TypeMismatch_Kd with a Note.
public class ErasureOwnedDiagnosticTest
{
    private const string HeldPair = "contract Has2\n    associate Out\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\n";

    private const string CaptureY = "    func inspect(a: S<Y>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n";

    private const string CaptureYV = "    func inspect(a: S<Y, V>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n";

    private const string MixedChain = "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\n";

    private const string MixedStore = "struct S<Z, V>\n    Z is Has2\n    V is Has2\n    Z is Owned\n    V is Owned\n    Z.(Has2).Out is Q<PH<V>>\n    V.(Has2).Out is Q<H<H<H<H<H<H<H<H<i32>>>>>>>>>\n    let p: PH<Z>\n";

    private const string Main = "public func main() -> ()\n";

    // A join's arms are erased at the join's expected Type, each where it is bound, and a failing arm is its own record (SPEC 10.2,
    // 14.9.2, 23.1.2; DECISIONS BU-107).
    private const string Join = Main + "    require k(true) == 0 else => $abort(\"j\")\n\nfunc k(c: bool) -> i32\n";

    private const string Two = "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0; UnsatisfiedConstraint_Kd func [r] () -> i32 => 1";

    // SPEC 7.7.4, 15.2.3 (dev's records, pinned for PLAN G148 (1)): a capture over a body atom is not Owned, and the erasure's Owned part
    // decides before independence is read: Refuted where the atom is the body's own (o1, a conversion; o2, an argument), Unknown in a
    // nested body where it is the enclosing body's (s2, s2r: a Closure literal awaits its call (10.5.1) and is judged at its value after
    // selection). Labels: originpub labels.txt o1, labels-r1.txt o2, labels-s.txt s2 and s2r (I).
    [Theory]
    [InlineData("    let f: () -> () = func [h@move] () => ()\n    f()\n", "UnsatisfiedConstraint_Kd func [h@move] () => ()")]
    [InlineData("    takes(func [h@move] () => ())\n", "UnsatisfiedConstraint_Kd func [h@move] () => ()")]
    [InlineData("    let outer = func [h@move] () => g(func [h@move] () => ())\n", "UnprovenConstraint_Kd func [h@move] () => ()")]
    [InlineData("    let outer = func [h@move] () => g(func [h@ref] () => ())\n", "UnprovenConstraint_Kd func [h@ref] () => ()")]
    public void TheOwnedPartDecidesAnErasureOverABodyAtom(string tail, string expected)
    {
        var source = "struct Wrap<E>\n    var value: E\n    public init(value: E) => self.value = value@move\nfunc takes(f: () -> ()) => ()\n" +
            "func g(f: () -> ()) => ()\n" + Main + "    var x = 1\n    var w = (0, x@ref)\n    var r = w.0@uniq\n    var h = Wrap.init(r@follow@raw@raw/u8)\n" + tail;
        var output = DiagnosticCorpus.Check(source);
        Assert.Equal(expected, string.Join("; ", output.Diagnostics.Select(x => $"{x.Code} {Text(source, x.Span)}")));
    }

    // SPEC 7.7.4, 15.2.3 (PLAN G148 (1), the Origin-atom borrows read while a body binds; G147 rulings 11, 36): a named Closure erased at
    // a call's applicability in a nested body, whose environment's raw layer states an enclosing body's Borrow atom (Owned Unknown there),
    // is judged by ReceiverIndependence while the bodies bind, through that atom's Borrows so far, and keeps its Unproven record, not the
    // located limit. The atom: a `let` borrow's P[w] (s2ln); a bare Place Subject's P[o] under a Pattern binding (pm2mn, pm2rn: the
    // Subject's Borrow formed by BindMatch before the arms bind); an argument's and a receiver's implicit Borrow under a Closure literal
    // argument's header (ap1, rc1: formed by the selection before the literal binds); a capture entry's slot Borrow (cp1: formed once
    // the entries are, before the body binds); a payload's implicit Borrow under a later Closure literal payload's header (pl3: formed by
    // CompletePayloads at its write); a Binding past a qualifying pair layer of an owned Subject or item, which depends on the Subject
    // Place itself (pv3, pv4: formed where the Patterns bind, before the bodies). Each row has no Borrow for its atom while it binds if
    // the formation comes later, so a reader of the rows (Z6) fails it first. Labels: originpub rvc\labels.txt s2ln, pm2mn, pm2rn,
    // c1c5\labels.txt ap1, rc1, cp1, d1d3\labels.txt pl3, pv3, pv4 (I).
    [Theory]
    [InlineData("    var x = 1\n    var w = (0, x@ref)\n    let r = w.0@uniq\n    var h = Wrap.init(r@follow@raw@raw/u8)\n    let outer = func [h@move] ()\n        let inner = func [h@move] () => ()\n        g(inner@move)\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    var o: Option<i32> = .Some(3)\n    match o\n        .Some(let m)\n            var h = Wrap.init(m@follow@raw@raw/u8)\n            let outer = func [h@move] ()\n                let inner = func [h@move] () => ()\n                g(inner@move)\n        _ => ()\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    var o: Option<i32> = .Some(3)\n    match o\n        .Some(let m)\n            var h = Wrap.init(m@follow@raw@raw/u8)\n            let outer = func [h@move] ()\n                let inner = func [h@ref] () => ()\n                g(inner)\n        _ => ()\n", "UnprovenConstraint_Kd inner")]
    [InlineData("    var v = 1\n    apply(v, func (r)\n        var h = Wrap.init(r@follow@raw@raw/u8)\n        let inner = func [h@move] () => ()\n        g(inner@move)\n    )\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    var b = Box.init(1)\n    b.each(func (r)\n        var h = Wrap.init(r@follow@raw@raw/u8)\n        let inner = func [h@move] () => ()\n        g(inner@move)\n    )\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    var h = 1\n    let outer = func [h@ref] ()\n        var k = Wrap.init(h@follow@raw@raw/u8)\n        let inner = func [k@move] () => ()\n        g(inner@move)\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    var v = 1\n    let e = Pair.Both(v, func (r)\n        var h = Wrap.init(r@follow@raw@raw/u8)\n        let inner = func [h@move] () => ()\n        g(inner@move)\n    )\nenum Pair {a}\n    Both(ref/i32 during a, (ref/i32 during a) -> ())\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    return\nfunc q<s/T>(pair: (s/T, i32))\n    s is owner or uniq\n    match pair@move\n        (let x, _)\n            var h = Wrap.init(x@follow@raw@raw/u8)\n            let outer = func [h@move] ()\n                let inner = func [h@move] () => ()\n                g(inner@move)\n", "UnprovenConstraint_Kd inner@move")]
    [InlineData("    return\nfunc q<s/T>(arr: Array<(s/T, i32)>)\n    s is owner or uniq\n    for (x, _) in arr@move\n        var h = Wrap.init(x@follow@raw@raw/u8)\n        let outer = func [h@move] ()\n            let inner = func [h@move] () => ()\n            g(inner@move)\n", "UnprovenConstraint_Kd inner@move")]
    public void ANamedClosureOverAnEnclosingBodyAtomKeepsItsConstraintRecord(string body, string expected)
    {
        var source = "struct Wrap<E>\n    var value: E\n    public init(value: E) => self.value = value@move\nstruct Box\n    var n: i32\n" +
            "    public init(n: i32) => self.n = n\n    public func each(self: ref/Self, f: (ref/i32 during self) -> ()) => ()\nfunc g(f: () -> ()) => ()\n" +
            "func apply(x: ref/i32, f: (ref/i32 during x) -> ()) => ()\n" + Main + body;
        Assert.Equal(expected, string.Join("; ", Records(source)));
    }

    // A let annotation, a default, a call argument, a Case payload (PLAN G381: was the construction's TypeMismatch_Kd), a captured
    // reference value and a join's arm give the same record at the converted value;
    // of two failing captures the body-local borrow is named, whatever their order, and in a nested closure the enclosing body's borrow
    // is fixed while the body's own is local (R4-M161).
    [Theory]
    [InlineData(Main + "    let text = \"a\"\n    let f: () -> () = func [text@ref] () => ()\n    f()\n", "func [text@ref] () => ()", "text", "text@ref", "text@ref")]
    [InlineData("func runH(text: string, action: () -> () = func [text@ref] () => ()) -> () => action()\n\n" + Main + "    runH(\"a\")\n", "func [text@ref] () => ()", "text", "text@ref", "text@ref")]
    [InlineData("func call(action: (i32) -> i32, x: i32) -> i32\n    return action(x)\n\n" + Main + "    let y: i32 = 2\n    require call(func [y@ref] (n) => n + y@follow, 1) == 3 else => $abort(\"k1\")\n", "func [y@ref] (n) => n + y@follow", "y", "y@ref", "y@ref")]
    [InlineData("enum E\n    A(() -> i32)\n    B\n\n" + Main + "    let v = 1\n    let r = v@ref\n    let e = E.A(func [r] () -> i32 => 0)\n", "func [r] () -> i32 => 0", "r", "v@ref", "v@ref")]
    [InlineData(Main + "    var n = 7\n    let view = n@uniq\n    let f = func [view] () => view@follow@ref\n    let e: () -> ref/i32 = f@move\n", "f@move", "view", "n@uniq", "n@uniq")]
    [InlineData(Main + "    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n    let g: (i32) -> i32 = f\n", "f", "y", "y@ref", "y@ref")]
    [InlineData(Join + "    let v = 1\n    let r = v@ref\n    let g: () -> i32 = if c => func [r] () -> i32 => 0 else => func [] () -> i32 => 1\n    return 0\n", "func [r] () -> i32 => 0", "r", "v@ref", "v@ref")]
    [InlineData(Main + "    let a = 1\n    require k(a@ref) == 0 else => $abort(\"p1\")\n\nfunc k(x: ref/i32) -> i32\n    let v = 1\n    let r = v@ref\n    let g: () -> i32 = func [x, r] () -> i32 => 0\n    return 0\n", "func [x, r] () -> i32 => 0", "r", "v@ref", "v@ref")]
    [InlineData(Main + "    require m() == 0 else => $abort(\"p5\")\n\nfunc m() -> i32\n    let w = 1\n    let mr = w@ref\n    let k = func [mr] () -> i32\n        let v = 1\n        let kr = v@ref\n        let g: () -> i32 = func [mr, kr] () -> i32 => 0\n        return 0\n    return k()\n", "func [mr, kr] () -> i32 => 0", "kr", "v@ref", "v@ref")]
    public void ACaptureOverABorrowIsAnUnsatisfiedConstraint(string source, string text, string member, string origin, string related)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), DiagnosticCategory.Language, text), (error.Code, error.Category, Text(source, error.Span)));
        Assert.Equal(related, Text(source, Assert.Single(error.Related!, static x => x.Role == "origin").Span));
        Assert.Contains($"the capture {member} depends on the borrow {origin}, which is not static", error.Note, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(new DiagnosticResult(output.Diagnostics, output.Sources), DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains($"{{\"name\":\"member\",\"kind\":\"Text\",\"value\":\"{member}\",\"elided\":false}}", json, StringComparison.Ordinal);
        Assert.Contains($"{{\"name\":\"origin\",\"kind\":\"Origin\",\"value\":\"{origin}\",\"elided\":false,\"origin\":\"borrow\"}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DiagnosticCode.TypeMismatch_Kd), json, StringComparison.Ordinal);
    }

    // SPEC 15.2.3, 7.7.4, 23.1.2 (DECISIONS BU-107, PLAN G381): a failed Owned part is the converted value's own record wherever the
    // value is: a Tuple or array element, an element of a payload or call argument, each failing join arm and array element, and beside
    // another part's independent record. Before, an enclosing literal's TypeMismatch_Kd, or one record for two failures.
    [Theory]
    [InlineData("b1", "    let t: (() -> i32, i32) = (func [r] () -> i32 => 0, 1)\n", "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0")]
    [InlineData("b2", "    let a: Array<() -> i32> = [func [r] () -> i32 => 0]\n", "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0")]
    [InlineData("b7", "    let e = E2.A((func [r] () -> i32 => 0, 1))\n", "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0")]
    [InlineData("b9", "    let n = takeT((func [r] () -> i32 => 0, 1))\n", "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0")]
    [InlineData("j2", "    let h: () -> i32 = if c => func [r] () -> i32 => 0 else => func [r] () -> i32 => 1\n", Two)]
    [InlineData("ar2", "    let a: Array<() -> i32> = [func [r] () -> i32 => 0, func [r] () -> i32 => 1]\n", Two)]
    [InlineData("tm1", "    let t: (() -> i32, i32) = (func [r] () -> i32 => 0, missing)\n", "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0; UnresolvedBinding_Kd missing")]
    public void EachFailedOwnedErasureIsItsValuesRecord(string id, string statement, string records)
    {
        var source = "enum E2\n    A((() -> i32, i32))\n    B\n\nfunc takeT(t: (() -> i32, i32)) -> i32 => 0\n\n" + Main +
            "    require g(true) == 0 else => $abort(\"g\")\n\nfunc g(c: bool) -> i32\n    let v = 1\n    let r = v@ref\n" + statement + "    return 0\n";
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics.OrderBy(static x => x.Span!.Value.Start);
        Assert.Equal((id, records), (id, string.Join("; ", diagnostics.Select(x => $"{x.Code} {Text(source, x.Span)}"))));
    }

    // SPEC 14.9.2 step 4, 7.7.4, 23.1.2 (DECISIONS R4-M161, BU-107; PLAN G844, G844-5): a call argument join's arm bound at the
    // parameter's Function Type is erased once, in the join's one per-source step, so its failed Owned part is one record (e3), never
    // InternalInvariant_Kd (a join record with no erasure row is one).
    [Theory]
    [InlineData("e3", "    let v = 1\n    let r = v@ref\n    return call(if c => func [r] () -> i32 => 0 else => func [] () -> i32 => 1)\n", "UnsatisfiedConstraint_Kd func [r] () -> i32 => 0")]
    public void AnArmBoundAtTheParameterTypeIsErasedOnce(string id, string statements, string records)
    {
        var source = "func call(action: () -> i32) -> i32 => action()\n\n" + Main + "    require g(true) == 0 else => $abort(\"g\")\n\nfunc g(c: bool) -> i32\n" + statements;
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics.OrderBy(static x => x.Span!.Value.Start);
        Assert.Equal((id, records), (id, string.Join("; ", diagnostics.Select(x => $"{x.Code} {Text(source, x.Span)}"))));
    }

    // A bound Type parameter that is not proven Owned leaves the proof Unknown; its member is the Item's bound Type argument, with no
    // Origin. A default and a let annotation agree.
    [Theory]
    [InlineData("func identity<T>(value: T) -> T => value@move\n\nfunc runT<T>(value: T, action: (T) -> T = identity) -> T => action(value@move)\n\n" + Main + "    require runT(4) == 4 else => $abort(\"t\")\n")]
    [InlineData("func identity<T>(value: T) -> T => value@move\n\nfunc runT<T>(value: T) -> T\n    let f: (T) -> T = identity\n    return f(value@move)\n\n" + Main + "    require runT(4) == 4 else => $abort(\"t\")\n")]
    public void AnUnprovenTypeArgumentIsAnUnprovenConstraint(string source)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), DiagnosticCategory.Proof, "identity"), (error.Code, error.Category, Text(source, error.Span)));
        var json = JsonSerializer.Serialize(new DiagnosticResult(output.Diagnostics, output.Sources), DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"member\",\"kind\":\"Text\",\"value\":\"1st Type argument\",\"elided\":false}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"origin\"", json, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(output.Diagnostics, output.Sources), string.Empty);
        Assert.Contains("the 1st Type argument is not proven Owned", console.Text, StringComparison.Ordinal);
    }

    // The counterparts: an Owned Constraint proves the conversion, and the receiver condition keeps its Type mismatch.
    [Fact]
    public void AnOwnedConstraintProvesTheConversion()
        => Assert.Empty(DiagnosticCorpus.Check("func identity<T>(value: T) -> T => value@move\n\nfunc keepOwned<T>(value: T) -> (T) -> T\n    T is Owned\n    let f: (T) -> T = identity\n    return f@move\n\n" + Main + "    let f = keepOwned(1)\n    require f(2) == 2 else => $abort(\"f\")\n").Diagnostics);

    // A call argument, a join's arm (grouped: in an arm's single-item body a closure takes no indented body, SPEC F.4.4; the arm's own
    // record at the declared Type, SPEC 14.9.2 step 4, F's G844-16), and a closure whose receiver and environment both fail (the receiver
    // is its record).
    [Theory]
    [InlineData("func call(action: (i32) -> i32, x: i32) -> i32\n    return action(x)\n\n" + Main + "    let c: i32 = 0\n    require call(func [var c] (n) -> i32\n        c += 1\n        return n + c\n    , 1) == 2 else => $abort(\"k2\")\n", "func [var c]", "expected (i32) -> i32, found closure (i32) -> i32")]
    [InlineData(Join + "    var n: i32 = 0\n    let g: () -> i32 = if c => (func [var n] () -> i32\n        n += 1\n        return n\n    ) else => func [] () -> i32 => 1\n    return 0\n", "(func [var n]", "expected () -> i32, found closure () -> i32")]
    [InlineData(Join + "    var n: i32 = 0\n    let v = 1\n    let r = v@ref\n    let g: () -> i32 = func [var n, r] () -> i32\n        n += 1\n        return n\n    return 0\n", "func [var n, r]", "expected () -> i32, found closure () -> i32")]
    public void AnExclusiveClosureStaysATypeMismatch(string source, string at, string label)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.TypeMismatch_Kd), label, (string?)null), (error.Code, error.Label, error.Note));
        Assert.StartsWith(at, source[error.Span!.Value.Start..], StringComparison.Ordinal);
    }

    // SPEC 23.1.2 (DECISIONS BU-75): an Owned proof that could not judge its subject, because a part names a failed declaration, fails the
    // conversion as derived from that declaration, with no record of its own: a let, an Item's own record beside it, a call argument, an
    // assignment, a return, an annotation naming the failed Type, two captures and a later use give only the declaration's records. These
    // were InternalInvariant_Kd (BU's n7).
    [Theory]
    [InlineData("func use(s: S) -> ()\n    let g: () -> () = func [s@move] () => ()\n    return\n", null)]
    [InlineData("func identity<T>(value: T) -> T => value@move\n\nfunc use(s: S) -> ()\n    let f: (S) -> S = identity\n    let g: () -> () = func [s@move] () => ()\n    return\n", "InvalidConstraint_Kd identity")]
    [InlineData("func take(g: () -> ()) -> ()\n    return\n\nfunc use(s: S) -> ()\n    take(func [s@move] () => ())\n    return\n", null)]
    [InlineData("func use(s: S) -> ()\n    var g: () -> () = func () => ()\n    g = func [s@move] () => ()\n    return\n", null)]
    [InlineData("func use(s: S) -> () -> ()\n    return func [s@move] () => ()\n", null)]
    [InlineData("func use(s: S) -> ()\n    let g: (S) -> () = func [s@move] (x: S) => ()\n    return\n", null)]
    [InlineData("func use(s: S, t: S) -> ()\n    let g: () -> () = func [s@move, t@move] () => ()\n    return\n", null)]
    [InlineData("func use(s: S) -> ()\n    let g: () -> () = func [s@move] () => ()\n    let h: () -> () = g@move\n    return\n", null)]
    public void AnOwnedFailureOnAFailedDeclarationIsDerived(string uses, string? own)
        => Assert.Equal(
            ["InvalidConstraint_Kd contract", "UnresolvedBinding_Kd Missing", "InvalidTypeFormation_Kd struct", "UnresolvedBinding_Kd C", .. own is null ? Array.Empty<string>() : [own]],
            Records("contract C: Missing\n    func m(self) -> i32\n\nstruct S: C\n    let x: i32\n    func m(self) -> i32 => 1\n\n" + uses + "\n" + Main + "    return\n"));

    // The valid twins: the same captures of a declaration that formed convert with no record.
    [Theory]
    [InlineData("func use(s: S) -> ()\n    let g: () -> () = func [s@move] () => ()\n    return\n")]
    [InlineData("func use(s: S) -> () -> ()\n    return func [s@move] () => ()\n")]
    public void AnOwnedCaptureOfAFormedDeclarationConverts(string uses)
        => Assert.Empty(Records("struct S\n    let x: i32\n    func m(self) -> i32 => 1\n\n" + uses + "\n" + Main + "    return\n"));

    // SPEC 23.1.2 (DECISIONS BU-76): the Owned walk holds the stored Type it reads, which its declaration fails, so a capture whose field,
    // Option field, enum payload or returned closure stores a failed declaration gives only the declarations' records; the valid twin
    // converts. These were InternalInvariant_Kd (r2-1b's B1).
    [Theory]
    [InlineData("struct W\n    let s: S\n\nfunc use(w: W) -> ()\n    let g: () -> () = func [w@move] () => ()\n    return\n", "InvalidConstraint_Kd let s: S")]
    [InlineData("struct W\n    let s: Option<S>\n\nfunc use(w: W) -> ()\n    let g: () -> () = func [w@move] () => ()\n    return\n", "InvalidConstraint_Kd let s: Option<S>")]
    [InlineData("enum V\n    A(S)\n    B\n\nfunc use(v: V) -> ()\n    let g: () -> () = func [v@move] () => ()\n    return\n", null)]
    [InlineData("struct W\n    let s: S\n\nfunc use(w: W) -> () -> ()\n    return func [w@move] () => ()\n", "InvalidConstraint_Kd let s: S")]
    public void AnOwnedFailureOnAFailedStoredTypeIsDerived(string uses, string? own)
    {
        Assert.Equal(
            ["InvalidConstraint_Kd contract", "UnresolvedBinding_Kd Missing", "InvalidTypeFormation_Kd struct", "UnresolvedBinding_Kd C", .. own is null ? Array.Empty<string>() : [own]],
            Records("contract C: Missing\n    func m(self) -> i32\n\nstruct S: C\n    let x: i32\n    func m(self) -> i32 => 1\n\n" + uses + "\n" + Main + "    return\n"));
        Assert.Empty(Records("struct S\n    let x: i32\n\n" + uses + "\n" + Main + "    return\n"));
    }

    // SPEC 6.2, 8.3.3, 23.1.2 (DECISIONS BU-76, BU-128): a stored Type whose conformance binding stores itself at a deeper instance fails with
    // the binding's InvalidInlineLayout_Kd, and a capture of its holder rests on it with no record; a terminating binding converts. PLAN
    // G1278 (G1278-A, Q13; F's G1278-3): a Field's name carries its written Type, so its restating InvalidConstraint_Kd goes (`p` here, in
    // AnOwnedFailureOnAResolutionUsingItselfIsDerived's ch, k-cond, r-gen, r-mut and k-inh, and `a` in AnIdentifiedTypeIsOneTypeForStorageAndA5's z03).
    [Theory]
    [InlineData("PH<W<(X, X)>>", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: PH<W<i32>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("PH<V<Option<X>>>", "")]
    public void AnOwnedFailureOnAConformanceUsingItselfIsDerived(string binding, string records)
        => Assert.Equal(
            records,
            string.Join(" | ", Records("contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is " + binding +
                "\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: PH<W<i32>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n" + Main + "    return\n")));

    // DECISIONS BU-75, BU-76: Binding.Check's Final prologue settles each use FailDerived listed; a part that is not Invalid there is a
    // broken contract, InternalInvariant_Kd, never a Language record.
    [Fact]
    public void ADerivedUseOnAPartNotInvalidIsABrokenContract()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    let x: i32\n\nfunc use(s: S) -> ()\n    let g: () -> () = func [s@move] () => ()\n    return\n\n" + Main + "    return\n");
        Assert.True(c.Bind().IsComplete);
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "g");
        var part = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single();
        typeof(Binding).GetMethod("FailDerived", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Koto), typeof(Koto)])!.Invoke(c.Binding, [use, part]);
        Assert.Equal(BindingFailure.InvalidConstraint, c.Semantics.FailureOf(use));
        typeof(Binding).GetMethod("SettleDerivedUses", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c.Binding, []);
        Assert.Equal(BindingFailure.Internal, c.Semantics.FailureOf(use));
    }

    // SPEC 8.3.3, 23.1.2 (DECISIONS BU-76): a stored Type that nests a conformance resolution using itself (Option, tuple, Array, an
    // inherited binding, a chain of two hops, two failing hops) holds the hops it resolved through, and a capture of its holder rests on
    // the first that failed, with no record; the guards (the stored Type itself fails, an enum payload, a generic holder, mutual and
    // conditional bindings, an inherited binding, the chain's twin) keep the declarations' records. ch-opt to k-inh-opt were
    // InternalInvariant_Kd at the closure.
    [Theory]
    [InlineData("ch-opt", "contract K\n    associate B\ncontract Has2\n    associate Out\nstruct Q<X>\n    Self is K\n    associate K.B is PH<W<(X, X)>>\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is Q<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct S\n    let p: Option<PH<W<i32>>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd K.B is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: Option<PH<W<i32>>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("ch2", "contract K\n    associate B\ncontract Has2\n    associate Out\nstruct Q<X>\n    X is Has2\n    Self is K\n    associate K.B is PH<W<(X, X)>>\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is Q<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct S\n    let p: Option<PH<W<i32>>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd K.B is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: Option<PH<W<i32>>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up); R7-C3 (F's C2 ruling 283 (c), DECISIONS Q13): - Q's `struct` and W's `Self is Has2` records and S's PrerequisiteUnavailable_Kd (now its own InvalidConstraint_Kd): the cause's records are Q's `K.B is PH<W<(X, X)>>` (InvalidInlineLayout_Kd, InvalidConstraint_Kd) (ruling 283 (c))
    [InlineData("r-opt", "contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: Option<PH<W<i32>>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: Option<PH<W<i32>>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("r-tup", "contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: (PH<W<i32>>, i32)\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: (PH<W<i32>>, i32) | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("r-arr", "contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: Array<PH<W<i32>>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: Array<PH<W<i32>>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("k-inh-opt", "contract Has2\n    associate Out\nopen struct Base<X>\n    Self is Has2\n    associate Has2.Out is PH<Leaf<(X, X)>>\n    public init() => ()\nstruct Leaf<X>: Base<X>\n    public init() => ()\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: Option<PH<Leaf<i32>>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<Leaf<(X, X)>> | InvalidConstraint_Kd PH<Leaf<(X, X)>> | InvalidInlineLayout_Kd Base<X> | InvalidConstraint_Kd let p: Option<PH<Leaf<i32>>> | InvalidConstraint_Kd PH<Leaf<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 rulings 298, 302, DECISIONS Q13): the evidence step reads the inherited path's declared binding as failed, so generation stops there (no limit record, O-448 (a)); + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("ch", "contract K\n    associate B\ncontract Has2\n    associate Out\nstruct Q<X>\n    Self is K\n    associate K.B is PH<W<(X, X)>>\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is Q<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct S\n    let p: PH<W<i32>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd K.B is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: PH<W<i32>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("ch-twin", "contract K\n    associate B\ncontract Has2\n    associate Out\nstruct Q<X>\n    Self is K\n    associate K.B is PH<W<(X, X)>>\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is Q<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\n", "InvalidInlineLayout_Kd K.B is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>>")]
    [InlineData("k-cond", "contract Has2\n    associate Out\nstruct W<X>\n    Self is Has2 when X is i32\n        associate Has2.Out is PH<W<(X, X)>>\n    Self is Has2 when X is (i32, i32)\n        associate Has2.Out is i32\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: PH<W<i32>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | DuplicateBinding_Kd Self is Has2 | InvalidConstraint_Kd let p: PH<W<i32>> | InvalidConstraint_Kd PH<W<i32>>")] // R7-C3 (F's C2 ruling 283 (c), DECISIONS Q13): - the conditional conformance's `Self is Has2 when X is i32` record and S's PrerequisiteUnavailable_Kd (now its own InvalidConstraint_Kd): the cause's records are `Has2.Out is PH<W<(X, X)>>` (InvalidInlineLayout_Kd, InvalidConstraint_Kd) (ruling 283 (c))
    [InlineData("r-enum", "contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nenum S\n    A(PH<W<i32>>)\n    B\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("r-gen", "contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S<Y>\n    let p: PH<W<Y>>\nfunc inspect2<Y>(a: S<Y>) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n", "InvalidInlineLayout_Kd Has2.Out is PH<W<(X, X)>> | InvalidConstraint_Kd PH<W<(X, X)>> | InvalidConstraint_Kd let p: PH<W<Y>> | InvalidConstraint_Kd PH<W<Y>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("r-mut", "contract Has2\n    associate Out\nstruct A<X>\n    Self is Has2\n    associate Has2.Out is PH<B<(X, X)>>\nstruct B<X>\n    Self is Has2\n    associate Has2.Out is PH<A<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: PH<A<i32>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<B<(X, X)>> | InvalidConstraint_Kd PH<B<(X, X)>> | InvalidInlineLayout_Kd Has2.Out is PH<A<(X, X)>> | InvalidConstraint_Kd PH<A<(X, X)>> | InvalidConstraint_Kd let p: PH<A<i32>> | InvalidConstraint_Kd PH<A<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 ruling 298, DECISIONS Q13): dev's records + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    [InlineData("k-inh", "contract Has2\n    associate Out\nopen struct Base<X>\n    Self is Has2\n    associate Has2.Out is PH<Leaf<(X, X)>>\n    public init() => ()\nstruct Leaf<X>: Base<X>\n    public init() => ()\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: PH<Leaf<i32>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n", "InvalidInlineLayout_Kd Has2.Out is PH<Leaf<(X, X)>> | InvalidConstraint_Kd PH<Leaf<(X, X)>> | InvalidInlineLayout_Kd Base<X> | InvalidConstraint_Kd let p: PH<Leaf<i32>> | InvalidConstraint_Kd PH<Leaf<i32>> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 C3-1b (F's C2 rulings 298, 302, DECISIONS Q13): the evidence step reads the inherited path's declared binding as failed, so generation stops there (no limit record, O-448 (a)); + the capture's dependent UnprovenConstraint_Kd (the memoized summary drops the failed hop's limit: PLAN follow-up)
    public void AnOwnedFailureOnAResolutionUsingItselfIsDerived(string id, string declarations, string records)
        => Assert.Equal((id, records), (id, string.Join(" | ", Records(declarations + "\n" + Main + "    return\n"))));

    // DECISIONS BU-76 (BU-61's first part wins): the settle rests a listed use on its held cause's first failed candidate only when no
    // site recorded a part for it first; a recorded part is kept, here one that is not Invalid, so the use is InternalInvariant_Kd.
    [Fact]
    public void TheSettleKeepsAPartAnotherSiteRecorded()
    {
        var c = CompilationTestHelper.ParseSuccess("contract Has2\n    associate Out\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\n" +
            "struct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nfunc use(s: i32) -> ()\n    let g: () -> () = func [s] () => ()\n    return\n\n" + Main + "    return\n");
        c.Bind();
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "g");
        var recorded = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "use");
        var impls = (ImplTable)typeof(Binding).GetField("impls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var held = impls.Paths.Single(static x => x.Type.Name == "W");
        Assert.Equal((BindingState.Invalid, BindingState.Resolved), (c.Semantics.StateOf(((ICheckCause)held).Use), c.Semantics.StateOf(recorded)));
        typeof(Binding).GetMethod("AddPendingPrerequisite", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c.Binding, [use, recorded]);
        typeof(Binding).GetMethod("FailDerived", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Koto), typeof(ICheckCause)])!.Invoke(c.Binding, [use, held]);
        typeof(Binding).GetMethod("SettleDerivedUses", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c.Binding, []);
        Assert.Equal(BindingFailure.Internal, c.Semantics.FailureOf(use));
    }

    // DECISIONS BU-76: FailDerived snapshots the held cause's candidates in a pooled list, so a warm rebind that derives a use from the
    // hops of a resolution using itself, and settles it, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindSettlesADerivedUseWithoutAllocating()
    {
        var c = CompilationTestHelper.ParseSuccess("contract Has2\n    associate Out\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\n" +
            "struct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S\n    let p: Option<PH<W<i32>>>\nfunc inspect(a: S) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n" + Main + "    return\n");
        Assert.False(c.Binding.Bind(BindingMode.Final).IsComplete);
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // SPEC 8.2.3, 8.3.3 (DECISIONS BU-22): a step resolved through an identity premise is no re-entry: W<Yi>'s conformance is a hop to
    // Yi.(Has2).Out, whose identity premise the walk reads and goes on, here 2 and 140 levels of constructed roots (they were
    // InternalInvariant_Kd at the closure). The walk ends at an associated Type of a premise conformance that nothing in scope proves
    // Owned, so the capture's record is independent (SPEC 15.2.3, 23.1.2) of the one at inspect's signature, whose premises inspect cannot
    // prove (one record per written occurrence, R7-V1c). The row with one conformance fewer (S is Invalid) pins a dependent UnprovenConstraint_Kd at the closure: an interim the owner
    // approved under PLAN G354 (3), removed with G354 (3)'s settle for Unknown Owned verdicts on a context that fails by the prologue.
    // In that row `let p` reads S's Invalid environment, which names its failed node (PLAN G651 U1), so its dependent record rests on that
    // node's reported one (rule W, DECISIONS Q13).
    [Theory]
    [InlineData(2, 3, "UnprovenConstraint_Kd S<Y> | UnprovenConstraint_Kd func [a@move] () => ()")]
    [InlineData(140, 141, "UnprovenConstraint_Kd S<Y> | UnprovenConstraint_Kd func [a@move] () => ()")]
    [InlineData(2, 2, "UnprovenConstraint_Kd PH<W<Y2>> | InvalidConstraint_Kd W<Y2> | InvalidConstraint_Kd S<Y> | UnprovenConstraint_Kd func [a@move] () => ()")] // R7-C3 (F's C2 ruling 283 (c), DECISIONS Q13): the cause's record is S's `Y1.(Has2).Out is PH<W<Y2>>` (UnprovenConstraint_Kd PH<W<Y2>>: Y2 is not proven Has2); S's `struct` record and the records over Y0, Y1, p, PH<W<Y0>> and W<Y0> go as its dependents; W<Y2>'s second and inspect's two S<Y> records change code: their formations read S's declaration, whose named unproven input is its own record (ruling 255), as Error (a read of a failed declaration, SPEC 8.3.1), not as dev's open Unknown; R7-V1c (ruling 2, Q13): the walk visits each written Type once, so W<Y2>'s second judgment (Unproven, which read the premise the first one failed) and S<Y>'s second copy go
    public void AnIdentityStepIsWalkedNotTestedForReEntry(int identities, int conformances, string records)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, identities + 1).Select(static i => "Y" + i));
        var source = "contract Has2\n    associate Out\nstruct W<X>\n    X is Has2\n    Self is Has2\n    associate Has2.Out is X.(Has2).Out\n    let x: X\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S<" + parameters + ">\n" +
            string.Concat(Enumerable.Range(0, conformances).Select(static i => $"    Y{i} is Has2\n")) +
            string.Concat(Enumerable.Range(0, identities).Select(static i => $"    Y{i}.(Has2).Out is PH<W<Y{i + 1}>>\n")) +
            "    let p: PH<W<Y0>>\nfunc inspect<" + parameters + ">(a: S<" + parameters + ">) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n\n" + Main + "    return\n";
        Assert.Equal(records.Replace("S<Y>", "S<" + parameters + ">", StringComparison.Ordinal), string.Join(" | ", Records(source)));
    }

    // SPEC 8.2.3, 8.3.3 (DECISIONS BU-22, BU-76): the steps tested for re-entry are those a chain's hops resolved. mx1 resolves
    // A1<Y>.(Has2).Out by a hop and tests Q<PH<A2<Y>>>.(K).B, then Q<i32>.(K).B, which is not deeper: valid, as its twin (it was
    // InternalInvariant_Kd at the closure). n4: an inner conformance at the end of a chain. mx2's hop step Q<H<...<i32>>>.(K).B is deeper
    // and no hop clause fails: no SPEC 8.3.3 record, so the use rests on the stored Type's located Unsupported_Kd until PLAN G369's exact
    // re-entry test (it was InternalInvariant_Kd at the closure). mx2-identity reached the same through an identity premise on a Type
    // parameter until R7-C3: the one judge forms its hop (`Y.(Has2).Out` is `PH<W<W<V>>>`, ending at `V.(Has2).Out is Owned`), so it is
    // valid and accepted (F's C2 ruling 304, the Middle's RP17 C1); mx2 keeps the located Unsupported_Kd. mx2-ctl reaches that root
    // through one level.
    [Theory]
    [InlineData("mx1", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct A2<Y>\n    Self is Has2\n    associate Has2.Out is Q<i32>\n    let y: Y\nstruct A1<Y>\n    Self is Has2\n    associate Has2.Out is Q<PH<A2<Y>>>\n    let y: Y\nstruct S<Y>\n    Y is Owned\n    let p: PH<A1<Y>>\n    func inspect(a: S<Y>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "")]
    [InlineData("mx1-twin", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct A1<Y>\n    Self is Has2\n    associate Has2.Out is Q<i32>\n    let y: Y\nstruct S<Y>\n    Y is Owned\n    let p: PH<A1<Y>>\n    func inspect(a: S<Y>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "")]
    [InlineData("n4", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Hk<X>\n    Self is K\n    associate K.B is i32\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct A1<Y>\n    Self is Has2\n    associate Has2.Out is Hk<Y>\n    let y: Y\nstruct S<Y>\n    Y is Owned\n    let p: PH<A1<Y>>\n    func inspect(a: S<Y>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "")]
    [InlineData("mx2", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct A2<Y>\n    Self is Has2\n    associate Has2.Out is Q<H<H<H<H<H<H<H<H<i32>>>>>>>>>\n    let y: Y\nstruct A1<Y>\n    Self is Has2\n    associate Has2.Out is Q<PH<A2<Y>>>\n    let y: Y\nstruct S<Y>\n    Y is Owned\n    let p: PH<A1<Y>>\n    func inspect(a: S<Y>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "Unsupported_Kd PH<A1<Y>>")]
    [InlineData("mx2-identity", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct W<X>\n    X is Has2\n    Self is Has2\n    associate Has2.Out is X.(Has2).Out\n    let x: X\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S<Y, V>\n    Y is Has2\n    V is Has2\n    Y is Owned\n    V is Owned\n    Y.(Has2).Out is PH<W<W<V>>>\n    V.(Has2).Out is Owned\n    let p: PH<W<Y>>\n    func inspect(a: S<Y, V>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "")] // R7-C3: valid; the identity path ends at V.(Has2).Out is Owned (SPEC 8.2.3, 8.3.3)
    [InlineData("mx2-ctl", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct PH<T>\n    T is Has2\n    T.(Has2).Out is K\n    let v: T.(Has2).Out.(K).B\nstruct A1<Y>\n    Self is Has2\n    associate Has2.Out is Q<H<H<H<H<H<H<H<H<i32>>>>>>>>>\n    let y: Y\nstruct S<Y>\n    Y is Owned\n    let p: PH<A1<Y>>\n    func inspect(a: S<Y>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "")]
    public void AMixedChainTestsTheStepsItsHopsResolved(string id, string declarations, string records)
        => Assert.Equal((id, records), (id, string.Join(" | ", Records(declarations + "\n" + Main + "    return\n"))));

    // SPEC 15.2.3, 5.7: a raw layer's Origin other than `self` is in OwnedOrigins, so a pointer stating a body or input Origin is not
    // Owned; `static` and a `during self` struct over an Owned T are.
    [Theory]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) => keep(p)", "UnprovenConstraint_Kd: keep(p)")]
    [InlineData("func f(p: raw/i32 during static) => keep(p)", null)]
    [InlineData("struct B<T>\n    let data: raw/T during self\n    public init() => self.data = null\nfunc f() => keep(B<i32>.init())", null)]
    public void ARawOriginIsAnOwnedOrigin(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], RawPointerSyntaxTest.Records("func keep<T>(value: T)\n    T is Owned\n    _ = value@move\n" + declarations));

    // SPEC 7.7.4, 15.2.3 (DECISIONS BU-73, PLAN G353): an erasure judges its subject's complete environment, never its call signature,
    // which the required Function Type states: a Closure whose signature fixes an enclosing function's Origin converts (p1, p11), a
    // capture of the borrowed parameter stays the one record at the value (p12), and a maker's result converts (p2, below).
    [Theory]
    [InlineData("p1", "func f(r: ref/i32) -> i32\n    let inner = func (y: ref/i32 during r) => ()\n    let g: (ref/i32 during r) -> () = inner\n    return 1", null)]
    [InlineData("p11", "func f(r: ref/i32) -> i32\n    let n = 1\n    let inner = func [n] (y: ref/i32 during r) -> i32 => n\n    let g: (ref/i32 during r) -> i32 = inner\n    return 1", null)]
    [InlineData("p12", "func f(r: ref/i32) -> i32\n    let inner = func [r] (y: ref/i32 during r) -> i32 => 0\n    let g: (ref/i32 during r) -> i32 = inner\n    return 1", "UnprovenConstraint_Kd: inner")]
    public void AnErasureJudgesTheEnvironmentNotTheSignature(string id, string declarations, string? records)
        => Assert.Equal([id, .. records is null ? [] : records.Split(" | ")], [id, .. RawPointerSyntaxTest.Records(declarations)]);

    // p2: a maker's result through a call. The old Analysis' stop at it (DECISIONS R4-M159's K5: five located Unsupported_Kd, at the
    // maker's two signatures, `let c = outer(p)`, `outer(p)` and `c`) went with the MIR deciding acceptance (H5; 1a, the owner
    // 2026-11-03): the program has no record, and a Library build (every root, K11) generates h.
    [Fact]
    public void AMakersResultThroughACallConverts()
    {
        const string Declarations = "func h(p: ref/i32) -> i32\n    let outer = func (x: ref/i32) => func (y: ref/i32 during x) -> i32 => 0\n    let c = outer(p)\n    let g: (ref/i32 during p) -> i32 = c@move\n    return 1";
        Assert.Empty(RawPointerSyntaxTest.Records(Declarations));
        Assert.Contains("h", MinimalEmissionTest.GeneratedBodies(Declarations + "\npublic func main() => ()\n", OutputKind.Library, accepted: true));
    }

    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records), the verdict from SPEC. A unit other than
    // guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and removes the marker.
    [Theory]
    // SPEC 15.2.3, 8.3.2: a premise proves a stored projection Owned, through an identity step too; without it the erasure's Owned is Unknown.
    [InlineData("guard", "hp-ctl", HeldPair + "struct S<Y>\n    Y is Has2\n    Y is Owned\n    Y.(Has2).Out is Owned\n    let p: PH<Y>\n" + CaptureY, "")]
    [InlineData("guard", "hp-mo2", HeldPair + "struct S<Y, V>\n    Y is Has2\n    V is Has2\n    Y is Owned\n    V is Owned\n    Y.(Has2).Out is PH<V>\n    V.(Has2).Out is Owned\n    let p: PH<Y>\n" + CaptureYV, "")]
    [InlineData("guard", "hp-deep", HeldPair + "struct S<Y>\n    Y is Has2\n    Y is Owned\n    Y.(Has2).Out is Has2\n    Y.(Has2).Out is Owned\n    Y.(Has2).Out.(Has2).Out is Owned\n    let p: PH<Y.(Has2).Out>\n" + CaptureY, "")]
    [InlineData("guard", "hp-n", HeldPair + "struct S<Y, V>\n    Y is Has2\n    V is Has2\n    Y is Owned\n    V is Owned\n    Y.(Has2).Out is PH<V>\n    let p: PH<Y>\n" + CaptureYV, "UnprovenConstraint_Kd: let g: () -> () = func [a@move] () => ()")]

    // SPEC 8.2.3, 15.2.3, 8.3.3: each identity step resolves and every stored Type ends at i32, so Owned is Proven, at an erasure and at a call, and no
    // limit is reached. An unresolved stored Type stays its own failure (mp-bad).
    [InlineData("guard", "mp", MixedChain + MixedStore + "    func inspect(a: S<Z, V>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "")]
    [InlineData("C6", "mp-call", MixedChain + "func need<T>(x: T) -> ()\n    T is Owned\n    return\n" + MixedStore + "    func inspect(a: S<Z, V>) -> ()\n        need(a@move)\n        return\n", "", Explicit = true)]
    [InlineData("guard", "mp-two", MixedChain + MixedStore + "    func inspect(a: S<Z, V>, b: S<Z, V>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        let h: () -> () = func [b@move] () => ()\n        return\n", "")]
    [InlineData("guard", "mp-bad", MixedChain + MixedStore + "    let q: Missing\n    func inspect(a: S<Z, V>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        return\n", "*: let q: Missing | ...")]
    public void OwnedReachesAStoredProjection(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-C3 (F's C2 ruling 304, the Middle's RP17 on Q-584: B1, C2; SPEC 6.2, 8.2.3, 15.6). y1: S2's storage `V.(Has2).Out` is S2<V>
    // under its premise, so it reaches S2 again: InvalidInlineLayout_Kd at S2 (dev's; the inline walk reads the projection through the
    // one judge, and failed before the fix: accepted). y4: S2's record with a concrete Z; Z's binding's formation `S2<Z>` records over
    // S2's failure (the cycle is S2's under the identity). Dev records the same plus dependents that rest here on S2's failed premise or
    // on the failed formation `S2<Z>` (DECISIONS Q13, the Middle's RP17 re-check N1): PrerequisiteUnavailable_Kd at `let p`,
    // InvalidConstraint_Kd at `p` and a second at its Type, and InvalidConstraint_Kd at Z's `Self is Has2`. z03 (the RP17 re-check's C3):
    // a chain whose premise is only at its root A stores A again through B and C (SPEC 6.2: in A's scope the stored projection is A<V>),
    // so InvalidInlineLayout_Kd is A's, beside the premise's and A's field's InvalidConstraint_Kd records, and B and C stay valid; dev
    // accepts it (a newly rejected invalid program). da1/da2: the identified `T.(K).X` local is an i32 to A5 (dv1's counterpart, newly
    // accepted da2). m4: mx2-identity's inspect reaches A5 (MovedPlace_Kd in place of the G369 Unsupported_Kd).
    [Theory]
    [InlineData("y1", "contract Has2\n    associate Out\nstruct S2<V>\n    V is Has2\n    V.(Has2).Out is S2<V>\n    let p: V.(Has2).Out\npublic func main() => ()\n", "InvalidInlineLayout_Kd struct | InvalidConstraint_Kd V.(Has2).Out | InvalidConstraint_Kd S2<V> | InvalidConstraint_Kd V.(Has2).Out")]
    [InlineData("y4", "contract Has2\n    associate Out\nstruct S2<V>\n    V is Has2\n    V.(Has2).Out is S2<V>\n    let p: V.(Has2).Out\nstruct Z\n    Self is Has2\n    associate Has2.Out is S2<Z>\nfunc use(s: ref/S2<Z>) -> i32 => 0\npublic func main() => ()\n", "InvalidInlineLayout_Kd struct | InvalidConstraint_Kd V.(Has2).Out | InvalidConstraint_Kd S2<V> | InvalidConstraint_Kd V.(Has2).Out | InvalidConstraint_Kd S2<Z> | InvalidConstraint_Kd S2<Z>")]
    [InlineData("z03", "contract Has2\n    associate Out\nstruct C<V>\n    V is Has2\n    let c: V.(Has2).Out\nstruct B<V>\n    V is Has2\n    let b: C<V>\nstruct A<V>\n    V is Has2\n    V.(Has2).Out is A<V>\n    let a: B<V>\npublic func main() => ()\n", "InvalidInlineLayout_Kd struct | InvalidConstraint_Kd V.(Has2).Out | InvalidConstraint_Kd A<V> | InvalidConstraint_Kd let a: B<V> | InvalidConstraint_Kd B<V>")]
    [InlineData("da1", "public contract Origin\npublic struct Source\n    Self is Origin\ncontract K\n    associate X\n    Self.(K).X is i32\n    Source is Origin\nfunc sink(v: ref/i32) -> i32 => 0\nfunc f<T>(v: T.(K).X) -> i32\n    T is K\n    var c: T.(K).X = v\n    let r = c@ref\n    c = 5\n    return sink(r)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd c = 5")]
    [InlineData("da2", "public contract Origin\npublic struct Source\n    Self is Origin\ncontract K\n    associate X\n    Self.(K).X is i32\n    Source is Origin\nfunc sink(v: ref/i32) -> i32 => 0\nfunc f<T>(v: T.(K).X) -> i32\n    T is K\n    var c: T.(K).X = v\n    let r = c@ref\n    let k = sink(r)\n    c = 5\n    return k\npublic func main() => ()\n", "")]
    [InlineData("m4", "contract Has2\n    associate Out\ncontract K\n    associate B\nstruct H<X>\n    let x: X\nstruct Q<X>\n    Self is K\n    associate K.B is H<X>\nstruct W<X>\n    X is Has2\n    Self is Has2\n    associate Has2.Out is X.(Has2).Out\n    let x: X\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\nstruct S<Y, V>\n    Y is Has2\n    V is Has2\n    Y is Owned\n    V is Owned\n    Y.(Has2).Out is PH<W<W<V>>>\n    V.(Has2).Out is Owned\n    let p: PH<W<Y>>\n    func inspect(a: S<Y, V>) -> ()\n        let g: () -> () = func [a@move] () => ()\n        let b = a@move\n        return\n\npublic func main() -> ()\n    return\n", "MovedPlace_Kd a")]
    public void AnIdentifiedTypeIsOneTypeForStorageAndA5(string id, string program, string records)
        => Assert.Equal((id, records), (id, string.Join(" | ", Records(program))));

    private static string[] Records(string source) => [.. DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {Text(source, x.Span)}")];

    private static string Text(string source, SourceSpan? span) => span is { } at ? source.Substring(at.Start, at.Length) : string.Empty;
}
