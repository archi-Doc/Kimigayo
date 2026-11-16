// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Initialization and moves on MIR (A1, SPEC 15.1, 14.10.3; docs/dev/DECISIONS.md R4-M20, R4-M21): each rule with a valid
/// program and an invalid one, through the problems <c>Compilation.BuildMir</c> finds (R4 shadow: unpublished), each as its code, the first
/// line of its subject and its views; and the move paths a body forms, through <see cref="MirMoveData"/> itself.</summary>
public sealed class MirInitTest
{
    private const string Resource = "struct Resource\n    public var name: string = \"r\"\nfunc make() -> Resource => Resource.init()\n" +
        "func consume(r: Resource) => ()\nfunc inspect(r: ref/Resource) => ()\n";

    private const string Take = "func take(a: string, b: string) -> i32 => 1\nfunc once(a: string) -> i32 => 1\n";
    private const string Pair = "struct P\n    public var a: i32 = 1\n    public init() => ()\nfunc make(a: string, b: string) -> P => P.init()\n";

    // Receivers of each aggregate kind, nested ones included, and two returns of an incomplete Array, valid on the assumed path.
    private const string Components = "func fail() -> Never => $abort(\"no\")\nfunc takeA(a: Array<i32>) => ()\nfunc takeF(a: [2 of i32]) => ()\n" +
        "func takeT(t: (i32, i32)) => ()\nfunc takeE(e: E) => ()\nfunc takeOA(o: Option<Array<i32>>) => ()\nfunc takeTA(t: (i32, Array<i32>)) => ()\n" +
        "func takeAT(a: Array<(i32, i32)>) => ()\nfunc takeAA(a: Array<Array<i32>>) => ()\nfunc takeFA(a: [2 of Array<i32>]) => ()\n" +
        "func takeS(s: S) => ()\nfunc takeAS(a: Array<string>) => ()\nfunc h2a(a: Array<i32>, b: Array<i32>) => ()\n" +
        "func takeRA(a: ref/Array<i32>) => ()\nenum E\n    Two(i32, i32)\n" +
        "struct S\n    public let a: Array<i32>\n    public init(a: Array<i32>) => self.a = a@move\nfunc r1() -> Array<i32> => [fail()]\n" +
        "func r2() -> Array<i32>\n    return [5, fail()]\npublic func main() -> ()\n    let y: i32 = 5\n    let c: bool = y > 2\n";

    private const string Entries = "func fail() -> Never => $abort(\"no\")\nfunc use(n: i32) => ()\nfunc takeD(d: Dictionary<i32, i32>) => ()\n" +
        "func takeDA(d: Dictionary<i32, Array<i32>>) => ()\nfunc takeDT(d: Dictionary<i32, (i32, i32)>) => ()\n" +
        "func takeDD(d: Dictionary<i32, Dictionary<i32, i32>>) => ()\nfunc takeDS(d: Dictionary<string, string>) => ()\n" +
        "func takeKT(d: Dictionary<(i32, i32), i32>) => ()\nfunc refD(d: ref/Dictionary<i32, i32>) => ()\n" +
        "func takeTD(t: (i32, Dictionary<i32, i32>)) => ()\nfunc takeAD(a: Array<Dictionary<i32, i32>>) => ()\n" +
        "func takeOD(o: Option<Dictionary<i32, i32>>) => ()\nfunc h2d(a: Dictionary<i32, i32>, b: Dictionary<i32, i32>) => ()\n" +
        "func h2<T>(a: T, b: T) => ()\nfunc r1d() -> Dictionary<i32, i32> => [1: fail()]\n" +
        "func r2d() -> Dictionary<i32, i32>\n    return [1: 5, 2: fail()]\nfunc r3d() -> Dictionary<i32, i32> => [1: fail(), 2: 5]\n" +
        "func r4d() -> i32\n    let d: Dictionary<string, string> = [\"a\": \"b\", \"c\": fail(), \"e\": (return 1)]\n    return 2\n" +
        "func takeG<U>(u: U) => ()\nfunc takeDN(d: Dictionary<i32, Never>) => ()\nfunc takeDR(d: Dictionary<i32, Res>) => ()\nfunc takeW(w: WD) => ()\n" +
        "struct Res\n    public var v: i32\n    public init(v: i32) => self.v = v\n    drop => ()\nstruct WD\n    public var d: Dictionary<i32, i32>\n" +
        "    public var b: i32\n    public init(d: Dictionary<i32, i32>)\n        self.d = d@move\n        self.b = 3\n" +
        "public func main() -> ()\n    let y: i32 = 5\n    let c: bool = y > 2\n";

    private const string Operands = "func fail() -> Never => $abort(\"no\")\nfunc use(n: i32) => ()\nfunc takeA(a: Array<i32>) => ()\n" +
        "func refA(a: ref/Array<i32>) => ()\nfunc takeF(a: [2 of i32]) => ()\nfunc takeF0(a: [0 of i32]) => ()\nfunc takeT(t: (i32, i32)) => ()\n" +
        "func takeTT(t: (i32, (i32, i32))) => ()\nfunc takeD(d: Dictionary<i32, i32>) => ()\nfunc takeTD(t: (i32, Dictionary<i32, i32>)) => ()\n" +
        "func takeE(e: E) => ()\nfunc takeOI(o: Option<i32>) => ()\n" +
        "func takeTA(t: (i32, Array<i32>)) => ()\nfunc takeAT(a: Array<(i32, i32)>) => ()\nfunc takeAA(a: Array<Array<i32>>) => ()\n" +
        "func takeAS(a: Array<string>) => ()\nfunc takeSI(s: SI) => ()\nfunc h2a(a: Array<i32>, b: Array<i32>) => ()\nfunc h2<T>(a: T, b: T) => ()\n" +
        "enum E\n    Two(i32, i32)\nstruct SI\n    public let v: i32\n    public init(v: i32) => self.v = v\n" +
        "func rw(c: bool, y: i32) -> Array<i32>\n    return [y]\n    return [y, if c => y else => fail()]\n" +
        "public func main() -> ()\n    let y: i32 = 5\n    let c: bool = y > 2\n";

    // The declarations before a function g that reads a Place of Type Never (CodeAfterANeverPlaceReadIsCheckedOnTheAssumedPaths).
    private const string Reads = "func fail() -> Never => $abort(\"no\")\nfunc use(n: i32) -> () => ()\nfunc useR(r: ref/i32) -> () => ()\n" +
        "func takeA(a: Array<i32>) -> () => ()\nfunc takeT(t: (i32, i32)) -> () => ()\nfunc takeS(s: string) -> () => ()\n" +
        "func pass(a: Never) -> () => ()\nfunc pass2(a: Never, b: Array<i32>) -> () => ()\nfunc passM(a: Never, b: Never) -> () => ()\n" +
        "func h(a: Array<i32>, b: Never) -> i32 => 1\nfunc hs(a: string, b: Never) -> i32 => 1\n" +
        "func keep(a: ref/i32, b: Never) -> ref/i32 during a => a\nstruct SN\n    public let v: Never\n    public let w: i32\n" +
        "enum RN\n    Ok(i32)\n    Err(Never)\nstruct Box\n    public var value: string\n" +
        "    public init(value: string) => self.value = value@move\n    drop => ()\n";

    // SPEC 15.1.1: a use needs its Place initialized on every incoming runtime path.
    [Fact]
    public void AUseNeedsInitializationOnEveryPath()
    {
        const string Branches = "func use(n: i32) => ()\nfunc f(condition: bool)\n    var number: i32\n    if condition\n        number = 1\n";
        Assert.Empty(Problems(Branches + "    else\n        number = 2\n    use(number)\n"));
        Assert.Equal(["UninitializedPlace_Kd use(number) 1"], Problems(Branches + "    use(number)\n"));
    }

    // SPEC 15.1.1, 15.1.2, 15.1.4: a Move leaves its Place Moved until a reinitialization, also along a loop's back edge; a use where it
    // may have been moved is MovedPlace. A Field written into a moved value does not construct it again: the write and a whole use after
    // it are both MovedPlace. The fixture and the write bind (SPEC 6.3.6: the implicit constructor; the Field is public).
    [Fact]
    public void AMovedPlaceIsUsableAgainOnlyAfterReinitialization()
    {
        Assert.Empty(Problems(Resource + "func f()\n    var x = make()\n    consume(x@move)\n    x = make()\n    consume(x@move)\n" +
            "func g(c: bool)\n    var x = make()\n    while c\n        consume(x@move)\n        x = make()\n"));
        Assert.Equal(["MovedPlace_Kd x 1"], Problems(Resource + "func f()\n    var x = make()\n    consume(x@move)\n    inspect(x)\n"));
        Assert.Equal(["MovedPlace_Kd x 1"], Problems(Resource + "func f(c: bool)\n    var x = make()\n    while c\n        consume(x@move)\n"));
        Assert.Equal(
            ["MovedPlace_Kd x 1"],
            Problems(Resource + "func f(flag: bool)\n    var x = make()\n    if flag\n        consume(x@move)\n    inspect(x)\n"));
        var written = MirBuildTest.Compile(Resource + "func f()\n    var x = make()\n    consume(x@move)\n    x.name = \"n\"\n    inspect(x)\n");
        Assert.Empty(written.Binding.Issues);
        Assert.Equal(["MovedPlace_Kd x.name = \"n\" 1", "MovedPlace_Kd x 1"], Problems(written));
    }

    // SPEC 15.1.1: a let binding is initialized once per binding lifetime: a loop body's let starts a lifetime per iteration, one outside
    // the loop does not, and neither a Move nor a destruction resets the first initialization.
    [Fact]
    public void ALetIsInitializedOncePerBindingLifetime()
    {
        Assert.Empty(Problems(Resource + "func f(c: bool)\n    while c\n        let s = make()\n        consume(s@move)\n"));
        Assert.Equal(
            ["ReassignedLet_Kd resource = make() 1"],
            Problems(Resource + "func f()\n    let resource: Resource\n    resource = make()\n    consume(resource@move)\n    resource = make()\n"));
        Assert.Equal(["ReassignedLet_Kd s = make() 1"], Problems(Resource + "func f(c: bool)\n    let s: Resource\n    while c\n        s = make()\n"));
    }

    // SPEC 15.1.2, 15.1.3: a Partial Move leaves the value incomplete for whole-value uses until the missing path is reinitialized.
    [Fact]
    public void APartialMoveLeavesTheValueIncomplete()
    {
        const string Pair = "func see(p: (string, i32)) => ()\nfunc f()\n    var pair: (string, i32) = (\"Alice\", 30)\n    let name = pair.0@move\n" +
            "    let age = pair.1\n";
        Assert.Empty(Problems(Pair + "    pair.0 = \"Bob\"\n    see(pair)\n"));
        Assert.Equal(["MovedPlace_Kd see(pair) 1"], Problems(Pair + "    see(pair)\n"));
    }

    // SPEC 13.5.3, 15.1.1-15.1.3 (PLAN G228): `@move` marks a Copy-typed part Moved like any part: a read, a second Move or a whole use
    // after it is MovedPlace until the part is written again, also across a loop, and a Type with drop needs the part restored before
    // the drop runs, a `during self` layer's Move restored by null included (SPEC 5.7, DECISIONS R4-M110); Copies of a part move nothing.
    // Before H5 the old analysis accepted these forms; the rows pin the MIR's verdicts.
    [Fact]
    public void AMoveOfACopyTypedPartMarksItMoved()
    {
        const string Head = "func take(x: i32) => ()\nstruct D\n    public var n: i32 = 1\n    public init() => ()\n    drop => ()\n" +
            "struct Block\n    internal var data: raw/u8 during self\n    public init() => self.data = null\n    drop => ()\n";
        const string Tuple = "    var t: (i32, i32) = (1, 2)\n    take(t.0@move)\n";
        const string Loop = "func cycle(n: i32)\n    var t: (i32, i32) = (1, 2)\n    var i = 0\n    while i < n\n        take(t.0@move)\n";
        Assert.Equal(
            ["MovedPlace_Kd t.0 1", "MovedPlace_Kd t.0 1", "MovedPlace_Kd t 1", "MovedPlace_Kd a[0] 1", "MovedPlace_Kd t.0 1",
                "MovedPlace_Kd var d = D.init() 1", "MovedPlace_Kd var b = Block.init() 1"],
            Problems(Head + "func read()\n" + Tuple + "    let b = t.0\nfunc twice()\n" + Tuple + "    take(t.0@move)\nfunc whole()\n" + Tuple +
                "    let u = t@move\nfunc element()\n    var a: [2 of i32] = [1, 2]\n    take(a[0]@move)\n    let x = a[0]\n" + Loop +
                "        i += 1\nfunc dropped()\n    var d = D.init()\n    take(d.n@move)\nfunc layer()\n    var b = Block.init()\n    let w = b.data@move\n"));
        Assert.Empty(Problems(Head + "func restored()\n" + Tuple + "    t.0 = 5\n    let u = t@move\nfunc copies()\n    var t: (i32, i32) = (1, 2)\n" +
            "    let a = t.0\n    let b = t.0\n    let u = t@move\nfunc elementRestored()\n    var a: [2 of i32] = [1, 2]\n    take(a[0]@move)\n" +
            "    a[0] = 3\n    let x = a[0]\n" + Loop + "        t.0 = i\n        i += 1\n    let u = t@move\nfunc dropped()\n" +
            "    var d = D.init()\n    take(d.n@move)\n    d.n = 3\nfunc layer()\n    var b = Block.init()\n    let w = b.data@move\n    b.data = null\n"));
    }

