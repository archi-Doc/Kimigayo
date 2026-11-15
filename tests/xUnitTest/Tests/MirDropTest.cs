// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Drop elaboration on MIR (A2, SPEC 16.2, 16.3.2, 15.1.3, 14.10.3; docs/dev/DECISIONS.md R4-M6a): each verdict and each
/// destructor rule with a valid program and an invalid one, through the dump of the named body (<c>MirDump</c>: its flags, each Drop's
/// verdict groups and each location's flag points, without source suffixes) and the problems <c>Compilation.BuildMir</c> finds.</summary>
public sealed partial class MirDropTest
{
    // A default holding ledger row a1-whole-incomplete's form (a fixed array's length read after an element's Move, SPEC 4.7.3), inline in f.
    internal const string Indices = "func probe(n: i32 = label w: do\n    let a: [2 of string] = [\"a\", \"b\"]\n    _ = a[0]@move\n" +
        "    for i in a.indices => ()\n    exit to w 1\n) -> i32 => n\nfunc f() -> i32 => probe()\n";

    private const string Resource = "struct Resource\n    var name: string\n    public init(name: string) => self.name = name@move\n" +
        "func make() -> Resource => Resource.init(\"r\")\nfunc consume(r: Resource) => ()\n";

    private const string Box = Resource + "struct Box\n    public var value: Resource\n    public init(value: Resource) => self.value = value@move\n" +
        "    drop => ()\n";

    // SPEC 16.2.1: a value initialized on every path is destroyed (Keep); one moved on every path is not (Elide), and needs no flag.
    [Fact]
    public void KeepWhereInitializedElideWhereMoved()
    {
        Assert.Equal(["drop _1 -> bb2 [keep]"], Lines(Resource + "func f()\n    let r = make()\n", "f"));
        Assert.Equal(["drop _3 -> bb3 [elide]", "drop _1 -> bb4 [elide]"], Lines(Resource + "func f()\n    let r = make()\n    consume(r@move)\n", "f"));
    }

    // SPEC 12.2, 16.2.1 (DECISIONS R5-M129): an Array literal's elements, each one before the last that builds code secured where it is
    // evaluated (_3, _4), are moved by its one aggregate (Elide after it); a transfer out of a later element (`try`'s Err) destroys those
    // secured so far in reverse acquisition order (_4, then _3) and not the unbuilt Array, whose flag the aggregate sets. The Drop after
    // the return, which only the checks reach, destroys the whole Array (PLAN G641 kind 4).
    [Fact]
    public void AnInterruptedArrayLiteralDestroysItsSecuredElementsInReverse()
    {
        Assert.Equal(
            [
                "flags 1", "StorageLive(_2) [clear f0]", "drop _6 -> bb22 [open ((_ as case 1) elide, (_ as case 0) open (.0 elide))]",
                "drop _8 -> bb7 [elide]", "drop _2 -> bb13 [flag f0] [clear f0]", "StorageDead(_2) [clear f0]", "drop _4 -> bb15 [keep]",
                "drop _3 -> bb16 [keep]", "drop _6 -> bb18 [keep]", "_2 = Array(move _3, move _4, move _5): Array<Resource> [set f0]",
                "drop _5 -> bb23 [elide]", "drop _4 -> bb24 [elide]", "drop _3 -> bb25 [elide]", "drop _2 -> bb29 [keep]",
            ],
            Lines(
                Resource + "func third(ok: bool) -> Result<Resource, i32>\n    if ok => return .Ok(make())\n    return .Err(7)\n" +
                    "func f(ok: bool) -> Result<i32, i32>\n    let a: Array<Resource> = [make(), make(), try third(ok)]\n    return .Ok(0)\n",
                "f"));
    }

    // SPEC 15, 16.2.1: a value moved on some paths only is destroyed under a flag, which mirrors its initialization: set where it is
    // defined (an argument at the entry), cleared where it is moved, dropped or its storage begins or ends. A drop tree node shared by two
    // exits, one after the Move, has one verdict; reinitializing on the moving path makes it Keep again.
    [Fact]
    public void FlagWhereInitializedOnSomePaths()
    {
        Assert.Equal(
            ["flags 1", "StorageLive(_2) [clear f0]", "_2 = make() -> bb1 [set f0]", "drop _2 -> bb7 [flag f0] [clear f0]", "_6 = move _2 [clear f0]",
                "drop _6 -> bb6 [elide]", "StorageDead(_2) [clear f0]"],
            Lines(Resource + "func f(flag: bool)\n    let r = make()\n    if flag => consume(r@move)\n", "f"));
        Assert.Equal(
            ["flags 1 [set f0]", "drop _2 -> bb6 [flag f0] [clear f0]", "_6 = move _2 [clear f0]", "drop _6 -> bb5 [elide]"],
            Lines(Resource + "func f(flag: bool, r: Resource)\n    if flag => consume(r@move)\n", "f"));
        const string Exits = Resource + "func log() => ()\nfunc f(flag: bool) -> i32\n    let r = make()\n    defer => log()\n    if flag\n" +
            "        consume(r@move)\n        return 1\n    return 2\n";
        Assert.Contains("drop _2 -> bb12 [flag f0] [clear f0]", Lines(Exits, "f"));
        Assert.Equal(
            ["drop _2 -> bb10 [keep]", "drop _6 -> bb6 [elide]", "drop _2 -> bb8 [elide]", "drop _8 -> bb9 [elide]"],
            Lines(Resource + "func f(flag: bool)\n    var r = make()\n    if flag\n        consume(r@move)\n        r = make()\n", "f"));
    }

    // SPEC 16.2.2, 16.3.2: after a Partial Move, the remaining parts are destroyed (Open): each child path by its own verdict, the components
    // without one by the own bit; restoring the part makes the value whole again, and a part moved on some paths only is flagged. An owned
    // decomposition leaves the enum Subject's other payloads and Cases with the own bit.
    [Fact]
    public void OpenAfterAPartialMove()
    {
        const string Pair = "func see(p: (string, i32)) => ()\nfunc f(flag: bool)\n    var pair: (string, i32) = (\"Alice\", 30)\n";
        Assert.Contains("drop _2 -> bb2 [open (.0 elide)]", Lines(Pair + "    let name = pair.0@move\n", "f"));
        Assert.Equal(
            ["drop _2.0 -> bb1 [elide]", "drop _5 -> bb2 [elide]", "drop _3 -> bb3 [keep]", "drop _2 -> bb4 [keep]"],
            Lines(Pair + "    let name = pair.0@move\n    pair.0 = \"Bob\"\n", "f"));
        Assert.Contains("drop _2 -> bb7 [open (.0 flag f0)] [clear f0]", Lines(Pair + "    if flag => see((pair.0@move, 1))\n", "f"));
        const string Decomposed = Resource + "enum Packet\n    Data(Resource)\n    Empty\nfunc f(packet: Packet)\n    match packet@move\n" +
            "        .Data(let r) => consume(r@move)\n        _ => ()\n";
        Assert.Contains("drop _3 -> bb9 [open ((_ as case 0) open (.0 flag f0))] [clear f0]", Lines(Decomposed, "f"));
    }