    // SPEC 6.3, 15.1.2: a constructor builds its object from fresh storage: a base and every own Field placed before it completes
    // (an implicit constructor and an empty struct's included); a missing Field is reported at the constructor. Writing a part of an
    // aggregate that was never constructed is reported at the write.
    [Fact]
    public void AConstructorPlacesEveryField()
    {
        const string Valid = "open struct Base\n    public var count: i32\n    public let name: string\n    protected init(name: string)\n" +
            "        self.name = name@move\n        self.count = 1\nstruct Leaf : Base\n    public var extra: i32 = 7\n" +
            "    public init(name: string, extra: i32) : base(name@move)\n        self.extra = extra\nstruct Plain\n    public var x: i32 = 1\n" +
            "struct Empty\n    public init()\n        ()\nfunc make() -> i32\n    let leaf = Leaf.init(\"a\", 2)\n    let plain = Plain.init()\n" +
            "    let empty = Empty.init()\n    return leaf.extra + plain.x\n";
        Assert.Empty(Problems(Valid));
        Assert.Equal(
            ["UninitializedPlace_Kd public init(a: i32) 1", "UninitializedPlace_Kd p.0 = 1 1"],
            Problems("struct Missing\n    public var a: i32\n    public var b: i32\n    public init(a: i32)\n        self.a = a\n" +
                "func f()\n    var p: (i32, i32)\n    p.0 = 1\n"));
    }

    // SPEC 11.7: a constructor places a let Field or one with a custom set once; a var with the standard set may be replaced.
    [Fact]
    public void AConstructorPlacesALetOrCustomSetFieldOnce()
    {
        Assert.Empty(Problems("struct S\n    var value: i32\n    let name: i32\n    public init()\n        self.value = 1\n        self.value = 2\n" +
            "        self.name = 3\nlet s = S.init()\n"));
        Assert.Equal(
            ["ReassignedLet_Kd self.value = 2 1", "ReassignedLet_Kd self.level = 2 1"],
            Problems("struct S\n    let value: i32\n    public init()\n        self.value = 1\n        self.value = 2\nstruct C\n    public var level: i32\n" +
                "        set(self: uniq/Self, value: i32) -> ()\n            storage = value\n    public init()\n        self.level = 1\n" +
                "        self.level = 2\n"));
    }

    // SPEC 6.3.5: a constructor's object is complete at each exit it requests, its body's end or a return, before the Scope Exit that runs
    // its defers, which cannot supply a missing Field: the problem is at the return and at the constructor, once for an early return; an
    // exit request only the checks reach delivers nothing (SPEC 14.10.3).
    [Fact]
    public void AConstructorIsCompleteWhereItRequestsItsExit()
    {
        Assert.Empty(Problems("struct S\n    var value: i32\n    public init(flag: bool)\n        defer => self.value = 2\n        self.value = 1\n" +
            "        if flag => return\ngroup G\n    public func stop() -> Never\n        loop => ()\nstruct T\n    var value: i32\n    public init()\n" +
            "        G.stop()\n        return\n"));
        Assert.Equal(
            ["UninitializedPlace_Kd return 1", "UninitializedPlace_Kd public init(flag: bool) 1"],
            Problems("struct S\n    var value: i32\n    public init(flag: bool)\n        defer => self.value = 1\n        if flag => return\n"));
        Assert.Equal(
            ["UninitializedPlace_Kd return 1"],
            Problems("struct S\n    var value: i32\n    public init(flag: bool)\n        if flag => return\n        self.value = 1\n"));
    }

    // SPEC 6.3.5, 23.1.2 (PLAN G105, G522): a Field Binding formed no stored Type for, its initializer failed in the parser or in
    // Binding or its Type annotation unresolved, is a part of a rejected declaration that its constructor places nothing into
    // (MirBuilder.Stored), so no exit requires it: the synthesized init() (SPEC 6.3.6, whatever the initializer's validity), an explicit
    // one, also one that never sets the annotated Field, a generic and a nested struct's report only the Field's own failure; another Field
    // an explicit init leaves unset is still reported (fail-before: UninitializedPlace_Kd at the struct or the init for the failed Field).
    [Theory]
    [InlineData("struct Node\n    var next = (1 x)\n", "")]
    [InlineData("struct Node\n    var next = undefinedName\n", "")]
    [InlineData("struct Node\n    var next = (1 x)\n    var k: i32\n    init(k: i32) => self.k = k\n", "")]
    [InlineData("struct S\n    var a: Missing\n    var k: i32\n    init() => self.k = 1\n", "")]
    [InlineData("struct Box<T>\n    var next = (1 x)\nfunc f()\n    let b = Box<i32>.init()\n", "")]
    [InlineData("struct Outer\n    public let k: i32 = 1\n    struct Inner\n        var next = (1 x)\n", "")]
    [InlineData("struct Node\n    var next = (1 x)\n    var k: i32\n    var m: i32\n    init(k: i32) => self.k = k\n", "UninitializedPlace_Kd init(k: i32) => self.k = k 1")]
    public void AConstructorRequiresNoFieldWithoutAStoredType(string source, string expected)
    {
        var c = CompilationTestHelper.Parse(source);
        c.Bind();
        var own = TestDiagnostics.Of(c).Select(static x => x.Code).Concat(c.Binding.Issues.Select(static x => x.Code.ToString()));
        Assert.Contains(own, static x => x is nameof(DiagnosticCode.ExpectedSyntax_Kd) or nameof(DiagnosticCode.UnresolvedBinding_Kd));
        Assert.Equal(expected.Length == 0 ? [] : [expected], Problems(c));
    }

    // SPEC 15.1.3, 15.1.5: a Move needs a Move Path: a constant index of a fixed array has one, a runtime index of an Array has none
    // (StaticMovePathRequired_Kd), and such a Move changes no state, so a second one is not MovedPlace; a raw pointer's Take is untracked
    // (SPEC 5.2.3), also of a pointer read through a borrow (PLAN G222), and so is a Take of a part its storage holds (TyCtxt.RawPart: a
    // fixed array's element), while an Array's element below it has no Move Path (SPEC 4.7.1; PLAN G672). A Place a function publishes
    // never offers Take, whatever root holds its reference: its bare Move is SPEC 3.5's error and its explicit one a shared path's
    // (SPEC 15.1.5), both Binding's since R6-check H1 (DECISIONS BU-84; TransferJudgmentTest), so no MIR Move Path is asked. A referent
    // through a reference Field offers no Take either, also inside a raw Place: its bare Move is Binding's TransferRequired_Kd since H1
    // (G222's MIR StaticMovePathRequired_Kd before).
    [Fact]
    public void AMoveNeedsAMovePath()
    {
        const string Lens = "struct Lens {source}\n    internal let to: uniq/string during source\n    internal let at: raw/string during source\n" +
            "    internal init(to: uniq/string during source, at: raw/string during source)\n        self.at = at\n        self.to = to\n";
        Assert.Empty(Problems("func take(s: string) => ()\nfunc f()\n    var arr: [2 of string] = [\"a\", \"b\"]\n    take(arr[0]@move)\n" +
            "    arr[0] = \"c\"\n    let whole = arr@move\n"));
        Assert.Empty(Problems("func take(s: string) => ()\nfunc f(pointer: raw/string)\n    unsafe\n        let first = (*pointer)@move\n" +
            "        let second = (*pointer)@move\n        take(first@move)\n        take(second@move)\n"));
        Assert.Empty(Problems(Lens + "func f(lens: uniq/Lens)\n    unsafe => _ = (*lens.at)@move\n"));
        Assert.Equal(
            ["StaticMovePathRequired_Kd (*p)[0] 1"],
            Problems("func f(p: raw/Array<string>, q: raw/[2 of string])\n    unsafe\n        _ = (*q)[1]@move\n        _ = (*p)[0]@move\n"));
        var lent = MirBuildTest.Compile(
            Lens + "func f(lens: uniq/Lens)\n    let s = lens.to@follow\nfunc g(p: raw/Lens)\n    unsafe\n        let s = (*p).to@follow\n");
        Assert.Equal(
            ["TransferRequired_Kd lens.to@follow", "TransferRequired_Kd (*p).to@follow"],
            lent.Binding.Issues.Select(static x => $"{x.Code} {x.Node.ToString().Split('\n')[0]}"));
        Assert.Empty(Problems(lent));
        Assert.Equal(
            ["StaticMovePathRequired_Kd values[i] 1", "StaticMovePathRequired_Kd values[i] 1"],
            Problems("func take(s: string) => ()\nfunc f(values: Array<string>, i: isize)\n    take(values[i]@move)\n    take(values[i]@move)\n"));
    }

    // SPEC 7.2.3 (DECISIONS R4-M69): a default cannot Move its prepared slot: an explicit Move of a preceding parameter or of its owned
    // contents in the template (R4-M69's: a fixed array's element or an Array's, whose element also offers no Take, SPEC 15.1.5),
    // parenthesized or not, is DefaultArgumentMove_Kd, and an inspection of it is valid. Each is the template's check: the calls' inline
    // copies, in a caller of two views, add none. A bare one is Binding's DefaultArgumentMove_Kd, and a bare one past a borrow the parameter
    // holds (StaticMovePathRequired_Kd before R6-check H1) Binding's TransferRequired_Kd (SPEC 3.5, 7.2.3; TransferJudgmentTest).
    [Fact]
    public void ADefaultCannotMoveItsPreparedSlot()
    {
        var c = MirBuildTest.Compile("func moved(x: string, y: string = x@move) => ()\nfunc bare(x: string, y: string = (x)@move) => ()\n" +
            "func part(p: (string, i32), y: string = p.0@move) => ()\nfunc elem(a: [2 of string], y: string = a[0]@move) => ()\n" +
            "func dyn(a: Array<string>, y: string = a[0]@move) => ()\nfunc peek(a: [2 of string], y: string = Text.toString(a[0])) => ()\n" +
            "func calls<s/T>(v: s/T)\n    s is ref or uniq\n    moved(\"a\")\n    bare(\"b\")\n    part((\"c\", 1))\n    elem([\"d\", \"e\"])\n" +
            "    dyn([\"f\"])\n    peek([\"g\", \"h\"])\n");
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(
            [
                "Ownership.DefaultArgumentMove DefaultArgumentMove_Kd x 1", "Ownership.DefaultArgumentMove DefaultArgumentMove_Kd (x) 1",
                "Ownership.DefaultArgumentMove DefaultArgumentMove_Kd p.0 1", "Ownership.DefaultArgumentMove DefaultArgumentMove_Kd a[0] 1",
                "Ownership.DefaultArgumentMove DefaultArgumentMove_Kd a[0] 1",
            ],
            c.BuildMir().Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
                .Select(x => $"{x.Requirement} {x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()} {x.Views}"));
    }