    // SPEC 15.1.3, 16.3.2: a layer whose Type declares a drop must be complete wherever that drop runs: a Partial Move not restored on every
    // path to its scope exit or a replacement is MovedPlace_Kd at the Drop, a base layer's own drop included; restored on every path, or
    // without an own drop, the parts are destroyed as they are.
    [Fact]
    public void ALayerWithItsOwnDropIsCompleteAtItsDrop()
    {
        Assert.Empty(Problems(Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n    box.value = make()\n"));
        Assert.Empty(Problems(Resource + "struct Plain\n    public var value: Resource\n    public init(value: Resource) => self.value = value@move\n" +
            "func f()\n    var plain = Plain.init(make())\n    consume(plain.value@move)\n"));
        Assert.Equal(["MovedPlace_Kd var box = Box.init(make()) 1"], Problems(Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n"));
        Assert.Equal(
            ["MovedPlace_Kd var box = Box.init(make()) 1"],
            Problems(Box + "func f(flag: bool)\n    var box = Box.init(make())\n    consume(box.value@move)\n    if flag => box.value = make()\n"));
        Assert.Equal(
            ["MovedPlace_Kd box = Box.init(make()) 1"],
            Problems(Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n    box = Box.init(make())\n"));
        Assert.Equal(
            ["MovedPlace_Kd var leaf = Leaf.init(make()) 1"],
            Problems(Resource + "open struct Base\n    public var value: Resource\n    protected init(value: Resource)\n        self.value = value@move\n" +
                "    drop => ()\nstruct Leaf : Base\n    public var extra: i32 = 7\n    public init(value: Resource) : base(value@move)\n        ()\n" +
                "func f()\n    var leaf = Leaf.init(make())\n    consume(leaf.value@move)\n"));
    }

    // SPEC 16.3.1-16.3.2: a destructor's self is no owner, so its body's end destroys the layer's parts, never the layer, whose drop would
    // run again: each own Field whose Type may need drop, last to first, then the base. No part of drop's self is Moved out (SPEC 16.3.1):
    // each such Move is Binding's ExclusivePathTake_Kd, and the MIR asserts nothing on those programs.
    [Fact]
    public void ADestructorBodyDestroysItsLayersParts()
    {
        const string Pair = Resource + "struct Pair\n    var on: bool\n    var first: Resource\n    var second: Resource\n" +
            "    public init(on: bool, first: Resource, second: Resource)\n        self.on = on\n        self.first = first@move\n" +
            "        self.second = second@move\n";
        Assert.Equal(["drop _1.2 -> bb1 [keep]", "drop _1.1 -> bb2 [keep]"], Lines(Pair + "    drop\n        let on = self.on\n", "drop"));
        Assert.Equal(["self.first@move"], Takes(Pair + "    drop\n        let moved = self.first@move\n"));
        Assert.Equal(["self.first@move"], Takes(Pair + "    drop\n        if self.on\n            let moved = self.first@move\n"));
        const string Holder = Resource + "struct Holder\n    var box: Box\n    public init(box: Box) => self.box = box@move\n    drop\n" +
            "        let moved = self.box.value@move\n";
        Assert.Equal(["self.box.value@move"], Takes(Holder + Box[Resource.Length..]));
        const string Base = Resource + "open struct Base\n    public var value: Resource\n    protected init(value: Resource)\n" +
            "        self.value = value@move\nstruct Leaf : Base\n    public var extra: i32 = 7\n    public init(value: Resource) : base(value@move)\n" +
            "        ()\n    drop\n        let moved = self.value@move\n";
        Assert.Equal(["self.value@move"], Takes(Base));
        Assert.Equal(["self.value@move"], Takes(Base.Replace("value@move\nstruct", "value@move\n    drop => ()\nstruct", StringComparison.Ordinal)));
        Assert.Equal(
            ["self.value@move", "self.value@move"],
            Takes("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    drop\n        let moved = self.value@move\n" +
                "        let again = self.value@move\nlet value = Box<i32>.init(7)\n"));
    }

    // Plan 5.6.1: the Type rule is per view: a pair-generic value is destroyed in its owner case and owns nothing in a reference case, so
    // its Drop has a group per view class, a part moved out of it included (GenericCleanupCaseTest's select). A default copy keeps the Drops
    // its instance Type may need (an abstract U's, judged by their state; none of i32), the call's result before its slots (SPEC 3.6,
    // DECISIONS R4-M129). A Drop only the checks reach, after a return, has its group by the same Type rule per view (PLAN G641).
    [Fact]
    public void TheTypeRuleIsPerView()
    {
        Assert.Equal(["drop _1 -> bb1 [keep in v0; elide in v1]"], Lines("func hold<s/T>(value: s/T) -> ()\n    s is owner or ref\n    ()\n", "hold"));
        const string Select = "struct Payload\n    drop => ()\ngroup Helpers\n    public func select<s/T>(pair: (s/T, s/T)) -> s/T\n" +
            "        s is ref or obj\n        return pair.0@move\n";
        Assert.Equal(
            ["drop _1 -> bb4 [elide in v0; open (.0 elide) in v1]", "drop _2 -> bb6 [elide]", "drop _2 -> bb9 [elide]",
                "drop _1 -> bb10 [elide in v0; open (.0 elide) in v1]"],
            Lines(Select, "select"));
        const string Pick = "func pick<T>(a: T, b: T = a) -> T\n    T is Copy\n    return b\nfunc generic<U>(a: U) -> U\n    U is Copy\n    return pick(a)\n" +
            "func concrete() -> i32 => pick(1)\n";
        Assert.Equal(
            ["drop _1 -> bb6 [keep]", "drop _2 -> bb8 [elide]", "drop _4 -> bb9 [elide]", "drop _3 -> bb10 [elide]", "drop _2 -> bb13 [elide]",
                "drop _4 -> bb14 [elide]", "drop _3 -> bb15 [elide]", "drop _1 -> bb16 [keep]"],
            Lines(Pick, "generic"));
        Assert.Empty(Lines(Pick, "concrete", all: true));
    }

    // SPEC 6.3, 13.7.1: a constructor's first placement of a Field destroys nothing (Elide), and a placement after a conditional one
    // destroys the old value under a flag.
    [Fact]
    public void AConstructorFieldIsDestroyedOnlyWherePlaced()
        => Assert.Equal(
            ["flags 1", "drop _0.0 -> bb6 [flag f0] [clear f0]", "drop _0.0 -> bb4 [elide] [clear f0]", "_0.0 = move _6 [set f0]", "_0.0 = move _8 [set f0]"],
            Lines("struct S\n    var name: string\n    public init(flag: bool)\n        if flag => self.name = \"a\"\n        self.name = \"b\"\n", "init")
                .Where(static x => x.Contains("_0.0", StringComparison.Ordinal) || x.StartsWith("flags", StringComparison.Ordinal)));

    // SPEC 14.10.3, plan 5.6.3 (R4-M6a): a Drop only the checks reach adds no runtime path and is not judged; its group says what it would
    // destroy (here each value whole, Keep). A layer with its own drop left incomplete in check-only code is no problem (plan Appendix B
    // C3) but a Keep row with no child rows, its whole destruction, drop included (PLAN G641 kind 4; the runtime form is MovedPlace_Kd,
    // ALayerWithItsOwnDropIsCompleteAtItsDrop); an owning interpolation's buffer, which its end's intoString moves, has an Elide Drop
    // at the statement's end, and intoString's Result, whose string the destination takes, an Open one that destroys nothing (R8-W2
    // c3a; before: the buffer's Keep, FormatFinish having emptied it).
    [Fact]
    public void ACheckOnlyDropIsNotJudged()
    {
        Assert.Equal(
            ["drop _1 -> bb5 [keep]", "drop _3 -> bb10 [keep]", "drop _3 -> bb13 [keep]", "drop _1 -> bb14 [keep]"],
            Lines(Resource + "func f() -> i32\n    let r = make()\n    return 1\n    let s = make()\n    return 2\n", "f", all: true));
        const string Partial = Box + "func f() -> i32\n    return 1\n    var box = Box.init(make())\n    consume(box.value@move)\n";
        Assert.Empty(Problems(Partial));
        Assert.Equal(["drop _2 -> bb6 [elide]", "drop _4 -> bb8 [elide]", "drop _1 -> bb9 [keep]"], Lines(Partial, "f", all: true));
        Assert.Equal(
            ["drop _15 -> bb12 [open ((_ as case 0) open (.0 elide))]", "drop _2 -> bb13 [elide]"],
            Lines("func label(n: i32) -> string => \"n=\\(n)\"\n", "label"));
    }

    // SPEC 14.10.3, 16.3.2, 8.15.3 (PLAN G641 kind 4): a Drop only the checks reach has the group of what it would destroy on its assumed
    // paths, by the runtime rules but flagless (R4-M6a): after the return, a partly moved Tuple is Open with its moved elements Elide, and
    // an element moved on some assumed paths only is Keep, not Flag, so the body has no flag; the return's runtime Drop keeps the whole.
    [Fact]
    public void ACheckOnlyDropsGroupIsWhatItWouldDestroy()
    {
        const string Pair = "func see(s: string) => ()\nfunc f(flag: bool, t: (string, string)) -> i32\n    return 1\n";
        Assert.Equal(
            ["drop _2 -> bb4 [keep]", "drop _5 -> bb8 [elide]", "drop _7 -> bb10 [elide]", "drop _2 -> bb13 [open (.0 elide, .1 elide)]"],
            Lines(Pair + "    see(t.0@move)\n    see(t.1@move)\n    return 2\n", "f"));
        Assert.Equal(
            ["drop _2 -> bb4 [keep]", "drop _7 -> bb11 [elide]", "drop _9 -> bb13 [elide]", "drop _2 -> bb16 [open (.0 keep, .1 elide)]"],
            Lines(Pair + "    if flag => see(t.0@move)\n    see(t.1@move)\n    return 2\n", "f"));
    }

    // DECISIONS R4-M102: the verdicts are exact only for a body A1 accepts (R4-M6a), so the table records each body's first A1 or A2 problem,
    // A1's (a Move's later use, a default template's) and A2's (an incomplete layer with its own drop) alike; a body without one, or with
    // only A5's (a Loan conflict, SPEC 15.6), is exact. The source's problems are given by code.
    [Theory]
    [InlineData(Indices, "probe", new[] { "DefaultTemplate MovedPlace_Kd a.indices #own", "Fn" }, "MovedPlace_Kd")]
    [InlineData(
        Resource + "func f()\n    let r = make()\n    consume(r@move)\n    consume(r@move)\n",
        "f",
        new[] { "Fn MovedPlace_Kd r #own" },
        "MovedPlace_Kd")]
    [InlineData(
        Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n",
        "f",
        new[] { "Fn MovedPlace_Kd var box = Box.init(make()) #own" },
        "MovedPlace_Kd")]
    [InlineData(Resource + "func f()\n    let r = make()\n", "f", new[] { "Fn" }, "")]
    [InlineData("func f() -> i32\n    var x = 1\n    let r = x@ref\n    x = 2\n    _ = r\n    return x\n", "f", new[] { "Fn" }, "ComparisonLoanConflict_Kd")]
    public void ABodysFirstA1OrA2ProblemMarksItsTable(string source, string name, string[] expected, string codes)
    {
        Assert.Equal(expected, Incomplete(source, name));
        Assert.Equal(codes, string.Join(' ', Problems(source).Select(static x => x.Split(' ')[0])));
    }

    // DECISIONS R4-M102 (SPEC 7.2.3, R4-M69): MirReport drops a problem found in an inline default copy as its template's, but the copy's
    // body is marked too, with the problem's own subject, the template's, so the caller's refusal shares its template's code and subject;
    // the template's mark names its problem by its index, the copy's by -1, as the report dropped it (R4-M132).
    [Fact]
    public void ACopysProblemMarksItsCallerWithItsTemplatesSubject()
    {
        var c = MirBuildTest.Compile(Indices);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        var all = program.Problems.All.ToArray();
        var problem = Assert.Single(all, static x => x.Subject.Module == 0);
        var bodies = program.Bodies.ToArray();
        var f = bodies.Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" });
        Assert.Equal((MirBodyKind.DefaultTemplate, "a.indices"), (bodies[problem.Body].Key.Kind, c.SyntaxOf(problem.Subject).ToString()));
        Assert.Equal((problem.Code, problem.Subject, Array.IndexOf(all, problem)), Mark(program, problem.Body));
        Assert.Equal((problem.Code, problem.Subject, -1), Mark(program, f.Ordinal));
    }

    // DECISIONS R4-M102, R4-M15a: a body whose A1 arena exceeds the limit judges no view and is marked OwnershipStorageLimit_Kd at its source
    // (N locals live across N branches, MirInitTest's form); A5's own limit (N reference locals, MirAnalysisTest's) leaves it exact.
    [Theory]
    [InlineData(13_500, "    var v{0} = {0}\n|    if flag => v{0} = 1\n", 0, "Fn OwnershipStorageLimit_Kd func f(flag: bool) -> i32 #own")]
    [InlineData(2_000, "    var r{0} = x@ref\n|    if flag => r{0} = y@ref\n|    inspect(r{0})\n", 1, "Fn")]
    public void OnlyA1sArenaLimitMarksItsBody(int size, string line, long views, string expected)
    {
        var text = new StringBuilder("func inspect(value: ref/i32) => ()\nfunc f(flag: bool) -> i32\n    var x = 1\n    var y = 2\n");
        foreach (var part in line.Split('|'))
        {
            for (var n = 0; n < size; n++)
            {
                text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, part, n);
            }
        }

        var c = MirBuildTest.Compile(text.Append("    return x\n").ToString());
        var program = c.BuildMir();
        var f = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" });
        Assert.Equal([Kimi.DiagnosticCode.OwnershipStorageLimit_Kd], program.Problems.All.ToArray().Where(x => x.Body == f.Ordinal).Select(static x => x.Code));
        Assert.Equal(views, program.Drops.ViewsOf(f.Ordinal));
        Assert.Equal(expected, Incomplete(c, program, "f")[0]);
    }

    // SPEC 16.2.2 (DECISIONS R5-M50): a transfer leaving a cleanup delivers its result through a temp (written at its jumps, moved by the
    // target's delivery at the temp's own source) in its target's scope, not the jump's: an `exit` out of a defer body past a local with a
    // cleanup (SPEC 16.1), through one in the scope that registered the defer, and a `yield` out of an `if` arm, through one in the scope
    // the `if` opened in, both `t`'s; a `return` past `t`, through one in the function's own scope.
    [Theory]
    [InlineData("func f(c: bool)\n    let t = \"t\"\n    defer\n        let s = \"s\"\n        if c\n            exit\n        _ = s@ref\n    _ = t@ref\n", false)]
    [InlineData("func f(c: bool)\n    let t = \"t\"\n    let v = if c\n        let s = \"s\"\n        yield 1\n    else\n        yield 2\n    _ = t@ref\n    _ = v\n", false)]
    [InlineData("func f(c: bool) -> i32\n    let t = \"t\"\n    _ = t@ref\n    return 1\n", true)]
    public void ADeliveredResultLiesInItsTargetsScope(string source, bool returns)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f" && x.Key.Kind == MirBodyKind.Fn);
        var jumps = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<JumpKoto>().Select(static x => SourceRef.Of(x)).ToHashSet();
        var (locals, operands) = (body.Locals.ToArray(), body.Operands.ToArray());
        var assigns = body.Statements.ToArray().Where(static x => x.Kind == MirStatementKind.Assign).ToArray();
        var written = assigns.Where(x => jumps.Contains(x.Source.Syntax)).Select(static x => x.Place.Root).ToHashSet();
        var deliveries = assigns.Where(x => x.Rvalue.Kind == MirRvalueKind.Use && operands[x.Rvalue.Start] is { Kind: MirOperandKind.Move } moved &&
            written.Contains(moved.Place.Root)).ToArray();
        var scope = returns ? 0 : locals.Single(static x => x.Name == "t").Source.Scope;
        Assert.NotEmpty(deliveries);
        Assert.All(deliveries, x =>
        {
            var temp = locals[operands[x.Rvalue.Start].Place.Root].Source;
            Assert.Equal((scope, temp), (temp.Scope, x.Source)); // The temp lies in the target's scope; its delivery is at the temp's source.
        });
    }

    // PLAN G702, SPEC 16.3.2, 15.1.3: A2's walk of a group (MirDropTable.Destroyed) states what an Open Drop destroys, in destruction
    // order: a struct's Fields from the last, each by its child row (an Open Field its own level, a Flag Field whole, an Elide one no
    // step), a part without a row whole under the level's own bit, then the base; a fixed array's elements from the last, no Run above an
    // element row at the highest position or below one at the lowest, one Run per gap between element rows, a Flag element whole between
    // its Runs; an enum's Case payloads by ordinal, an Open payload its own level beside a Case without a row, whole. Each walk is
    // followed by the level of the dropped Type no row governs (MirDropTable.Level, a glue function's): the same enumeration, so every
    // level of a walk names its parts in that level's order (Walks checks each).
    [Fact]
    public void AGroupsWalkStatesWhatItDestroysInDestructionOrder()
    {
        const string Source = "struct Pair\n    public var a: string\n    public var b: (string, Array<i32>)\n    public var n: i32\n" +
            "    public init(a: string, b: (string, Array<i32>), n: i32)\n        self.a = a@move\n        self.b = b@move\n        self.n = n\n" +
            "open struct Base\n    public var p: string\n    public var q: Array<i32>\n    protected init(p: string, q: Array<i32>)\n" +
            "        self.p = p@move\n        self.q = q@move\nstruct Leaf : Base\n    public var x: string\n" +
            "    public init(x: string, p: string, q: Array<i32>) : base(p@move, q@move)\n        self.x = x@move\n" +
            "enum Two\n    A(string, Array<i32>)\n    B(string, string)\n" +
            "func take(s: string) => ()\nfunc partly(c: bool, p: Pair)\n    if c => take(p.a@move)\n    take(p.b.0@move)\n" +
            "func elements(c: bool, a: [5 of string])\n    take(a[4]@move)\n    take(a[0]@move)\n    if c => take(a[2]@move)\n" +
            "func based(l: Leaf)\n    take(l.p@move)\n" +
            "func cases(t: Two)\n    match t@move\n        .A(let r, _) => take(r@move)\n        _ => ()\n";
        var program = Built(Source);
        Assert.Equal(["_( .2:i32 .1( .1:Array<i32> ) .0:string:Flag )", "_( .2:i32 .1:(string, Array<i32>) .0:string )"], Walks(program, "partly"));
        Assert.Equal(["_( [3..3]:string [2]:string:Flag [1..1]:string )", "_( [0..4]:string )"], Walks(program, "elements"));
        Assert.Equal(["_( .0:string base( .1:Array<i32> ) )", "_( .0:string base:Base )"], Walks(program, "based"));
        Assert.Equal(
            ["_( case 0( .1:Array<i32> .0:string:Flag ) case 1:(string, string) )", "_( case 0:(string, Array<i32>) case 1:(string, string) )"],
            Walks(program, "cases"));
    }

    // PLAN G702, V16: the walk passes over the rows below the walked row a fixed number of times (their count, their index), never once
    // per part: over a struct of N Fields with every second one moved, N 32, 64 and 128 (the wider two past the walk's inline index),
    // the rows those passes read grow with N. The walker's searches of the sorted index (one per part) are not rows read.
    [Fact]
    public void AnOpenLevelsChildRowsAreReadInOnePass() => RowsReadGrowLinearly([32, 64, 128], static size =>
    {
        var text = new StringBuilder("struct Wide\n");
        for (var n = 0; n < size; n++)
        {
            text.Append("    public var x").Append(n).Append(": string = \"s\"\n");
        }

        text.Append("    public init() => ()\nfunc take(s: string) => ()\nfunc work(w: Wide)\n");
        for (var n = 0; n < size; n += 2)
        {
            text.Append("    take(w.x").Append(n).Append("@move)\n");
        }

        return text.ToString();
    });

    // PLAN G702, V16: no level passes over the rows below it again, so the walk's passes read each row of its group the same number
    // of times at any depth: over a chain of D nested structs with the innermost Field moved (D Open levels, one inside the other), D
    // 8, 16 and 32, the rows those passes read grow with D. The walker's searches of the sorted index are not rows read.
    [Fact]
    public void NestedOpenLevelsReadOnlyTheirDirectChildRows() => RowsReadGrowLinearly([8, 16, 32], static depth =>
    {
        var text = new StringBuilder("struct S0\n    public var q: string\n    public var k: string\n" +
            "    public init(q: string, k: string)\n        self.q = q@move\n        self.k = k@move\n");
        for (var n = 1; n < depth; n++)
        {
            text.Append("struct S").Append(n).Append("\n    public var p: S").Append(n - 1).Append("\n    public var k: string\n")
                .Append("    public init(p: S").Append(n - 1).Append(", k: string)\n        self.p = p@move\n        self.k = k@move\n");
        }

        text.Append("func take(s: string) => ()\nfunc work(w: S").Append(depth - 1).Append(")\n    take(w");
        return text.Insert(text.Length, ".p", depth - 1).Append(".q@move)\n").ToString();
    });

    // DECISIONS R4-M102, V16: a warm rebuild of a program whose bodies are marked allocates nothing (MirBuildTest's measurement): the mark is
    // stored with each closed body's ends, and the table keeps its storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebuildMarksWithoutAllocating()
    {
        var c = MirBuildTest.Compile(Indices);
        var marked = Incomplete(c, c.BuildMir(), "f");
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
        Assert.Equal(["Fn MovedPlace_Kd a.indices #-1"], marked);
        Assert.Equal(marked, Incomplete(c, c.BuildMir(), "f"));
    }

    private static MirProgram Built(string source)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return program;
    }

    // V16, in KeysKeepTheEvaluationsLinear's form: the rows the walk of the one Open Drop of work reads (MirDropTable.WalkReads), per
    // size of the source, grow no faster than the size: the growth from the second size to the third is at most twice the growth
    // from the first to the second.
    private static void RowsReadGrowLinearly(int[] sizes, Func<int, string> source)
    {
        var reads = new long[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            var program = Built(source(sizes[i]));
            var before = program.Drops.WalkReads;
            Assert.Equal(2, Walks(program, "work").Length);
            reads[i] = program.Drops.WalkReads - before;
        }

        Assert.NotEqual(0, reads[0]);
        Assert.True(reads[2] - reads[1] <= 2 * (reads[1] - reads[0]), $"rows read {string.Join(", ", reads)}");
    }

    // Per Open group of view 0 in the body named name, in block order: its walk (MirDropTable.Destroyed), then the level of the dropped
    // Type no row governs (MirDropTable.Level). A step is its move-path step (_ at the top) with "(" for an Open step, ")" for an End,
    // else its Type and, under a row, the row's verdict; a Run is its positions and its element Type. Every level of a walk names its
    // parts in the order of its Type's level without rows, less the parts moved out.
    private static string[] Walks(MirProgram program, string name)
    {
        var body = program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == name && x.Key.Kind == MirBodyKind.Fn);
        var (lines, steps, level) = (new List<string>(), new List<MirDropStep>(), new List<MirDropStep>());
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            var terminator = body.Blocks[block].Terminator;
            var group = terminator.Kind == MirTerminatorKind.Drop ? program.Drops.Group(body.Ordinal, block, 0) : -1;
            if (group < 0 || program.Drops.RowsOf(body.Ordinal)[group].Verdict != MirDropVerdict.Open)
            {
                continue;
            }

            steps.Clear();
            program.Drops.Destroyed(body.Ordinal, group, body.TypeOf(terminator.Place)!, steps);
            for (var open = 0; open < steps.Count; open++)
            {
                if (steps[open].Kind == MirDropStepKind.Open)
                {
                    var array = steps[open].Type is { Kind: BoundTypeKind.FixedArray } fixedArray ? fixedArray : null;
                    Assert.True(array is null || MirTypes.LevelPart(array, 0) == (MirProjectionKind.ConstIndex, array.Length - 1));
                    level.Clear();
                    MirDropTable.Level(steps[open].Type, level);
                    var (all, at) = (Places(level, 0), 0);
                    foreach (var place in Places(steps, open))
                    {
                        at = all.IndexOf(place, at) + 1;
                        Assert.True(at > 0, $"{name}: {place} is out of the level's order");
                    }
                }
            }

            level.Clear();
            MirDropTable.Level(steps[0].Type, level);
            lines.Add(Text(program, body, steps));
            lines.Add(Text(program, body, level));
        }

        return [.. lines];
    }

    private static string Text(MirProgram program, MirBody body, List<MirDropStep> steps) => string.Join(' ', steps.Select(x =>
    {
        var label = x.Arg < 0 ? "_" : x.Step switch
        {
            MirProjectionKind.Field => $".{x.Arg}",
            MirProjectionKind.Base => "base",
            MirProjectionKind.Downcast => $"case {x.Arg}",
            _ => $"[{x.Arg}]",
        };
        return x.Kind switch
        {
            MirDropStepKind.Open => label + "(",
            MirDropStepKind.End => ")",
            MirDropStepKind.Run => $"[{x.Arg}..{x.Last}]:{TyCtxt.Name(x.Type)}",
            _ => $"{label}:{TyCtxt.Name(x.Type)}{(x.Row < 0 ? string.Empty : ":" + program.Drops.RowsOf(body.Ordinal)[x.Row].Verdict)}",
        };
    }));

    // The direct parts of the level whose Open step is at an index, in order: each part's move-path step, a Run's every position.
    private static List<(MirProjectionKind Step, long Position)> Places(List<MirDropStep> steps, int open)
    {
        List<(MirProjectionKind Step, long Position)> places = [];
        for (var (i, depth) = (open + 1, 0); depth >= 0; i++)
        {
            var step = steps[i];
            var direct = depth == 0 && step.Kind != MirDropStepKind.End;
            for (var position = step.Kind == MirDropStepKind.Run ? step.Last : step.Arg; direct && position >= step.Arg; position--)
            {
                places.Add((step.Step, position));
            }

            depth += step.Kind == MirDropStepKind.Open ? 1 : step.Kind == MirDropStepKind.End ? -1 : 0;
        }

        return places;
    }

    // The dump lines of a source's body named name that carry A2's output (its flags, each Drop with a verdict and each location with flag
    // points; with all, every Drop), without their statement marks and source suffixes; the source has no Binding issue.
    private static string[] Lines(string source, string name, bool all = false)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = Array.Find(program.Bodies.ToArray(), x => x.Key.Decl?.Name == name && x.Key.Kind == MirBodyKind.Fn && x.Source.Module == 0)!;
        var text = new StringBuilder();
        MirDump.Write(body, text, program.Drops);
        return [.. text.ToString().Split('\n').Select(static x => Suffix().Replace(x.TrimStart(' ', '*'), string.Empty))
            .Where(x => x.StartsWith("flags", StringComparison.Ordinal) || Annotation().IsMatch(x) || (all && x.StartsWith("drop", StringComparison.Ordinal)))];
    }

    private static string[] Problems(string source)
    {
        Assert.Empty(MirBuildTest.Compile(source).Binding.Issues);
        return MirInitTest.Problems(source);
    }

    // The first A1 or A2 problem of each body named name (DECISIONS R4-M102), by kind: "Kind Code subject #index", the index "own" where it
    // names that problem in the report (R4-M132), or "Kind" for an exact body; the source has no Binding issue.
    private static string[] Incomplete(string source, string name)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return Incomplete(c, program, name);
    }

    private static string[] Incomplete(Compilation c, MirProgram program, string name)
        => [.. program.Bodies.ToArray().Where(x => x.Key.Decl?.Name == name).Select(x => Mark(program, x.Ordinal) is (var code, var subject, var problem)
            ? $"{x.Key.Kind} {code} {c.SyntaxOf(subject).ToString().Split('\n')[0].Trim()} #{Index(program, (code, subject, x.Ordinal), problem)}"
            : $"{x.Key.Kind}")];

    // A body's mark (MirDropTable.Incomplete): its code, its subject located in the body and its problem's index; null for an exact body.
    private static (Kimi.DiagnosticCode Code, SourceRef Subject, int Problem)? Mark(MirProgram program, int body)
        => program.Drops.Incomplete(body) is { } mark ? (mark.Code, program.Body(body).Subject(mark.Source), mark.Problem) : null;

    // A mark's index: "own" where it names the report's problem of the mark's code, subject and body, else the index.
    private static string Index(MirProgram program, (Kimi.DiagnosticCode Code, SourceRef Subject, int Body) mark, int problem)
        => problem >= 0 && program.Problems.All[problem] is var found && (found.Code, found.Subject, found.Body) == mark ? "own"
            : problem.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The nodes of a source's Binding issues, each ExclusivePathTake_Kd.
    private static string[] Takes(string source)
    {
        var issues = MirBuildTest.Compile(source).Binding.Issues;
        Assert.All(issues, static x => Assert.Equal(Kimi.DiagnosticCode.ExclusivePathTake_Kd, x.Code));
        return [.. issues.Select(static x => x.Node.ToString()!)];
    }

    [GeneratedRegex(@" @[0-9:]+( s[0-9]+)?$")]
    private static partial Regex Suffix();

    [GeneratedRegex(@" \[(set|clear|keep|elide|flag|open)")]
    private static partial Regex Annotation();

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests(ITestOutputHelper output)
    {
        // V16 (plan "New V-checks"): A2 is linear in the body: N 16/32/64 locals each initialized on one branch need N flags, and the cold analysis
        // of the body (fresh components, as MirInitTest's) allocates linearly. MirBuildTest.WarmRebuildAllocatesNothing covers the warm
        // re-analysis.
        [Fact]
        public void DropFlagsScaleLinearly()
        {
            var (sizes, flags) = (new[] { 16, 32, 64 }, new long[3]);
            var analyses = new (MirBody Body, MirProgram Sink)[3];
            var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
            {
                var text = new StringBuilder("func text(n: i32) -> string => \"\\(n)\"\nfunc work(flag: bool)\n");
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.Append("    var v").Append(n).Append(": string\n    if flag => v").Append(n).Append(" = text(").Append(n).Append(")\n");
                }

                var c = MirBuildTest.Compile(text.ToString());
                var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "work");
                var (flow, moves, cases, sink) = (new MirDataflow(new MirReach()), new MirMoveData(), new MirCases(c.Types), new MirProgram());
                var drops = new MirDrops(moves, cases);
                var init = new MirInit(flow, moves, cases, drops);
                analyses[i] = (body, sink);
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
                var (_, sink) = analyses[i];
                Assert.Equal(0, sink.Problems.All.Length);
                flags[i] = sink.Drops.FlagsOf(0);
            }

            output.WriteLine($"N {string.Join(", ", sizes)}: flags {string.Join(", ", flags)}, cold analysis allocation {string.Join(", ", allocated)}");
            Assert.Equal([16L, 32L, 64L], flags);
            Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold analysis allocation {string.Join(", ", allocated)}");
        }
    }
}