    // SPEC 15.1.3: a Move of a constant index forms the referenced element's Move Path; the paths follow the accesses, never the declared
    // length (an input-size property of MirMoveData).
    [Fact]
    public void AnElementMoveAddsOnlyItsOwnPath()
    {
        var c = MirBuildTest.Compile("func f()\n    var a: [1000000 of string]\n    let taken = a[42]@move\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var (flow, moves) = (new MirDataflow(new MirReach()), new MirMoveData());
        flow.Begin(body);
        moves.Build(body, flow);
        var array = body.Locals.ToArray().ToList().FindIndex(static x => x.Name == "a");
        Assert.Equal(2, moves.Paths.ToArray().Count(x => x.Local == array));
    }

    // SPEC 15.1.3 (DECISIONS R4-M21, R4-M146): Build empties the paths of a large body whole and a small body's entry by entry (MirTable),
    // and a body after either gets the paths a fresh instance gives: the paths, the flow paths and each Place's path, so no child or Place
    // of an earlier body, keyed by indices that restart, answers for it.
    [Fact]
    public void ABodyAfterALargeOneGetsAFreshInstancesPaths()
    {
        var text = new StringBuilder("struct Q\n    public var a: i32 = 1\n    public var b: (i32, i32) = (1, 2)\n    public init() => ()\n");
        text.Append("func large()\n");
        for (var i = 0; i < 600; i++)
        {
            text.Append($"    var q{i} = Q.init()\n    q{i}.b.1 = {i}\n");
        }

        text.Append("func small(flag: bool)\n    var q = Q.init()\n    if flag => q.b.0 = 3\n    q.a = 2\n");
        text.Append("func other(flag: bool)\n    var q = Q.init()\n    q.a = 4\n    let t = (q.b.1, flag)\n");
        var bodies = MirBuildTest.Compile(text.ToString()).BuildMir().Bodies.ToArray();
        var (flow, moves) = (new MirDataflow(new MirReach()), new MirMoveData());
        Paths(bodies.Single(static x => x.Key.Decl?.Name == "large"), moves);
        foreach (var name in new[] { "small", "other" })
        {
            var body = bodies.Single(x => x.Key.Decl?.Name == name);
            Assert.Equal(Paths(body, new MirMoveData()), Paths(body, moves));
        }

        // A body's paths built by a MirMoveData: each path, the flow paths' count and each projected Place's path and access in use order.
        string[] Paths(MirBody body, MirMoveData data)
        {
            flow.Begin(body);
            data.Build(body, flow);
            var found = new Found(data, [.. data.Paths.ToArray().Select(static x => x.ToString()), $"flow {data.FlowCount}"]);
            for (var block = 0; block < body.Blocks.Length; block++)
            {
                for (var i = 0; i <= body.Blocks[block].Count; i++)
                {
                    MirUses.ForEach(body, new(block, i), ref found);
                }
            }

            return [.. found.Log];
        }
    }

    // SPEC 4.7.4 (DECISIONS R4-M56): a selection's receiver is checked once, where it is located: at its guard, the selection, so an
    // access through a runtime index into an uninitialized array is one problem whatever consumes the element.
    [Theory]
    [InlineData("values[i] = 9")]
    [InlineData("use(values[i])")]
    [InlineData("see(values[i]@ref)")]
    [InlineData("values[i] += 1")]
    [InlineData("let x = values[i]")]
    public void ASelectionChecksItsReceiverOnce(string statement)
    {
        var source = $"func use(n: i32) => ()\nfunc see(v: ref/i32) => ()\nfunc f(i: isize)\n    var values: [3 of i32]\n    {statement}\n";
        Assert.Equal(["UninitializedPlace_Kd values[i] 1"], Problems(source));
    }

    // SPEC 4.7.4, 15.1, 23.1 (DECISIONS R4-M56): the access through the index checks the receiver again under the selection's subject, so a
    // receiver Moved while its own index is evaluated is MovedPlace at the selection, independent of the Move's conflict with the guard,
    // A5's problem at the Move.
    [Fact]
    public void AReceiverMovedInItsIndexIsMovedAtTheSelection()
    {
        const string Source = "func take(a: (string, [1 of i32])) => ()\nfunc f() -> i32\n    let a: (string, [1 of i32]) = (\"a\", [1])\n" +
            "    let n = a.1[(label work: do\n        take(a@move)\n        exit to work 0\n    )]\n    return n\n";
        Assert.Equal(["MovedPlace_Kd a.1[( 1", "ComparisonLoanConflict_Kd a 1"], Problems(Source));
    }

    // SPEC 14.10.3: code only the checks reach is checked with the state after the transfer's operand, which is never merged into a runtime
    // path: a value consumed before a return out of a do is usable after the do (plan Appendix B C1), a default that does not complete
    // delivers nothing at its template's Return, an index whose value only the checks reach leaves its compiler temporary unwritten (not a
    // Place of the program), a value moved before a return is MovedPlace after it, and a value whose initializer or assignment diverged is
    // UninitializedPlace where only the checks read it (plan Appendix B C3): the Never value is not placed, nor is the old value dropped
    // (DECISIONS R4-M62); a call whose argument diverged moves its other arguments for the checks (SPEC 14.10.3). A loop head only the
    // checks reach joins the return's continuation with its back edge, keeping what holds on both, so a Move in the loop is MovedPlace
    // unless the loop reinitializes it; a back edge from code only the checks reach adds nothing to a head a runtime path reaches.
    [Fact]
    public void CheckOnlyCodeKeepsItsOwnState()
    {
        const string Box = "struct Box\n    var value: string\nfunc consume(box: Box) => ()\nfunc stop() -> Never\n    loop => ()\n";
        Assert.Empty(Problems(Box + "func consumedThenReturned(flag: bool, box: Box) -> i32\n    do\n        if flag\n            consume(box@move)\n" +
            "            return 1\n        return 2\n    consume(box@move)\n    return 3\nfunc peek(flag: bool, value: i32 = stop()) -> i32 => value\n" +
            "func caller() -> i32 => peek(true)\nfunc index()\n    var a: (string, [1 of i32]) = (\"held\", [0])\n    a.1[label work: do\n" +
            "        defer => loop => ()\n        exit to work 0\n    ] = 42\nfunc replaced(box: Box) -> i32\n    var b = box@move\n    b = stop()\n" +
            "    consume(b@move)\n    return 1\n"));
        Assert.Equal(
            ["MovedPlace_Kd box 1", "UninitializedPlace_Kd x 1", "UninitializedPlace_Kd y 1", "MovedPlace_Kd box 1"],
            Problems(Box + "func afterReturn(box: Box) -> i32\n    consume(box@move)\n    return 1\n    consume(box@move)\n    return 2\n" +
                "func diverged() -> i32\n    let x: i32 = stop()\n    return x\nfunc assigned() -> i32\n    var y: i32\n    y = stop()\n    return y\n" +
                "func take(box: Box, n: i32) => ()\nfunc continued(box: Box) -> i32\n    take(box@move, (return 1))\n    consume(box@move)\n    return 2\n"));
        Assert.Equal(["MovedPlace_Kd x 1"], Problems(Resource + "func g(c: bool)\n    var x = make()\n    return\n    while c\n        consume(x@move)\n"));
        Assert.Empty(Problems(Resource + "func g(c: bool)\n    var x = make()\n    return\n    while c\n        consume(x@move)\n        x = make()\n" +
            "func f(c: bool)\n    var x = make()\n    while c\n        inspect(x)\n        return\n        consume(x@move)\n"));
    }

    // SPEC 17.6.3 (PD22): a verification's records read the temps its comparison's operands were read into once, initialized and not yet
    // moved: made Places of the program (A1 reports a Var's state, never a temp's), they have no problem, where a record reading an
    // operand after the comparison's Move would be MovedPlace.
    [Fact]
    public void RecordsReadOperandsTheComparisonHasNotMoved()
    {
        var c = MirBuildTest.Compile(
            "func helper(x: i32) -> i32 => x + 1\n#Test\nfunc sample()\n    let a = 3\n    $expect(helper(1) == a)\n    $expect((a < 4))\n",
            test: true);
        var program = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1); // Unanalyzed: a version's problems are reset only with its bodies.
        var body = program.Bodies.ToArray().Single(static x => x.Key.Kind == MirBodyKind.Test);
        var read = MirBuildTest.Records(body).Select(x => body.Operands[x.Start + 1]).Where(static x => x.Kind == MirOperandKind.Copy).ToArray();
        Assert.Equal(3, read.Length);
        foreach (var operand in read)
        {
            ref readonly var local = ref body.Locals[operand.Place.Root];
            Unsafe.AsRef(in local) = local with { Kind = MirLocalKind.Var };
        }

        new MirAnalysis(c.Types).Run(program, c.SourceModules.Length);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
    }

    // SPEC 3.1.3, 14.10.3 (DECISIONS R5-M38, BU-6): a named Place of Type Never takes the acquisition Binding publishes into its node's
    // Never temp, Copy for a bare read (Never is Copy, BU-4) and Move for x@move, which keeps its effect on the assumed path: a Never
    // parameter read twice is no MovedPlace, a read after x@move is one, and a read before it is not. A MovedPlace's subject is the bare
    // read, the node that takes Copy.
    [Theory]
    [InlineData(" => g(x, x)\n", "Copy Copy", "")]
    [InlineData(" => g(x@move, x)\n", "Move Copy", "MovedPlace_Kd x 1")]
    [InlineData(" => g(x, x@move)\n", "Copy Move", "")]
    [InlineData("\n    let a = x@move\n    let b = x\n", "Move Copy", "MovedPlace_Kd x 1")]
    public void ANeverPlaceIsAcquiredAsBindingPublishes(string body, string acquisitions, string problems)
    {
        var source = "func g(a: Never, b: Never) => ()\nfunc absurd(x: Never)" + body;
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var reads = KotoTree.Walk(c.Kotonoha.RootKoto).Where(static x => x.ToString() == "x" && HirTables.AdjustOf(x).Acquire != HirAcquire.Unset);
        Assert.Equal(acquisitions, string.Join(' ', reads.Select(static x => HirTables.AdjustOf(x).Acquire)));
        var absurd = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "absurd");
        Assert.Equal(acquisitions, string.Join(' ', absurd.Operands.ToArray().Where(static x => x.Place == new MirPlace(1)).Select(static x => x.Kind)));
        Assert.All(
            program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0 && x.Code == DiagnosticCode.MovedPlace_Kd),
            x => Assert.Equal(HirAcquire.Copy, HirTables.AdjustOf(c.SyntaxOf(x.Subject)).Acquire));
        Assert.Equal(problems, string.Join(" | ", Problems(source)));
    }

    // SPEC 12.2 (DECISIONS R5-M38): a named Never Place read before a later operand builds code is acquired into its node's Never temp,
    // which secures it and the call then moves: made a Place of the program, the temp has no problem, where an unwritten one is
    // uninitialized.
    [Fact]
    public void ASecuredNeverPlaceIsWritten()
    {
        var c = MirBuildTest.Compile("func k() -> i32 => 1\nfunc g2(a: Never, b: i32) => ()\nfunc absurd(x: Never) => g2(x, k())\n");
        var program = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1); // Unanalyzed: a version's problems are reset only with its bodies.
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "absurd");
        var call = body.Blocks.ToArray().Select(static x => x.Terminator).Single(static x => x.Callee.Symbol?.Name == "g2");
        var secured = body.Operands[call.Start];
        Assert.Equal((MirOperandKind.Move, MirLocalKind.Temp), (secured.Kind, body.Locals[secured.Place.Root].Kind));
        ref readonly var local = ref body.Locals[secured.Place.Root];
        Unsafe.AsRef(in local) = local with { Kind = MirLocalKind.Var };
        new MirAnalysis(c.Types).Run(program, c.SourceModules.Length);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
    }

    // SPEC 14.9.3, 14.10.3: a read of a named Never Place does not complete, so the block that writes its Never temp ends there,
    // Unreachable with its check-only continuation, and no runtime edge reaches the code after it (fail-before: the block went on to
    // the call).
    [Fact]
    public void ANeverPlaceReadEndsItsBlock()
    {
        var c = MirBuildTest.Compile("func k() -> i32 => 1\nfunc absurd(x: Never)\n    let n = x\n    _ = k()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "absurd");
        var blocks = body.Blocks.ToArray();
        var call = Array.FindIndex(blocks, static x => x.Terminator.Callee.Symbol?.Name == "k");
        Assert.True(blocks[0].Terminator is { Kind: MirTerminatorKind.Unreachable, CheckTarget: > 0 });
        var reach = new MirReach();
        reach.Clear(blocks.Length);
        reach.Walk(body, 0, checks: false);
        Assert.True(call > 0 && !reach[call]);
        reach.Clear(blocks.Length);
        reach.Walk(body, 0, checks: true);
        Assert.True(reach[call]);
    }

    // SPEC 3.8, 15.6.2 (DECISIONS R5-M38, the owner's choice): a pattern binding of a Never payload borrows the subject in place as Binding
    // publishes it, a `ref/Never` that keeps its Loan, so the program is valid (no finding, no problem).
    [Theory]
    [InlineData("enum R\n    Ok(i32)\n    Err(Never)\nfunc f(r: R) -> i32 => match r\n    .Ok(let v) => v\n    .Err(let e) => 0\n")]
    [InlineData("func f(o: Option<Never>) -> i32 => match o\n    .Some(let n) => 1\n    .None => 0\n")]
    public void ANeverPayloadBindingIsABorrow(string source)
    {
        Assert.Empty(Problems(source));
        var body = MirBuildTest.Compile(source).BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        Assert.Contains(
            body.Statements.ToArray(),
            static x => x.Rvalue is { Kind: MirRvalueKind.Ref, Type.Components: [var referent] } && ReferenceEquals(referent, BoundType.Never));
    }

    // SPEC 14.10.3, 3.8 (DECISIONS R4-M62, R5-M38; BU-6): a read of a named Never Place supplies no normal completion, so its assumed
    // completion initializes nothing it is placed into: the Place a let, an assignment or a Never var takes from it stays uninitialized,
    // each later read of it an UninitializedPlace.
    [Theory]
    [InlineData("func absurd(x: Never) -> i32\n    let y: i32 = x\n    return y\n", "UninitializedPlace_Kd y 1")]
    [InlineData("func absurd(x: Never) -> i32\n    let y: i32\n    y = x\n    return y\n", "UninitializedPlace_Kd y 1")]
    [InlineData(
        "func g(a: Never) => ()\nfunc absurd(x: Never)\n    var y: Never = x\n    g(y)\n    g(y)\n",
        "UninitializedPlace_Kd y 1 | UninitializedPlace_Kd y 1")]
    public void ANeverReadPlacesNothing(string source, string expected)
        => Assert.Equal(expected.Split(" | "), Problems(source));

    // SPEC 15.1.2, 15.1.3, 14.10.3 (DECISIONS R4-M175, R4-M188; PLAN D-661): a construction places each component and nothing into one
    // whose operand is the Never value, also where every operand is, an Array literal's element as a fixed array's: on its assumed path
    // the value is incomplete, so a whole use of it (a call's argument, a nested component, a Move, an accessor, a read through a runtime
    // index) is UninitializedPlace_Kd, of a temp too, while a placed part's read, a real path, a whole replacement, a call taking the
    // Never value itself and the whole returned value (r1, r2) stay valid. Fail-before: dev accepted every invalid row here (an Array
    // literal's element had no component path, and a construction of Never values only was not built, its temp holding nothing).
    // The last six rows are the implementation review's (N1): a write through a runtime index neither is allowed nor restores (two
    // records), a named Never Place's read, an inline default, a Move of an incomplete let in a return, a bare Place match Subject
    // (SPEC 15.1.6), and a loop whose assumed back edge stores an incomplete Array while the head and the later Move have real paths.
    // Then the redesign's review rows (nc): a placed part of a nested component and of a let stays readable beside an unplaced one,
    // which no Place names, so its owner lacks a part (A1's Unplaced lane): a read of the Never part, a whole read of the incomplete
    // part and a whole Copy are errors; a join operand that has a value on both assumed paths is complete.
    [Theory]
    [InlineData("takeA([fail()])", "UninitializedPlace_Kd takeA([fail()]) 1")]
    [InlineData("takeA([y, fail()])", "UninitializedPlace_Kd takeA([y, fail()]) 1")]
    [InlineData("takeA([fail(), y])", "UninitializedPlace_Kd takeA([fail(), y]) 1")]
    [InlineData("h2a([y], [fail()])", "UninitializedPlace_Kd h2a([y], [fail()]) 1")]
    [InlineData("takeT((fail(), fail()))", "UninitializedPlace_Kd takeT((fail(), fail())) 1")]
    [InlineData("takeF([fail(), fail()])", "UninitializedPlace_Kd takeF([fail(), fail()]) 1")]
    [InlineData("takeE(E.Two(fail(), fail()))", "UninitializedPlace_Kd takeE(E.Two(fail(), fail())) 1")]
    [InlineData("takeS(S.init([fail()]))", "UninitializedPlace_Kd S.init([fail()]) 1")]
    [InlineData("takeOA(Option.Some([fail()]))", "UninitializedPlace_Kd takeOA(Option.Some([fail()])) 1")]
    [InlineData("takeTA((y, [fail()]))", "UninitializedPlace_Kd takeTA((y, [fail()])) 1")]
    [InlineData("takeAT([(y, fail())])", "UninitializedPlace_Kd takeAT([(y, fail())]) 1")]
    [InlineData("takeAA([[fail()]])", "UninitializedPlace_Kd takeAA([[fail()]]) 1")]
    [InlineData("takeFA([[fail()], [y]])", "UninitializedPlace_Kd takeFA([[fail()], [y]]) 1")]
    [InlineData("takeAS([\"a\", fail()])", "UninitializedPlace_Kd takeAS([\"a\", fail()]) 1")]
    [InlineData("var s: Array<i32> = [y]\n    s = [fail()]\n    takeA(s@move)", "UninitializedPlace_Kd s 1")]
    [InlineData("var t: (i32, i32) = (y, y)\n    t = (fail(), fail())\n    takeT(t)", "UninitializedPlace_Kd takeT(t) 1")]
    [InlineData("let s: Array<i32> = [y, fail()]\n    _ = s[0]", "UninitializedPlace_Kd s[0] 1")]
    [InlineData("_ = [y, fail()].length", "UninitializedPlace_Kd [y, fail()].length 1")]
    [InlineData("let i: isize = 0\n    _ = [y, fail()][i]", "UninitializedPlace_Kd [y, fail()][i] 1")]
    [InlineData("return\n    takeA([fail()])", "UninitializedPlace_Kd takeA([fail()]) 1")]
    [InlineData("takeA(if c => [fail()] else => [y, fail()])", "UninitializedPlace_Kd takeA(if c => [fail()] 1")]
    [InlineData("if c\n        fail()\n        takeA([fail()])", "UninitializedPlace_Kd takeA([fail()]) 1")]
    [InlineData("takeA([y])\n    takeAA([[y]])\n    takeAT([(y, y)])\n    takeS(S.init(fail()))\n    _ = r1()\n    _ = r2()\n    _ = (y, fail()).0", "")]
    [InlineData("takeA(if c => [y] else => [fail()])\n    let s: Array<i32> = if c => [y] else => [fail()]\n    takeA(s@move)", "")]
    [InlineData("var s: Array<i32> = [y]\n    s = [fail()]\n    s = [y]\n    takeA(s@move)\n    let t: Array<string> = [\"a\", fail()]", "")]
    [InlineData("var s: Array<i32> = [y, fail()]\n    s[1] = 5\n    takeA(s@move)", "UninitializedPlace_Kd s[1] 1 | UninitializedPlace_Kd s 1")]
    [InlineData("_ = 0\nfunc gx(x: Never) => takeA([x])", "UninitializedPlace_Kd takeA([x]) 1")]
    [InlineData("hd()\nfunc hd(a: Array<i32> = [fail()]) => ()", "UninitializedPlace_Kd hd() 1")]
    [InlineData("_ = 0\nfunc rr() -> Array<i32>\n    let t: Array<i32> = [5, fail()]\n    return t@move", "UninitializedPlace_Kd t 1")]
    [InlineData("let o: Option<Array<(i32, i32)>> = Option.Some([(y, fail())])\n    match o\n        .Some(_) => ()\n        .None => ()", "UninitializedPlace_Kd o 1")]
    [InlineData("var s: Array<i32> = [y]\n    var i: i32 = 0\n    while i < 3\n        takeRA(s)\n        i = i + 1\n        if c\n            fail()\n            s = [fail()]\n    takeA(s@move)", "")]
    [InlineData("return\n    takeA(if c => [y] else => fail())\n    takeT(if c => (y, y) else => fail())", "")]
    [InlineData("let t: (i32, (i32, i32, i32)) = (y, (y, if c => y else => y, fail()))\n    _ = t.1.1\n    _ = t.0\n    let a: (i32, i32) = (y, fail())\n    _ = a.0", "")]
    [InlineData("let t: (i32, (i32, i32, i32)) = (y, (y, if c => y else => y, fail()))\n    _ = t.1.2", "UninitializedPlace_Kd t.1.2 1")]
    [InlineData("let t: (i32, (i32, i32, i32)) = (y, (y, if c => y else => y, fail()))\n    use3(t.1)\nfunc use3(t: (i32, i32, i32)) => ()", "UninitializedPlace_Kd use3(t.1) 1")]
    [InlineData("let a: (i32, i32) = (y, fail())\n    let b: (i32, i32) = a", "UninitializedPlace_Kd a 1")]
    [InlineData("return\n    let t: (i32, (i32, i32)) = (y, (y, if c => y else => y))\n    takeT(t.1)\n    let u: (i32, (i32, i32)) = (y, (y, if c => y else => fail()))\n    _ = u.1.0", "")]
    public void AConstructionPlacesNothingIntoItsNeverComponents(string body, string expected)
        => Assert.Equal(expected.Length == 0 ? [] : expected.Split(" | "), Problems(Components + "    " + body + "\n"));

    // SPEC 12.4.3, 14.10.3, 15.1.2, 15.1.3 (DECISIONS R4-M188's Places Call; PLAN D-661's Dictionary row): a Dictionary literal's entry
    // whose key or value has no value on a path (a Never form in any entry and position, a read of a named Never Place, a value or key
    // a construction left a part of unplaced, an if with a Never arm on assumed paths only) inserts nothing, so the literal's value is
    // incomplete there and a whole use of it (a call's argument, a nested component, a Move, a borrow, an accessor, an index, an
    // iteration) is UninitializedPlace_Kd, once, at the use; a later entry's lookup and insertion are the literal's own steps and report
    // nothing, and a real path, a whole replacement, an unused or only cleaned-up literal, a call taking the Never value itself and the
    // whole returned value (r1d to r3d) stay valid. Fail-before: D-661's head accepted every invalid row here (a Places Call transferred
    // its temps and gave their state to nothing). The rows after the last group: a temp holding a complete literal on one assumed path
    // and nothing on another is taken whole as the Never value there (R4-M188; the first six failed on the unit's first head, whose
    // inserted part read as unplaced where the temp held nothing), beside a real path, its let twin and an incomplete then arm; then
    // the implementation review's N1 rows (a Never value Type, droppable values, a loop, a match, a generic Type, a struct Field);
    // last, with the aggregate-operand unit: a literal absent on one assumed path as a Tuple's component (qa1j) and a literal whose
    // entry takes a join's value inside a temp absent on another (valid).
    [Theory]
    [InlineData("takeD([1: y, 2: fail()])", "UninitializedPlace_Kd takeD([1: y, 2: fail()]) 1")]
    [InlineData("takeD([1: fail(), 2: y])", "UninitializedPlace_Kd takeD([1: fail(), 2: y]) 1")]
    [InlineData("takeD([1: y, 2: fail(), 3: y])", "UninitializedPlace_Kd takeD([1: y, 2: fail(), 3: y]) 1")]
    [InlineData("takeD([1: y, fail(): y])", "UninitializedPlace_Kd takeD([1: y, fail(): y]) 1")]
    [InlineData("takeDT([1: (y, fail())])", "UninitializedPlace_Kd takeDT([1: (y, fail())]) 1")]
    [InlineData("takeKT([(y, fail()): y])", "UninitializedPlace_Kd takeKT([(y, fail()): y]) 1")]
    [InlineData("takeDS([\"a\": \"b\", \"c\": fail()])", "UninitializedPlace_Kd takeDS([\"a\": \"b\", \"c\": fail()]) 1")]
    [InlineData("h2d([1: y], [1: y, 2: fail()])", "UninitializedPlace_Kd h2d([1: y], [1: y, 2: fail()]) 1")]
    [InlineData("h2([1: y], [1: y, 2: fail()])", "UninitializedPlace_Kd h2([1: y], [1: y, 2: fail()]) 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: fail()]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, i32> = [fail(): y]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, i32> = [fail(): fail()]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: fail(), 2: fail()]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("_ = 0\nfunc gd(x: Never)\n    let d: Dictionary<i32, i32> = [1: x]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("_ = 0\nfunc gk(x: Never)\n    let d: Dictionary<i32, i32> = [x: 1]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, Array<i32>> = [1: [fail()]]\n    takeDA(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, Dictionary<i32, i32>> = [1: [2: fail()]]\n    takeDD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let t: (i32, Dictionary<i32, i32>) = (y, [1: fail()])\n    takeTD(t@move)", "UninitializedPlace_Kd t 1")]
    [InlineData("let a: Array<Dictionary<i32, i32>> = [[1: fail()]]\n    takeAD(a@move)", "UninitializedPlace_Kd a 1")]
    [InlineData("let o: Option<Dictionary<i32, i32>> = Option.Some([1: fail()])\n    takeOD(o@move)", "UninitializedPlace_Kd o 1")]
    [InlineData("var d: Dictionary<i32, i32> = [1: y]\n    d = [1: y, 2: fail()]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: y, 2: fail()]\n    _ = d.length", "UninitializedPlace_Kd d.length 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: y, 2: fail()]\n    _ = d[1]", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: y, 2: fail()]\n    refD(d@ref)", "UninitializedPlace_Kd d@ref 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: y, 2: fail()]\n    for (k, v) in d => ()", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, i32> = [1: y, 2: fail()]\n    for (k, v) in d@move => ()", "UninitializedPlace_Kd d 1")]
    [InlineData("_ = [1: y, 2: fail()].length", "UninitializedPlace_Kd [1: y, 2: fail()].length 1")]
    [InlineData("return\n    takeD([1: y, 2: fail()])", "UninitializedPlace_Kd takeD([1: y, 2: fail()]) 1")]
    [InlineData("return\n    takeD([1: if c => y else => fail()])", "UninitializedPlace_Kd takeD([1: if c => y 1")]
    [InlineData("takeD(if c => [1: fail()] else => [1: y, 2: fail()])", "UninitializedPlace_Kd takeD(if c => [1: fail()] 1")]
    [InlineData("if c\n        fail()\n        takeD([1: y, 2: fail()])", "UninitializedPlace_Kd takeD([1: y, 2: fail()]) 1")]
    [InlineData("takeD([1: y, 2: if c => fail() else => fail()])", "UninitializedPlace_Kd takeD([1: y, 2: if c => fail() 1")]
    [InlineData("takeD([1: y, 2: y])\n    takeDT([1: (y, y)])\n    takeKT([(y, y): y])\n    let e: Dictionary<i32, i32> = [:]\n    takeD(e@move)\n    _ = r1d()\n    _ = r2d()\n    _ = r3d()\n    _ = r4d()\n    takeD(fail())", "")]
    [InlineData("takeD(if c => [1: y] else => [1: fail(), 2: y])\n    let d: Dictionary<i32, i32> = [1: fail(), 2: y]\n    let m: Dictionary<i32, i32> = [1: y, 2: fail(), 3: y]\n    _ = [fail(): y, 2: y]", "")]
    [InlineData("takeD(if c => [1: y] else => [1: y, 2: fail()])\n    let d: Dictionary<i32, i32> = if c => [1: y] else => [1: fail()]\n    takeD(d@move)\n    takeD([1: y, 2: if c => y else => fail()])", "")]
    [InlineData("var d: Dictionary<i32, i32> = [1: y]\n    d = [1: y, 2: fail()]\n    d = [1: y]\n    takeD(d@move)\n    let u: Dictionary<string, string> = [\"a\": \"b\", \"c\": fail()]\n    _ = [1: y, 2: fail()]", "")]
    [InlineData("return\n    takeD([1: y])\n    takeD([1: if c => y else => y])\n    use(if c => y else => fail())", "")]
    [InlineData("fail()\n    takeD([1: y])\n    let d: Dictionary<i32, i32> = [1: y, 2: fail()]", "")]
    [InlineData("return\n    takeD(if c => [1: y] else => fail())", "")]
    [InlineData("return\n    takeD(if c => [1: y, 2: y] else => fail())", "")]
    [InlineData("return\n    takeTD(if c => (y, [1: y]) else => fail())", "")]
    [InlineData("if c\n        fail()\n        takeD(if c => [1: y] else => fail())", "")]
    [InlineData("return\n    takeG(if c => [1: y] else => fail())", "")]
    [InlineData("return\n    refD(if c => [1: y] else => fail())", "")]
    [InlineData("takeD(if c => [1: y] else => fail())", "")]
    [InlineData("return\n    let d: Dictionary<i32, i32> = if c => [1: y] else => fail()\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("return\n    takeD(if c => [1: fail()] else => fail())", "UninitializedPlace_Kd takeD(if c => [1: fail()] 1")]
    [InlineData("let d: Dictionary<i32, Never> = [1: fail()]\n    takeDN(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("_ = 0\nfunc gv(x: Never)\n    let d: Dictionary<i32, Never> = [1: x]\n    takeDN(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("let d: Dictionary<i32, Res> = [1: Res.init(1), 2: fail()]\n    takeDR(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("_ = 0\nfunc r7(x: Never)\n    let d: Dictionary<i32, Res> = [1: Res.init(1), 2: x]\n    takeDR(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("var d: Dictionary<i32, i32> = [1: y]\n    var i: i32 = 0\n    while i < 3\n        i = i + 1\n        if c\n            fail()\n            d = [fail(): y]\n            refD(d)", "UninitializedPlace_Kd d 1")]
    [InlineData("return\n    let d: Dictionary<i32, i32> = match c\n        true => [1: y]\n        false => [1: fail()]\n    takeD(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("_ = 0\nfunc mk<T>(t: T)\n    let d: Dictionary<i32, T> = [1: t@move, 2: fail()]\n    takeG(d@move)", "UninitializedPlace_Kd d 1")]
    [InlineData("var w: WD = WD.init([1: y])\n    w.d = [1: fail()]\n    takeW(w@move)", "UninitializedPlace_Kd w 1")]
    [InlineData("takeDT([1: (y, y), 2: (y, fail())])", "UninitializedPlace_Kd takeDT([1: (y, y), 2: (y, fail())]) 1")]
    [InlineData("var d: Dictionary<i32, i32> = [1: y]\n    var i: i32 = 0\n    while i < 3\n        refD(d)\n        i = i + 1\n        if c\n            fail()\n            d = [fail(): y]\n    takeD(d@move)", "")]
    [InlineData("return\n    takeTD((y, if c => [1: y] else => fail()))", "UninitializedPlace_Kd takeTD((y, if c => [1: y] 1")]
    [InlineData("return\n    takeTD(if c => (y, [1: if c => y else => y]) else => fail())", "")]
    public void ADictionaryLiteralInsertsNoEntryWithoutAValue(string body, string expected)
        => Assert.Equal(expected.Length == 0 ? [] : [expected], Problems(Entries + "    " + body + "\n"));

    // SPEC 14.10.3, 15.1.2, 15.1.3 (DECISIONS R4-M175, R4-M188; PLAN D-661's operand row): a form that has no value on an assumed path
    // (an if or a match with a Never arm, a return in an arm, a labeled do, a loop or a labeled if whose exit or yield is of the Never
    // value, alone or below another construction) places nothing into the component that takes it, in any position of a Tuple, an
    // Array literal, a fixed array or a Case, so on the join of the assumed paths the construction is incomplete and a whole use of it
    // (a call's argument, a Move, a borrow, an accessor, an index, an iteration, the unplaced part's read) is UninitializedPlace_Kd,
    // once, at the use. The value passes on through the temps that take it whole (an exit's operand, a secured result), and a let
    // binding it is placed into, at its declaration or later, stays uninitialized on that path. A real path, both arms placing, a
    // placed part's read, an unused or only cleaned-up construction, a whole replacement, a call (a constructor's too) taking the
    // Never value itself and the whole returned value stay valid, and so does a var such a value overwrites, which keeps its value
    // on that path. Fail-before: the Dictionary unit's head accepted every invalid row here (a placing Assign transferred the
    // whole temp and gave its own state to nothing). Then a temp whose construction took a join's value on one assumed path and
    // that holds nothing on another, taken whole as the Never value there (R4-M188: the temp lacks no part, as nothing reached its
    // component there), beside its twins with an incomplete construction, a let and an outer component. Last, the review's U1-N4
    // (the redesign's unit 1): a sibling a Place names stays apart from an incomplete one under one source temp, as the source
    // counterpart of each destination's Move Path is one too: a complete x.0 or u.0 is accepted and the incomplete x.1 or u.1 is
    // UninitializedPlace_Kd, whether the incomplete part is a Never part, a construction's lacking entry or a join operand, and
    // for a deeper Move Path; the last four reach a let placed later through the arm's temp, whose complete sibling no own carry
    // tracks (the first and the third were rejected without the closure's walk, the last two by unit 1 alone).
    [Theory]
    [InlineData("return\n    takeA([if c => y else => fail()])", "UninitializedPlace_Kd takeA([if c => y 1")]
    [InlineData("return\n    takeT((y, if c => y else => fail()))", "UninitializedPlace_Kd takeT((y, if c => y 1")]
    [InlineData("return\n    takeF([y, if c => y else => fail()])", "UninitializedPlace_Kd takeF([y, if c => y 1")]
    [InlineData("return\n    takeE(E.Two(y, if c => y else => fail()))", "UninitializedPlace_Kd takeE(E.Two(y, if c => y 1")]
    [InlineData("return\n    takeOI(Option.Some(if c => y else => fail()))", "UninitializedPlace_Kd takeOI(Option.Some(if c => y 1")]
    [InlineData("return\n    takeA([if c => y else => fail(), y])", "UninitializedPlace_Kd takeA([if c => y 1")]
    [InlineData("return\n    takeA([y, if c => y else => fail(), y])", "UninitializedPlace_Kd takeA([y, if c => y 1")]
    [InlineData("return\n    takeA([y, if c => y else => fail()])", "UninitializedPlace_Kd takeA([y, if c => y 1")]
    [InlineData("return\n    takeT((if c => y else => fail(), y))", "UninitializedPlace_Kd takeT((if c => y 1")]
    [InlineData("return\n    takeT((if c => y else => fail(), if c => y else => fail()))", "UninitializedPlace_Kd takeT((if c => y 1")]
    [InlineData("return\n    h2a([y], [if c => y else => fail()])", "UninitializedPlace_Kd h2a([y], [if c => y 1")]
    [InlineData("return\n    h2((y, 1), (y, if c => y else => fail()))", "UninitializedPlace_Kd h2((y, 1), (y, if c => y 1")]
    [InlineData("return\n    takeA([(match c\n        true => y\n        false => fail()\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeA([if c => y else => (return)])", "UninitializedPlace_Kd takeA([if c => y 1")]
    [InlineData("return\n    takeA([(label w: do\n        if c => exit to w y\n        exit to w fail()\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeA([(loop\n        if c => exit y\n        exit fail()\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeA([if c => y else => (do => fail())])", "UninitializedPlace_Kd takeA([if c => y 1")]
    [InlineData("return\n    takeA([(label s: if c\n        yield to s y\n    else\n        yield to s fail()\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeTT((y, if c => (y, y) else => fail()))", "UninitializedPlace_Kd takeTT((y, if c => (y, y) 1")]
    [InlineData("return\n    takeAT([if c => (y, y) else => fail()])", "UninitializedPlace_Kd takeAT([if c => (y, y) 1")]
    [InlineData("return\n    takeTA((y, if c => [y] else => fail()))", "UninitializedPlace_Kd takeTA((y, if c => [y] 1")]
    [InlineData("return\n    takeAT([(y, if c => y else => fail())])", "UninitializedPlace_Kd takeAT([(y, if c => y 1")]
    [InlineData("return\n    takeTT((y, (y, if c => y else => fail())))", "UninitializedPlace_Kd takeTT((y, (y, if c => y 1")]
    [InlineData("return\n    takeAA([[if c => y else => fail()]])", "UninitializedPlace_Kd takeAA([[if c => y 1")]
    [InlineData("return\n    use((y, if c => y else => fail()).1)", "UninitializedPlace_Kd use((y, if c => y 1")]
    [InlineData("return\n    let t: (i32, i32) = (y, if c => y else => fail())\n    use(t.1)", "UninitializedPlace_Kd use(t.1) 1")]
    [InlineData("return\n    let t: (i32, i32) = (y, if c => y else => fail())\n    takeT(t)", "UninitializedPlace_Kd takeT(t) 1")]
    [InlineData("return\n    let a: Array<i32> = [y, if c => y else => fail()]\n    takeA(a@move)", "UninitializedPlace_Kd a 1")]
    [InlineData("return\n    let a: Array<i32> = [y, if c => y else => fail()]\n    refA(a@ref)", "UninitializedPlace_Kd a@ref 1")]
    [InlineData("return\n    _ = [y, if c => y else => fail()].length", "UninitializedPlace_Kd [y, if c => y 1")]
    [InlineData("return\n    let a: Array<i32> = [y, if c => y else => fail()]\n    use(a[0])", "UninitializedPlace_Kd a[0] 1")]
    [InlineData("return\n    let f: [2 of i32] = [y, if c => y else => fail()]\n    use(f[1])", "UninitializedPlace_Kd use(f[1]) 1")]
    [InlineData("return\n    let i: isize = 0\n    let f: [2 of i32] = [y, if c => y else => fail()]\n    use(f[i])", "UninitializedPlace_Kd f[i] 1")]
    [InlineData("var a: Array<i32> = [y]\n    return\n    a = [y, if c => y else => fail()]\n    takeA(a@move)", "UninitializedPlace_Kd a 1")]
    [InlineData("return\n    takeAS([\"a\", if c => \"b\" else => fail()])", "UninitializedPlace_Kd takeAS([\"a\", if c => \"b\" 1")]
    [InlineData("return\n    for v in [y, if c => y else => fail()] => ()", "UninitializedPlace_Kd for v in [y, if c => y 1")]
    [InlineData("return\n    takeA([(label w: do\n        defer => use(y)\n        if c => exit to w y\n        exit to w fail()\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeA([(label w: do\n        defer => use(y)\n        exit to w (if c => y else => fail())\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeA([(label w: do\n        exit to w (if c => y else => fail())\n    )])", "UninitializedPlace_Kd takeA([( 1")]
    [InlineData("return\n    takeT((y, (loop\n        exit (if c => y else => fail())\n    )))", "UninitializedPlace_Kd takeT((y, ( 1")]
    [InlineData("return\n    let x: i32 = (label w: do\n        defer => use(y)\n        exit to w (if c => y else => fail())\n    )\n    use(x)", "UninitializedPlace_Kd use(x) 1")]
    [InlineData("let x: i32\n    return\n    x = if c => y else => fail()\n    use(x)", "UninitializedPlace_Kd use(x) 1")]
    [InlineData("let s: Array<i32>\n    return\n    s = if c => [y] else => fail()\n    takeA(s@move)", "UninitializedPlace_Kd s 1")]
    [InlineData("let x: i32\n    return\n    x = if c => y else => y\n    use(x)\n    takeA([(label w: do\n        exit to w (if c => y else => y)\n    )])\n    let z: i32 = (label w: do\n        defer => use(y)\n        exit to w (if c => y else => y)\n    )\n    use(z)", "")]
    [InlineData("takeA([if c => y else => fail()])\n    takeT((y, if c => y else => fail()))\n    takeF([y, if c => y else => fail()])\n    takeE(E.Two(y, if c => y else => fail()))\n    takeOI(Option.Some(if c => y else => fail()))\n    takeF([2 of if c => y else => fail()])", "")]
    [InlineData("return\n    takeA([if c => y else => y])\n    takeSI(SI.init(if c => y else => fail()))\n    use(if c => y else => fail())\n    h2((y, 1), (y, y))\n    takeTT((y, if c => (y, y) else => (y, y)))\n    takeTT((y, (y, y)))\n    use((y, if c => y else => fail()).0)\n    _ = (y, if c => y else => fail())\n    takeF0([0 of if c => y else => fail()])\n    _ = rw(c, y)", "")]
    [InlineData("return\n    takeA([(label w: do\n        if c => exit to w y\n        exit to w y\n    )])\n    takeA([(label w: do\n        if c => exit to w y\n        fail()\n        exit to w y\n    )])", "")]
    [InlineData("return\n    let t: (i32, i32) = (y, if c => y else => fail())\n    use(t.0)\n    let f: [2 of i32] = [y, if c => y else => fail()]\n    use(f[0])\n    let a: Array<i32> = [y, if c => y else => fail()]\n    let s: Array<string> = [\"a\", if c => \"b\" else => fail()]", "")]
    [InlineData("var a: Array<i32> = [y]\n    return\n    a = [y, if c => y else => fail()]\n    a = [y]\n    takeA(a@move)", "")]
    [InlineData("var x: i32 = 1\n    var a: Array<i32> = [y]\n    var b: Array<i32> = [y]\n    return\n    x = if c => y else => fail()\n    use(x)\n    a = if c => [y] else => fail()\n    takeA(a@move)\n    b[0] = if c => y else => fail()\n    takeA(b@move)", "")]
    [InlineData("var x: i32 = 1\n    var t: (i32, i32) = (y, y)\n    return\n    x = (label w: do\n        defer => use(y)\n        if c => exit to w y\n        exit to w fail()\n    )\n    use(x)\n    t = (label w: do\n        defer => use(y)\n        if c => exit to w (y, y)\n        exit to w fail()\n    )\n    takeT(t)", "")]
    [InlineData("_ = 0\nfunc gn(x: Never, c: bool, y: i32) => takeA([if c => y else => x])", "")]
    [InlineData("return\n    takeT(if c => (y, if c => y else => y) else => fail())\n    takeA(if c => [if c => y else => y] else => fail())\n    takeTT(if c => (y, (y, if c => y else => y)) else => fail())", "")]
    [InlineData("return\n    takeT(if c => (y, if c => y else => fail()) else => fail())", "UninitializedPlace_Kd takeT(if c => (y, if c => y 1")]
    [InlineData("return\n    let t: (i32, i32) = if c => (y, if c => y else => y) else => fail()\n    takeT(t)", "UninitializedPlace_Kd takeT(t) 1")]
    [InlineData("return\n    takeTT((y, if c => (y, if c => y else => y) else => fail()))", "UninitializedPlace_Kd takeTT((y, if c => (y, if c => y 1")]
    [InlineData("return\n    let x: ((i32, i32), (i32, i32)) = if c => ((y, if c => y else => y), (y, fail())) else => ((y, y), (y, y))\n    _ = x.1.0\n    takeT(x.0)", "")]
    [InlineData("return\n    let x: ((i32, i32), (i32, i32)) = if c => ((y, if c => y else => y), (y, fail())) else => ((y, y), (y, y))\n    _ = x.1.0\n    takeT(x.1)", "UninitializedPlace_Kd takeT(x.1) 1")]
    [InlineData("return\n    let u: (Dictionary<i32, i32>, Dictionary<i32, i32>) = if c => ([1: y], [2: fail()]) else => ([1: y], [2: y])\n    takeD(u.0@move)", "")]
    [InlineData("return\n    let u: (Dictionary<i32, i32>, Dictionary<i32, i32>) = if c => ([1: y], [2: fail()]) else => ([1: y], [2: y])\n    takeD(u.1@move)", "UninitializedPlace_Kd u.1 1")]
    [InlineData("return\n    let u: (Dictionary<i32, i32>, (i32, i32)) = if c => ([1: fail()], (y, y)) else => ([1: y], (y, y))\n    takeT(u.1)", "")]
    [InlineData("return\n    let u: (Dictionary<i32, i32>, (i32, i32)) = if c => ([1: fail()], (y, y)) else => ([1: y], (y, y))\n    takeD(u.0@move)", "UninitializedPlace_Kd u.0 1")]
    [InlineData("return\n    let x: ((i32, i32), (i32, i32)) = if c => ((y, if c => y else => y), (y, if c => y else => fail())) else => ((y, y), (y, y))\n    takeT(x.0)", "")]
    [InlineData("return\n    let x: ((i32, i32), (i32, i32)) = if c => ((y, if c => y else => y), (y, if c => y else => fail())) else => ((y, y), (y, y))\n    takeT(x.1)", "UninitializedPlace_Kd takeT(x.1) 1")]
    [InlineData("return\n    let x: (((i32, i32), i32), (i32, i32)) = if c => (((y, if c => y else => y), y), (y, fail())) else => (((y, y), y), (y, y))\n    takeT(x.0.0)", "")]
    [InlineData("return\n    let x: (i32, (i32, i32))\n    x = if c => (fail(), (y, if c => y else => y)) else => (y, (y, y))\n    takeT(x.1)", "")]
    [InlineData("return\n    let x: (i32, (i32, i32))\n    x = if c => (fail(), (y, if c => y else => y)) else => (y, (y, y))\n    use(x.0)", "UninitializedPlace_Kd use(x.0) 1")]
    [InlineData("return\n    let x: (i32, (i32, Dictionary<i32, i32>))\n    x = if c => (fail(), (y, [1: y])) else => (y, (y, [1: y]))\n    takeTD(x.1@move)", "")]
    [InlineData("return\n    let u: (Dictionary<i32, i32>, Dictionary<i32, i32>)\n    u = if c => ([1: y], [2: fail()]) else => ([1: y], [2: y])\n    takeD(u.0@move)", "")]
    public void AnOperandWithoutAValuePlacesNothingIntoItsComponent(string body, string expected)
        => Assert.Equal(expected.Length == 0 ? [] : [expected], Problems(Operands + "    " + body + "\n"));

    // SPEC 14.9.3, 14.10.3 (DECISIONS R5-M38, R4-M175): a read or an unadapted acquisition of a Place of Type Never does not
    // complete (a parameter read bare, placed at another Type, discarded, by @move, @copy or @Never, as a call's argument, a
    // construction's, an assignment's or an operator's operand, a condition or a match Subject; a Field, a Tuple, Array or
    // fixed-array element; a by-value binding of a Never payload and @follow of a ref/Never; a: T under T is Never), so no real
    // path continues after it and what follows is checked on the join of the assumed paths: a let a value absent on the else arm
    // initializes stays uninitialized there (UninitializedPlace_Kd at its use), a value Moved on that arm is Moved (MovedPlace_Kd),
    // a Loan formed on it is live (the conflict at the write), in a nested block and a loop too, an aggregate's operand absent on
    // the else arm leaves the aggregate incomplete and a let placed later from it uninitialized (the aggregate-operand rule, as
    // after a Never call or a return). Both arms placing, a read of a non-Never Place, a real path into the code (an omitted else, a while's false
    // path), a copy of a ref/Never binding and an explicit borrow stay valid. A statement if whose else arm ends in a Never call
    // (rows sx-sy1) does not continue to its join, after a Never read as after a Never call (SPEC 14.10.3: assumed paths add no
    // implicit result; R4-M175). A require's failure body that ends in such a read does not continue (SPEC 14.11), and a layer
    // with its own drop left incomplete after it has no runtime Drop (R4-M6a). Fail-before: the block went on after the read, so
    // the joins after it took only the arms that complete (every invalid row but tn3, tx and the boundary rows bf-bla, xq3, xdn
    // was accepted) and xq1, xq2 and xd were rejected.
    [Theory]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rp
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rpv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n: i32 = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rpi
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n: i32 = x\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rpiv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rd
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rdv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@move\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rm
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@move\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rmv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@copy\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rc
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@copy\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rcv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@Never\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rn
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@Never\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rnv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    pass(x)\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // ra
    [InlineData("func g(x: Never, y: i32, c: bool)\n    pass(x)\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rav
    [InlineData("func g(x: Never, y: i32, c: bool)\n    use(x)\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // ru
    [InlineData("func g(x: Never, y: i32, c: bool)\n    use(x)\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // ruv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = (y, x)\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rt
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = (y, x)\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rtv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var z: i32 = 1\n    z = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rs
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var z: i32 = 1\n    z = x\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rsv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let z = x + y\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // ro
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let z = x + y\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rov
    [InlineData("func g(s: SN, y: i32, c: bool)\n    let n = s.v\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rf
    [InlineData("func g(s: SN, y: i32, c: bool)\n    let n = s.v\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rfv
    [InlineData("func g(t: (i32, Never), y: i32, c: bool)\n    let n = t.1\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rtf
    [InlineData("func g(t: (i32, Never), y: i32, c: bool)\n    let n = t.1\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rtfv
    [InlineData("func g(a: Array<Never>, y: i32, c: bool)\n    let n = a[0]\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rae
    [InlineData("func g(a: Array<Never>, y: i32, c: bool)\n    let n = a[0]\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // raev
    [InlineData("func g(f: [2 of Never], y: i32, c: bool)\n    let n = f[0]\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rfe
    [InlineData("func g(f: [2 of Never], y: i32, c: bool)\n    let n = f[0]\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rfev
    [InlineData("func g(r: RN, y: i32, c: bool)\n    match r\n        .Ok(let v) => use(v)\n        .Err(let e)\n            let n = e\n            let q: i32 = if c => y else => fail()\n            use(q)\n", "")] // rmb
    [InlineData("func g(r: RN, y: i32, c: bool)\n    match r\n        .Ok(let v) => use(v)\n        .Err(let e)\n            let n = e\n            let q: i32 = if c => y else => y\n            use(q)\n", "")] // rmbv
    [InlineData("func g(r: RN, y: i32, c: bool)\n    match r\n        .Ok(let v) => use(v)\n        .Err(let e)\n            let n = e@follow\n            let q: i32 = if c => y else => fail()\n            use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rmbf
    [InlineData("func g(r: RN, y: i32, c: bool)\n    match r\n        .Ok(let v) => use(v)\n        .Err(let e)\n            let n = e@follow\n            let q: i32 = if c => y else => y\n            use(q)\n", "")] // rmbfv
    [InlineData("func g(r: RN, y: i32, c: bool)\n    match r@move\n        .Ok(let v) => use(v)\n        .Err(let e)\n            let n = e\n            let q: i32 = if c => y else => fail()\n            use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rmbm
    [InlineData("func g(r: RN, y: i32, c: bool)\n    match r@move\n        .Ok(let v) => use(v)\n        .Err(let e)\n            let n = e\n            let q: i32 = if c => y else => y\n            use(q)\n", "")] // rmbmv
    [InlineData("func g(o: Option<Never>, y: i32, c: bool)\n    match o\n        .None => ()\n        .Some(let e)\n            let n = e\n            let q: i32 = if c => y else => fail()\n            use(q)\n", "")] // rob
    [InlineData("func g(o: Option<Never>, y: i32, c: bool)\n    match o\n        .None => ()\n        .Some(let e)\n            let n = e\n            let q: i32 = if c => y else => y\n            use(q)\n", "")] // robv
    [InlineData("func g(o: Option<Never>, y: i32, c: bool)\n    match o\n        .None => ()\n        .Some(let e)\n            let n = e@follow\n            let q: i32 = if c => y else => fail()\n            use(q)\n", "UninitializedPlace_Kd use(q) 1")] // robf
    [InlineData("func g(o: Option<Never>, y: i32, c: bool)\n    match o\n        .None => ()\n        .Some(let e)\n            let n = e@follow\n            let q: i32 = if c => y else => y\n            use(q)\n", "")] // robfv
    [InlineData("func g(o: Option<Never>, y: i32, c: bool)\n    match o@move\n        .None => ()\n        .Some(let e)\n            let n = e\n            let q: i32 = if c => y else => fail()\n            use(q)\n", "UninitializedPlace_Kd use(q) 1")] // robm
    [InlineData("func g(o: Option<Never>, y: i32, c: bool)\n    match o@move\n        .None => ()\n        .Some(let e)\n            let n = e\n            let q: i32 = if c => y else => y\n            use(q)\n", "")] // robmv
    [InlineData("func g<T>(x: T, y: i32, c: bool) -> ()\n    T is Never\n    let n = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // rg
    [InlineData("func g<T>(x: T, y: i32, c: bool) -> ()\n    T is Never\n    let n = x\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // rgv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    takeA([y, if c => y else => fail()])\n", "UninitializedPlace_Kd takeA([y, if c => y 1")] // rpa
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    takeA([y, if c => y else => y])\n", "")] // rpav
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@move\n    takeA([y, if c => y else => fail()])\n", "UninitializedPlace_Kd takeA([y, if c => y 1")] // rma
    [InlineData("func g(x: Never, y: i32, c: bool)\n    _ = x@move\n    takeA([y, if c => y else => y])\n", "")] // rmav
    [InlineData("func g(x: Never, y: i32, c: bool)\n    pass(x)\n    takeA([y, if c => y else => fail()])\n", "UninitializedPlace_Kd takeA([y, if c => y 1")] // raa
    [InlineData("func g(x: Never, y: i32, c: bool)\n    pass(x)\n    takeA([y, if c => y else => y])\n", "")] // raav
    [InlineData("func g(s: SN, y: i32, c: bool)\n    let n = s.v\n    takeA([y, if c => y else => fail()])\n", "UninitializedPlace_Kd takeA([y, if c => y 1")] // rfa
    [InlineData("func g(s: SN, y: i32, c: bool)\n    let n = s.v\n    takeA([y, if c => y else => y])\n", "")] // rfav
    [InlineData("func g(a: Array<Never>, y: i32, c: bool)\n    let n = a[0]\n    takeA([y, if c => y else => fail()])\n", "UninitializedPlace_Kd takeA([y, if c => y 1")] // raea
    [InlineData("func g(a: Array<Never>, y: i32, c: bool)\n    let n = a[0]\n    takeA([y, if c => y else => y])\n", "")] // raeav
    [InlineData("func g(x: Never, y: i32, c: bool)\n    pass2(x, [y, if c => y else => fail()])\n", "UninitializedPlace_Kd pass2(x, [y, if c => y 1")] // rarg
    [InlineData("func g(x: Never, y: i32, c: bool)\n    pass2(x, [y, if c => y else => y])\n", "")] // rargv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = y\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "")] // tv0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = y\n    takeA([y, if c => y else => fail()])\n", "")] // ta0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: Array<i32> = [y]\n    let n = x\n    use(if c => y else => h(s@move, fail()))\n    takeA(s@move)\n", "MovedPlace_Kd s 1")] // tm
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: Array<i32> = [y]\n    let n = y\n    use(if c => y else => h(s@move, fail()))\n    takeA(s@move)\n", "")] // tm0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var v: i32 = 1\n    var w: i32 = 2\n    let n = x\n    let r: ref/i32 = if c => w@ref else => keep(v@ref, fail())\n    v = 3\n    useR(r)\n", "ComparisonLoanConflict_Kd v = 3 1")] // tb
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var v: i32 = 1\n    var w: i32 = 2\n    let n = y\n    let r: ref/i32 = if c => w@ref else => keep(v@ref, fail())\n    v = 3\n    useR(r)\n", "")] // tb0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: string = \"a\"\n    let n = x\n    use(if c => y else => hs(s@move, fail()))\n    takeS(s@move)\n", "MovedPlace_Kd s 1")] // td
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: string = \"a\"\n    let n = y\n    use(if c => y else => hs(s@move, fail()))\n    takeS(s@move)\n", "")] // td0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: string = \"a\"\n    let n = x\n    use(if c => y else => hs(s@move, fail()))\n", "")] // tdd
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: string = \"a\"\n    let n = y\n    use(if c => y else => hs(s@move, fail()))\n", "")] // tdd0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let q: i32\n    do\n        let n = x\n        q = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // tn
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let q: i32\n    do\n        let n = y\n        q = if c => y else => fail()\n    use(q)\n", "")] // tn0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    if c\n        let n = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "")] // tn2
    [InlineData("func g(x: Never, y: i32, c: bool)\n    if c\n        let n = x\n    else\n        let m = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // tn3
    [InlineData("func g(x: Never, y: i32, c: bool)\n    if c\n        let n = x\n        let q: i32 = if y > 2 => y else => fail()\n        use(q)\n", "UninitializedPlace_Kd use(q) 1")] // tn4
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    loop\n        let q: i32 = if c => y else => fail()\n        use(q)\n        exit\n", "UninitializedPlace_Kd use(q) 1")] // tl
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = y\n    loop\n        let q: i32 = if c => y else => fail()\n        use(q)\n        exit\n", "")] // tl0
    [InlineData("func g(x: Never, y: i32, c: bool)\n    loop\n        let n = x\n        let q: i32 = if c => y else => fail()\n        use(q)\n        exit\n", "UninitializedPlace_Kd use(q) 1")] // tl2
    [InlineData("func g(x: Never, y: i32, c: bool)\n    while c\n        let n = x\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "")] // tl3
    [InlineData("func g(x: Never, y: i32, c: bool) -> i32\n    let n = x\n    return y\n", "")] // tr1
    [InlineData("func g(x: Never, y: i32, c: bool) -> i32\n    let n = x\n", "")] // tr2
    [InlineData("func g(x: Never, y: i32, c: bool) -> i32 => x\n", "")] // tr3
    [InlineData("func g(x: Never, y: i32, c: bool)\n    passM(x@move, x)\n", "MovedPlace_Kd x 1")] // tx
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    use(y)\n    let k: i32 = y\n    use(k)\n    takeA([y, if c => y else => y])\n", "")] // tu
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    var p: i32\n    if c\n        p = 1\n    else\n        fail()\n    use(p)\n", "")] // sx
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = x\n    var p: i32\n    if c => p = 1 else => fail()\n    use(p)\n", "")] // sx1
    [InlineData("func g(x: Never, y: i32, c: bool)\n    fail()\n    var p: i32\n    if c\n        p = 1\n    else\n        fail()\n    use(p)\n", "")] // sf
    [InlineData("func g(x: Never, y: i32, c: bool)\n    fail()\n    var p: i32\n    if c => p = 1 else => fail()\n    use(p)\n", "")] // sf1
    [InlineData("func g(x: Never, y: i32, c: bool)\n    return\n    var p: i32\n    if c\n        p = 1\n    else\n        fail()\n    use(p)\n", "")] // sr
    [InlineData("func g(x: Never, y: i32, c: bool)\n    return\n    var p: i32\n    if c => p = 1 else => fail()\n    use(p)\n", "")] // sr1
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = y\n    var p: i32\n    if c\n        p = 1\n    else\n        fail()\n    use(p)\n", "")] // sy
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = y\n    var p: i32\n    if c => p = 1 else => fail()\n    use(p)\n", "")] // sy1
    [InlineData("func g(x: Never, y: i32, c: bool)\n    fail()\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // bf
    [InlineData("func g(x: Never, y: i32, c: bool)\n    fail()\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // bfv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    return\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // br
    [InlineData("func g(x: Never, y: i32, c: bool)\n    return\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // brv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    $abort(\"no\")\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // ba
    [InlineData("func g(x: Never, y: i32, c: bool)\n    $abort(\"no\")\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // bav
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = fail()\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // bl
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = fail()\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // blv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    loop => ()\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // bw
    [InlineData("func g(x: Never, y: i32, c: bool)\n    loop => ()\n    let q: i32 = if c => y else => y\n    use(q)\n", "")] // bwv
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let n = fail()\n    let m = n\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd n 1 | UninitializedPlace_Kd use(q) 1")] // bla
    [InlineData("func g(x: Never, y: i32, c: bool)\n    require c else\n        let n = x\n    use(y)\n", "")] // xq1
    [InlineData("func g(x: Never, y: i32, c: bool)\n    require c else => pass(x)\n    use(y)\n", "")] // xq2
    [InlineData("func g(x: Never, y: i32, c: bool)\n    require c else\n        let n = y\n    use(y)\n", "RequireFallthrough_Kd let n = y 1")] // xq3
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var box = Box.init(\"a\")\n    let n = x\n    takeS(box.value@move)\n", "")] // xd
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var box = Box.init(\"a\")\n    return\n    takeS(box.value@move)\n", "")] // xdr
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var box = Box.init(\"a\")\n    let n = y\n    takeS(box.value@move)\n", "MovedPlace_Kd var box = Box.init(\"a\") 1")] // xdn
    [InlineData("func g(x: Never, y: i32, c: bool)\n    if x => use(y)\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // xif
    [InlineData("func g(x: Never, y: i32, c: bool)\n    match x\n        _ => ()\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // xms
    [InlineData("func g(r: ref/Never, y: i32, c: bool)\n    let n = r@follow\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "UninitializedPlace_Kd use(q) 1")] // xrr
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let b = x@ref\n    let q: i32 = if c => y else => fail()\n    use(q)\n", "")] // xrb
    public void CodeAfterANeverPlaceReadIsCheckedOnTheAssumedPaths(string function, string expected)
        => Assert.Equal(expected.Length == 0 ? [] : expected.Split(" | "), Problems(Reads + function));

    // SPEC 14.9.3, 14.10.3, 16.2 (DECISIONS R4-M6a; the redesign's DRV-N8): a construction leaves a part no Place names unplaced (A1's
    // Unplaced lane, which A2 never reads) only after a Never form, which ends the runtime paths, so no Drop the runtime edges reach
    // finds one, and A2 destroys each Move Path whole by its own bit; MirInit records InternalInvariant_Kd where one would. Valid: a
    // Tuple, an Array literal, a nested Tuple with a part moved out, a Dictionary literal and an overwritten var whose component is a
    // Never parameter's read, each dropped at its scope's end in the check-only code after the read, and the same after a Never call.
    // Fail-before: with the read's block runtime (unit 1), each row but the last reached its Drop by the runtime edges with the part
    // unplaced (InternalInvariant_Kd under this check).
    [Theory]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let t: (Box, Box) = (Box.init(\"a\"), x)\n")]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let s: Array<Box> = [Box.init(\"a\"), x]\n")]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let t: (Box, (Box, Box)) = (Box.init(\"a\"), (Box.init(\"b\"), x))\n    let u = t.0@move\n")]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    let d: Dictionary<i32, Box> = [1: Box.init(\"a\"), 2: x]\n")]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    var t: (Box, Box) = (Box.init(\"a\"), Box.init(\"b\"))\n    t = (Box.init(\"c\"), x)\n")]
    [InlineData("func g(x: Never, y: i32, c: bool)\n    if c\n        let t: (Box, Box) = (Box.init(\"a\"), fail())\n")]
    public void NoRuntimeDropFindsAnUnplacedPart(string function)
        => Assert.Empty(Problems(Reads + function));

    // MirInit's second invariant (the step 2 review's S2-N1): a definition the runtime edges reach never keeps its path's state, as a
    // temp holds nothing only after a Never form; a kept definition there would set MaybeInit, A2's lane, where nothing was placed. The
    // builder never emits one; made so (the else arm's placement of the join temp a let takes later turned into a StorageLive of it), the
    // let's Use keeps x's state on a runtime path: one InternalInvariant_Kd; the unmutated body has none. Fail-before: without the check
    // the mutated body records none.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoRuntimeDefinitionKeepsItsState(bool broken)
    {
        var c = MirBuildTest.Compile("func use(n: i32) => ()\nfunc f(y: i32, c: bool)\n    let x: i32\n    x = if c => y else => y\n    use(x)\n");
        var program = new MirBuilder(c).Build(); // Unanalyzed: a version's problems are reset only with its bodies.
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var statements = body.Statements.ToArray();
        var take = Array.FindIndex(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use, Place.Projection: 0 } &&
            body.Locals[x.Place.Root].Kind == MirLocalKind.Var && body.Operands[x.Rvalue.Start].Place is { Projection: 0, Root: >= 0 } operand &&
            body.Locals[operand.Root].Kind == MirLocalKind.Temp);
        Assert.True(take >= 0);
        var temp = body.Operands[statements[take].Rvalue.Start].Place;
        var placements = Enumerable.Range(0, statements.Length).Where(x => statements[x].Kind == MirStatementKind.Assign && statements[x].Place == temp).ToArray();
        Assert.Equal(2, placements.Length);
        if (broken)
        {
            Unsafe.AsRef(in body.Statements[placements[1]]) = statements[placements[1]] with { Kind = MirStatementKind.StorageLive };
        }

        new MirAnalysis(c.Types).Run(program, c.SourceModules.Length);
        var internals = program.Problems.All.ToArray().Where(x => x.Code == DiagnosticCode.InternalInvariant_Kd && x.Body == body.Ordinal);
        Assert.Equal(broken ? 1 : 0, internals.Count());
    }

    // Plan 5.6.1, SPEC 13.5.5: a PairLayer is the Place itself in the owner case and a Deref in a reference case, so writing through an
    // unset pair layer is a problem in the uniq view only; a body whose accesses no view decides is analyzed once, for every view.
    [Fact]
    public void ViewsDecidePairLayers()
    {
        Assert.Equal(
            ["UninitializedPlace_Kd v@follow = x 2", "UninitializedPlace_Kd y 3"],
            Problems("func write<s/T>(x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var v: s/T\n    v@follow = x\n" +
                "func plain<s/T>(value: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    let y: T\n    return y\n"));
        Assert.Empty(Problems(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "cases.kimi"))));
    }

    // SPEC 3.5, 3.5.1, 8.15.2 (R6-check (b2)'s acceptance moves): the owner case of a pair layer over `uniq/i32`, directly or through its
    // own binder's layer, Reborrows, and over a nested `t/U` acquires by that layer's cases, so the Place is usable again after the child's
    // last use (before (b2) each was a Move, a false MovedPlace_Kd at the second use).
    [Theory]
    [InlineData("func exclusive<s/T>(value: s/(uniq/i32 during a)) -> ()\n    s is owner or uniq\n    let local = value\n    _ = local\n" +
        "    let again = value\n    _ = again\nfunc nested<t/U, s/T>(value: s/t/U) -> ()\n    s is owner or uniq\n    t is owner or uniq\n" +
        "    U is Copy\n    let local = value\n    _ = local\n    let again = value\n    _ = again\npublic func main() -> ()\n    var n: i32 = 1\n" +
        "    let r = n@uniq\n" +
        "    exclusive<owner/i32>(r@move)\n    var m: i32 = 2\n    nested<owner/i32, owner/i32>(m)\n")]
    [InlineData("func own<s/T>(value: s/(s/(uniq/i32 during a) during b)) -> ()\n    s is owner or ref\n    let local = value@follow\n    _ = local\n" +
        "    let again = value@follow\n    _ = again\nfunc exclusive<s/T>(value: s/(uniq/i32 during a)) -> ()\n    s is owner or ref\n    let local = value\n" +
        "    _ = local\n    let again = value\n    _ = again\npublic func main() -> ()\n    var n: i32 = 1\n    let r = n@uniq\n    own<owner/i32>(r@move)\n" +
        "    var m: i32 = 1\n    let q = m@uniq\n    exclusive<owner/i32>(q@move)\n")]
    public void AnOwnerCaseReborrowLeavesItsPlaceUsable(string source) => Assert.Empty(Problems(source));

    // R4-M15a: a body whose state arena would exceed the analyses' byte limit (64 MiB) records OwnershipStorageLimit_Kd once, at the body,
    // and is not analyzed: N locals live across N branches need about N heads × 5 lanes × N/64 words, so the unset read of y is reported at
    // N = 64 and not at N = 13,500, where the limit carries its arguments, A1's arena over the limit (G213, R4-M132).
    [Fact]
    public void AnArenaAboveTheLimitStopsTheBody()
    {
        foreach (var (size, expected) in new[]
        {
            (64, "UninitializedPlace_Kd y 1"),
            (13_500, "OwnershipStorageLimit_Kd func f(flag: bool) -> i32 1 Initialization over"),
        })
        {
            var text = new StringBuilder("func f(flag: bool) -> i32\n    let y: i32\n");
            for (var n = 0; n < size; n++)
            {
                text.Append("    var v").Append(n).Append(" = ").Append(n).Append('\n');
            }

            for (var n = 0; n < size; n++)
            {
                text.Append("    if flag => v").Append(n).Append(" = 1\n");
            }

            Assert.Equal([expected], Problems(text.Append("    return y\n").ToString()));
        }
    }

    // SPEC 4.3.4, 4.9.5: noinit and an empty collection literal construct initialized values.
    [Fact]
    public void ConstructedAggregatesAreInitialized()
        => Assert.Empty(Problems("func noinit() -> i32\n    unsafe\n        var values: [4 of i32] = noinit\n        values[1] = 2\n" +
            "        return values[0]\nfunc keep(values: Array<i32>) => ()\nfunc empty()\n    let values: Array<i32> = []\n    keep(values@move)\n"));

    // SPEC 23.1.2 (DECISIONS R5-M63): a node Binding failed hides no independent problem of the children Binding resolved: a closure's
    // body is built wherever its creation stands (discarded, bound by a let, inside a let Binding rejects for its own reason, its Type
    // annotation, or a failed Index's kept operand; a result over the call's
    // own storage fails only its returned value, whose relation is a row the Origin stratum also records, DECISIONS R4-M152), and a failed
    // Index, Field, follow or `*p` evaluates its operands before its Opaque value, which does not use a moved operand again.
    [Fact]
    public void AFailedNodeKeepsTheProblemsOfItsResolvedChildren()
    {
        const string Body = "func (n: i32)\n        let t = \"x\"\n        _ = take(t@move, t@move)\n        return n@ref\n";
        Assert.Equal(["MovedPlace_Kd t 1", "UnsatisfiedOriginRelation_Kd return n@ref 1"], Problems(Take + "func f()\n    _ = " + Body));
        Assert.Equal(["MovedPlace_Kd t 1", "UnsatisfiedOriginRelation_Kd return n@ref 1"], Problems(Take + "func f()\n    let g = " + Body));
        Assert.Equal(["MovedPlace_Kd t 1", "UnsatisfiedOriginRelation_Kd return n@ref 1"], Problems(Take + "func f()\n    let g: Missing = " + Body));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Take + "func f(n: i32) -> i32\n    let s = \"x\"\n    return n[take(s@move, s@move)]\n"));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Take + Pair + "func f() -> i32\n    let s = \"x\"\n    return make(s@move, s@move).missing\n"));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Take + "func f() -> i32\n    let s = \"x\"\n    return take(s@move, s@move)@follow\n"));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Take + "func f(n: i32) -> i32\n    let s = \"x\"\n    _ = once(s@move)\n    return n[once(s@move)]\n"));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Take + "func f()\n    let s = \"x\"\n    unsafe\n        _ = *take(s@move, s@move)\n"));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Take + "func f(n: i32)\n    let s = \"x\"\n    _ = n[func (k: i32) -> i32 => take(s@move, s@move)]\n"));
        // A recovered group (the default's list item) gives the problems of the default written correctly: each Move of the prepared slot
        // (SPEC 7.2.3, R4-M69).
        string[] slot = ["DefaultArgumentMove_Kd s 1", "DefaultArgumentMove_Kd s 1"];
        Assert.Equal(slot, Problems(Take + "func g(s: string, x: i32 = (take(s@move, s@move)), y: i32 = 0) -> i32 => x\n"));
        var recovered = CompilationTestHelper.Parse(Take + "func g(s: string, x: i32 = (take(s@move, s@move)) y: i32 = 0) -> i32 => x\n");
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), Assert.Single(TestDiagnostics.Of(recovered)).Code);
        recovered.Bind();
        Assert.Equal(slot, Problems(recovered));
    }

    // SPEC 23.1.2 (DECISIONS BU-106): a rejected do's body is built before its derived value, so its moves keep their problems, within
    // the body and against a use after the do (fail-before: the body was one Opaque and neither problem was found).
    [Fact]
    public void ARejectedDoKeepsTheMovesOfItsBody()
    {
        const string Head = Take + "func f(s: string) -> i32\n    let y: i32 = do\n        _ = once(s@move)\n";
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Head + "        _ = once(s@move)\n    return 1\n"));
        Assert.Equal(["MovedPlace_Kd s 1"], Problems(Head + "    _ = once(s@move)\n    return 1\n"));
    }

    // SPEC 23.1.2 (DECISIONS R4-M88): an operation Binding left unresolved evaluates every operand before its derived value, also inside
    // an operand it did not resolve (`1 + (missing + once(s@move))`, a selection's index), so the problems of what it evaluates stay; one
    // in a statement or a typed destination, `and`, a parenthesized `if` and a selection whose `if` or labeled do index, also in
    // parentheses or under a unary operator, has no Type validate now (fail-before: a finding, which stops every analysis, or, for the
    // index, the whole selection derived), such an operand replaced with its parts. A construct, an assignment, a labeled do and a default
    // keep their forms elsewhere (a typed destination keeps the Loan its branch places), an assignment's derived value initializes its
    // target, and a failed call's operands stay replaced (PLAN G121).
    [Theory]
    [InlineData(Take + "func f(c: bool) -> bool\n    let s = \"x\"\n    return take(s@move, s@move) == 1 and missing\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f()\n    let s = \"x\"\n    _ = once(s@move) + missing\n    let t = s@move\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f(c: bool) -> i32\n    let s = \"x\"\n    return (if c => take(s@move, s@move) else => missing)\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f()\n    let s = \"x\"\n    let x: i32 = 1 + (missing + once(s@move))\n    let t = s@move\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f(v: [3 of i32]) -> i32\n    let s = \"x\"\n    return v[take(s@move, s@move) + missing]\n", "MovedPlace_Kd s 1")]
    [InlineData(
        Take + "func f(c: bool, v: [3 of i32], i: isize) -> i32\n    let s = \"x\"\n    let a = once(s@move)\n    let b = once(s@move)\n" +
        "    return v[if c => i else => missing]\n",
        "MovedPlace_Kd s 1")]
    [InlineData(
        Take + "func f(c: bool, v: [3 of i32], i: isize) -> i32\n    let s = \"x\"\n    let a = once(s@move)\n    let b = once(s@move)\n" +
        "    return v[(if c => i else => missing)]\n",
        "MovedPlace_Kd s 1")]
    [InlineData(
        Take + "func f(c: bool, v: [3 of i32], i: isize) -> i32\n    let s = \"x\"\n    let a = once(s@move)\n    let b = once(s@move)\n" +
        "    return v[-(if c => i else => missing)]\n",
        "MovedPlace_Kd s 1")]
    [InlineData(Take + Pair + "func f()\n    let s = \"x\"\n    var y: i32 = 0\n    y = make(s@move, s@move).missing\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + Pair + "func f()\n    let s = \"x\"\n    var y: i32 = 0\n    y += make(s@move, s@move).missing\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f(c: bool)\n    let s = \"x\"\n    var y: i32 = 0\n    y = if c => take(s@move, s@move) else => missing\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f(c: bool) -> i32\n    let s = \"x\"\n    let v: i32 = if c => take(s@move, s@move) else => missing\n    return v\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f()\n    let s = \"x\"\n    let x: i32 = once(s@move) + missing\n    let t = s@move\n", "MovedPlace_Kd s 1")]
    [InlineData(Take + Pair + "func f()\n    let s = \"x\"\n    _ = (make(s@move, \"y\").missing)\n    let t = s@move\n", "MovedPlace_Kd s 1")]
    [InlineData(
        "func index(a: string) -> isize => 1\nfunc f(v: [3 of i32])\n    let s = \"x\"\n    _ = (v[index(s@move)].missing)\n    let t = s@move\n",
        "MovedPlace_Kd s 1")]
    [InlineData(
        Take + "func f(c: bool, v: [3 of i32], i: isize) -> i32\n    let s = \"x\"\n    let a = once(s@move)\n    let b = once(s@move)\n" +
        "    return v[label l: do\n        if c => exit to l i\n        exit to l missing\n    ]\n",
        "MovedPlace_Kd s 1")]
    [InlineData(
        Take + "func f(c: bool, v: [3 of i32], i: isize) -> i32\n    let s = \"x\"\n    let a = once(s@move)\n    let b = once(s@move)\n" +
        "    return v[(label l: do\n        if c => exit to l i\n        exit to l missing\n    )]\n",
        "MovedPlace_Kd s 1")]

    // A labeled do or loop, an operand without a Type, is replaced with its body, whose problems are lost (PLAN G121's failed construct).
    [InlineData(
        Take + "func f() -> i32\n    let s = \"x\"\n    return other + label l: do\n        _ = once(s@move)\n        _ = once(s@move)\n" +
        "        exit to l missing\n",
        "")]
    [InlineData(
        Take + "func f(c: bool) -> i32\n    let s = \"x\"\n    _ = once(s@move)\n    _ = once(s@move)\n    return other + label l: loop\n" +
        "        if c => exit to l 1\n        exit to l missing\n",
        "MovedPlace_Kd s 1")]
    [InlineData(Take + "func f()\n    var t: string\n    t = missing\n    _ = once(t@move)\n", "")]
    [InlineData("struct S\n    var a: i32\n    public computed b: i32\n        get() -> i32 => 5\n    public init()\n        self.a = self.b\n", "UninitializedPlace_Kd self.b 1")]
    [InlineData(
        "func use(v: ref/i32) -> i32 => 1\nfunc f(c: bool) -> i32\n    var x: i32 = 1\n    let r: ref/i32 = if c => x@ref else => missing\n    x = 2\n" +
        "    return use(r)\n",
        "ComparisonLoanConflict_Kd x = 2 1")]
    [InlineData(
        Take + "func two(a: i32, b: i32) -> i32 => 1\nfunc f() -> i32\n    let s = \"x\"\n    let v: i32 = two(once(s@move), missing)\n    let t = s@move\n" +
        "    return v\n",
        "")]
    public void ADerivedValueKeepsTheProblemsOfWhatItEvaluates(string source, string expected)
        => Assert.Equal(expected.Length == 0 ? [] : expected.Split(" | "), Problems(source));

    // SPEC 13.7.1, 15.1.1, 23.1.2 (DECISIONS R5-M195; PLAN G105, G791): a write Binding rejected for its value's fit after admitting its
    // destination still places a value derived from that failure, as one whose value Binding left unresolved does: a second placement of
    // a let local or Field is reported, and a first placement, the target's later use, a constructor's end and a later write to a replaced
    // reference's old referent are not; its target's operands are evaluated (SPEC 13.7.1 step 2), so a move there is a later use's
    // MovedPlace_Kd (fail-before: the whole write was one Opaque naming its target as a use, so ReassignedLet_Kd and that MovedPlace_Kd were
    // lost and UninitializedPlace_Kd or ComparisonLoanConflict_Kd reported).
    [Theory]
    [InlineData("func f()\n    let x: i32 = 1\n    x = \"a\"\n", "ReassignedLet_Kd x = \"a\" 1")]
    [InlineData("func f()\n    let x: i32 = 1\n    x = (\"a\")\n", "ReassignedLet_Kd x = (\"a\") 1")]
    [InlineData("func f() -> i32\n    var x: i32\n    x = \"a\"\n    return x\n", "")]
    [InlineData("struct S\n    public let f: i32\n    public let n: string\n    public init()\n        self.f = \"a\"\n        self.n = \"x\"\n", "")]
    [InlineData("struct S\n    public let f: i32\n    public init()\n        self.f = 1\n        self.f = \"a\"\n", "ReassignedLet_Kd self.f = \"a\" 1")]
    [InlineData("func f() -> i32\n    var a = 1\n    var slot: ref/i32 = a@ref\n    slot = \"a\"\n    a = 3\n    return slot@follow\n", "")]
    [InlineData(
        "func k(s: string) -> isize => 0\nfunc h(s: string) -> i32\n    var a: [3 of i32] = [1, 2, 3]\n    a[k(s@move)] = \"a\"\n    let u = s@move\n" +
        "    return a[0]\n",
        "MovedPlace_Kd s 1")]
    public void ARejectedWriteKeepsItsWrite(string source, string expected)
        => Assert.Equal(expected.Length == 0 ? [] : expected.Split(" | "), Problems(source));

    // R4-M20: the words of A1's state arena for a body begun and built: its heads × the stored words of its lanes.
    internal static long ArenaWords(MirBody body, MirDataflow flow, MirMoveData moves)
        => (body.Blocks.ToArray().Select((_, b) => flow.Head(b)).Max() + 1) * (long)MirInit.Lanes * ((moves.FlowCount + 63) / 64);

    // The problems of a source's own bodies: code, the first line of the subject, the views and a storage limit's arguments.
    internal static string[] Problems(string source)
        => Problems(MirBuildTest.Compile(source));

    private static string[] Problems(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. program.Problems.All.ToArray().Select(static (x, i) => (Problem: x, Index: i)).Where(static x => x.Problem.Subject.Module == 0)
            .Select(x => $"{x.Problem.Code} {c.SyntaxOf(x.Problem.Subject).ToString().Split('\n')[0].Trim()} {x.Problem.Views}" +
                (x.Problem.Code == DiagnosticCode.OwnershipStorageLimit_Kd ? $" {MirDataflowTest.Limit(program.Problems, x.Index)}" : string.Empty))];
    }

    // The path and access MirMoveData finds for each projected Place a body uses, after the lines a log starts with.
    private readonly struct Found(MirMoveData moves, List<string> log) : IMirUses
    {
        internal List<string> Log => log;

        public void Operand(in MirOperand operand)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place is { IsStatic: false, Projection: > 0 })
            {
                log.Add($"{place} {moves.Find(place, out var how)} {how}");
            }
        }

        public void Effect(MirEffect effects, int site)
        {
        }
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests(ITestOutputHelper output)
    {
        // V16 (plan "New V-checks"): A1 is linear in the body: N 16/32/64 groups of a straight move, a branch that moves on both arms and a loop
        // that moves and reinitializes grow the allocation of the body's cold analysis (fresh components, the driver's per-body steps with A2,
        // which A1's checks replay feeds; a fresh driver's pools would hold the widest library body) linearly, with no problem; the output gives
        // the dense arena's words (R4-M20).
        // MirBuildTest.WarmRebuildAllocatesNothing covers the warm re-analysis, which BuildMir runs.
        [Theory]
        [InlineData("    let v{0} = text({0})\n    keep(v{0}@move)\n")]
        [InlineData("    let v{0} = text({0})\n    if flag => keep(v{0}@move) else => other(v{0}@move)\n")]
        [InlineData("    var v{0} = text({0})\n    while flag\n        keep(v{0}@move)\n        v{0} = text({0})\n")]
        public void InitializationScalesLinearly(string group)
        {
            var (sizes, work, arena) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
            var analyses = new (MirBody Body, MirDataflow Flow, MirMoveData Moves, MirProgram Sink)[3];
            var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
            {
                var text = new StringBuilder("func text(n: i32) -> string => \"\\(n)\"\nfunc keep(s: string) => ()\nfunc other(s: string) => ()\n");
                text.Append("func work(flag: bool)\n");
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, group, n);
                }

                var c = MirBuildTest.Compile(text.ToString());
                var program = c.BuildMir();
                Assert.Empty(program.Problems.All.ToArray());
                var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "work");
                work[i] = body.Blocks.Length + body.Statements.Length;
                var (flow, moves, cases, sink) = (new MirDataflow(new MirReach()), new MirMoveData(), new MirCases(c.Types), new MirProgram());
                var drops = new MirDrops(moves, cases);
                var init = new MirInit(flow, moves, cases, drops);
                analyses[i] = (body, flow, moves, sink);
                return () =>
                {
                    flow.Begin(body);
                    moves.Build(body, flow);
                    drops.Begin(body, sink);
                    cases.Select(body, 0);
                    Assert.True(init.Run(body, 0, sink.Problems));
                    drops.Judge(0);
                    drops.End();
                };
            });
            for (var i = 0; i < sizes.Length; i++)
            {
                var (body, flow, moves, sink) = analyses[i];
                Assert.Equal(0, sink.Problems.All.Length);
                arena[i] = ArenaWords(body, flow, moves);
            }

            output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold analysis allocation {string.Join(", ", allocated)}, " +
                $"arena words {string.Join(", ", arena)}");
            Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
            Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold analysis allocation {string.Join(", ", allocated)}");
        }
    }
}
