// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The drops of MIR code generation (R5 C0c, C4a, C4b; package C 3.6 and 3.8; DECISIONS R5-M78, R5-M95, R5-M96, R5-M99): the Drop rows,
// the drop flags, the glue functions, and gate step 2, which writes no body A1 or A2 left inexact.
public sealed partial class FunctionCodegenTest
{
    // Two literals' temporaries, one on a conditional path, each borrowed and destroyed (Keep), and a string never initialized (Elide).
    private const string Drops =
        "Console.writeLine(\"a\")\nlet s: string\nlet flag = true\nif flag\n    Console.writeLine(\"b\")\n";

    // A Library's parameters, each destroyed by its Type's glue (R5 C4a; DECISIONS R5-M95): a derived struct with a string, an i32 and a
    // zero-sized Guard Field and a base, a Tuple, an enum with an empty Case and a fixed array; Guard and Holder declare drops.
    private const string Glues =
        "struct Guard\n    drop => ()\nstruct Holder\n    var s: string\n    public init(s: string) => self.s = s@move\n    drop => ()\n" +
        "open struct Base\n    public var b: string\n    protected init(b: string) => self.b = b@move\nstruct Leaf : Base\n    public var x: string\n" +
        "    public var n: i32\n    public var y: Guard\n    public init(x: string, b: string) : base(b@move)\n        self.x = x@move\n" +
        "        self.n = 1\n        self.y = Guard.init()\nenum Shape\n    Pair(string, i32, Guard)\n    Empty\n    One(Holder)\n" +
        "func leaf(v: Leaf) => ()\nfunc tuple(v: (Holder, i32, Guard)) => ()\nfunc shape(v: Shape) => ()\nfunc row(v: [3 of string]) => ()\n";

    // A Library's values destroyed under A2's flags (R5 C4b; DECISIONS R5-M99): an argument and a local string, each moved on one path; a
    // zero-sized Guard declaring a drop, made by a call; a generic value at i32, which needs no drop, and at string; a pair-generic value
    // replaced through its layer, whose points differ by view, in its owner case (Flag) and its uniq case (Elide).
    private const string Flags =
        "struct Guard\n    drop => ()\nfunc take(s: string) => ()\nfunc keep(g: Guard) => ()\nfunc sink<T>(x: T) => ()\n" +
        "func argument(c: bool, s: string)\n    if c => take(s@move)\nfunc local(c: bool)\n    let s = \"x\"\n    if c => take(s@move)\n" +
        "func guard(c: bool)\n    let g = Guard.init()\n    if c => keep(g@move)\nfunc give<T>(c: bool, x: T)\n    if c => sink(x@move)\n" +
        "func view<s/T>(c: bool, value: s/T, x: T)\n    s is owner or uniq\n    var v = value@move\n    v@follow = x@move\n" +
        "    if c => sink(v@move)\nfunc use()\n    give(true, 1)\n    give(true, \"a\")\n    var k = \"k\"\n    view(true, \"b\", \"c\")\n" +
        "    view(true, k@uniq, \"d\")\n";

    // A Library's values destroyed after a Partial Move (R5 C4c; DECISIONS R5-M100), each part a string, so each glue its Drops call is the
    // string's: a struct's Fields moved on one path, borrowed and unnamed, or moved while the whole is moved on the other path; a fixed
    // array's elements at constant indices; a derived struct's nested Field and base Field; an owned match Subject's two Cases; a generic
    // pair at i32, which needs no drop, and at string; a pair-generic pair in its owner and its ref case.
    private const string Opens =
        "struct Triple\n    public var a: string\n    public var b: string\n    public var c: string\n" +
        "    public init(a: string, b: string, c: string)\n        self.a = a@move\n        self.b = b@move\n        self.c = c@move\n" +
        "open struct Base\n    public var p: string\n    public var q: string\n    protected init(p: string, q: string)\n" +
        "        self.p = p@move\n        self.q = q@move\nstruct Leaf : Base\n    public var x: string\n    public var y: Triple\n" +
        "    public init(x: string, y: Triple, p: string, q: string) : base(p@move, q@move)\n        self.x = x@move\n        self.y = y@move\n" +
        "enum Two\n    A(string, string)\n    B(string)\nfunc take(s: string) => ()\nfunc look(s: ref/string) => ()\n" +
        "func whole(t: Triple) => ()\nfunc sink<T>(x: T) => ()\nfunc field(c: bool, t: Triple)\n    if c => take(t.b@move)\n    look(t.c@ref)\n" +
        "func own(c: bool, t: Triple)\n    if c\n        whole(t@move)\n    else\n        take(t.b@move)\n" +
        "func element(c: bool, a: [5 of string])\n    if c => take(a[1]@move)\n    take(a[3]@move)\n" +
        "func layers(c: bool, l: Leaf)\n    if c => take(l.y.b@move)\n    if c => take(l.p@move)\n" +
        "func cases(c: bool, t: Two)\n    match t@move\n        .A(let r, _) => if c => take(r@move)\n        .B(let s) => take(s@move)\n" +
        "func give<T>(c: bool, p: (T, T))\n    if c => sink(p.0@move)\nfunc view<s/T>(pair: (s/T, s/T)) -> s/T\n    s is owner or ref\n" +
        "    return pair.0@move\nfunc use(a: string, b: string, x: string, y: string, k: string)\n    give(true, (1, 2))\n" +
        "    give(true, (a@move, b@move))\n    let one = view((x@move, y@move))\n    let two = view((k@ref, k@ref))\n";

    // A whole fixed array's indices after one element moved out, which A1 rejects (MovedPlace_Kd at a.indices) and the old analysis
    // accepts: MIR difference row a1-whole-incomplete.
    private const string WholeIncomplete =
        "func f()\n    let a: [2 of string] = [\"a\", \"b\"]\n    _ = a[0]@move\n    for i in a.indices\n        Console.writeLine(\"i\")\nf()\n";

    // The same form in a default, expanded inline into its caller f, where MirReport drops the copy's problem as its template's (R4-M69).
    private const string CopiedIncomplete =
        "func g(n: i32 = (label v: do\n    let a: [2 of string] = [\"a\", \"b\"]\n    _ = a[0]@move\n    var c: i32 = 0\n" +
        "    for i in a.indices\n        c += 1\n    exit to v c\n)) -> i32 => n\nfunc f() -> i32\n    let k = \"kept\"\n    return g()\nlet r = f()\n";

    // DECISIONS R5-M78, R4-M6a: a Ref stores its local's address; a Keep Drop calls the dropped Type's glue once, on the Place's address, and
    // an Elide Drop nothing, so each string a path initializes is destroyed exactly once and the one never initialized is not; the string's
    // glue item calls the runtime release with its own parameters (PD18).
    [Fact]
    public void EachInitializedStringIsDestroyedOnce()
    {
        var ir = GeneratedIr.Parse(Generate(Drops));
        var body = ir.Startup;
        var strings = body.Instructions.Where(static x => x.Shape == "alloca %kimi.string, align 8").ToArray();
        var literals = body.Instructions.Where(static x => x.Opcode == "store" && x.Shape.StartsWith("store %kimi.string", StringComparison.Ordinal))
            .Select(static x => Assert.Single(x.Operands)).ToArray();
        var borrows = body.Instructions.Where(static x => x.Shape == "store ptr %, ptr %, align 8").Select(static x => x.Operands[0]).ToArray();
        var writes = body.Instructions.Where(x => x.Opcode == "call" && x.Callee == ir.Function("__kimi_write_line")).ToArray();
        var drops = body.Instructions.Where(x => x.Opcode == "call" && x.Callee != ir.Function("__kimi_write_line")).ToArray();
        Assert.Equal(3, strings.Length);
        Assert.Equal(2, literals.Distinct().Count());
        Assert.Equal(literals, borrows);
        Assert.Equal(2, writes.Length);
        Assert.Equal(literals, drops.Select(static x => x.Operands[0]));
        Assert.All(drops, static x => Assert.Equal("call void @(ptr %, ptr @, i64 15)", x.Shape));
        var glue = Assert.Single(drops.Select(static x => x.Callee).Distinct())!;
        Assert.Equal(["call void @(ptr %, ptr %, i64 %)", "ret void"], glue.Instructions.Select(static x => x.Shape));
        Assert.Same(ir.Function("__kimi_destroy_string"), glue.Instructions[0].Callee);
        Assert.Equal(glue.Parameters, glue.Instructions[0].Operands);
    }

    // SPEC 16.3.2, PD18, R4-M6b (R5 C4a; DECISIONS R5-M95): a glue destroys one level of its Type in destruction order, each part that may
    // need a drop through that part's glue at its address: a derived struct's Fields last to first, the i32 none, then its base at the
    // value's own address (X1); a Tuple's elements by decreasing index; an enum's active Case under one switch on its i32 tag at the value's
    // address, its payload last to first, the empty Case nothing; a fixed array's elements from the last in one loop; a struct that declares
    // a drop its destructor alone, on the value's address or, zero-sized, with no argument; a string the runtime release.
    [Fact]
    public void ADropGlueDestroysOneLevelInDestructionOrder()
    {
        var c = Analyze(Glues, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var (leaf, tuple, shape, row) = (GlueOf(ir, c, "leaf"), GlueOf(ir, c, "tuple"), GlueOf(ir, c, "shape"), GlueOf(ir, c, "row"));
        var (guard, text, parent) = (CalleesOf(leaf)[0], CalleesOf(leaf)[1], CalleesOf(leaf)[2]);
        var holder = CalleesOf(tuple)[1];
        Assert.Equal([guard, text, parent], CalleesOf(leaf));
        Assert.Same(leaf.Parameters[0], CallsOf(leaf)[2].Operands[0]);
        Assert.Equal([text], CalleesOf(parent));
        Assert.Same(parent.Parameters[0], CallsOf(parent)[0].Operands[0]);
        Assert.Equal([ir.Function("__kimi_destroy_string")], CalleesOf(text));
        Assert.Equal(text.Parameters, CallsOf(text)[0].Operands);
        Assert.Equal("internal void ()", Assert.Single(CalleesOf(guard)).Signature);
        Assert.Equal("internal void (ptr)", Assert.Single(CalleesOf(holder)).Signature);
        Assert.Same(holder.Parameters[0], Assert.Single(CallsOf(holder)).Operands[0]);
        Assert.Equal([guard, holder], CalleesOf(tuple));
        var entry = shape.Blocks[0];
        Assert.Same(shape.Parameters[0], Assert.Single(entry.Instructions, static x => x.Opcode == "load").Operands[0]);
        Assert.Equal("switch i32 %, label % [ i32 0, label % i32 2, label % ]", entry.Terminator.Shape);
        Assert.Equal([guard, text], CalleesOf(entry.Successors[1].Instructions));
        Assert.Equal([holder], CalleesOf(entry.Successors[2].Instructions));
        Assert.Equal([text], CalleesOf(row));
        Assert.Contains(row.Instructions, static x => x.Shape == "sub i64 48, %");
        Assert.Contains(row.Instructions, static x => x.Shape == "icmp eq i64 %, 3");
    }

    // R4-M6a, DECISIONS R5-M99: each of A2's flags is a bool slot of the entry block, cleared there, and the entry's flag points follow: an
    // argument moved on one path has its flag set before the first block; a body without flags has no flag slot.
    [Fact]
    public void AFlagIsABoolSlotClearedAtEntryWhereAnArgumentIsSet()
    {
        var (c, ir) = Flagged();
        var argument = Assert.Single(FunctionsOf(ir, c, "argument"));
        var flag = Assert.Single(FlagsOf(argument));
        var entry = argument.Blocks[0];
        Assert.Equal(["store i8 0, ptr %, align 1", "store i8 1, ptr %, align 1"], StoresOf(entry.Instructions, flag));
        Assert.Equal("br label %", entry.Terminator.Shape);
        Assert.Empty(FlagsOf(Assert.Single(FunctionsOf(ir, c, "take"))));
    }

    // SPEC 16.2.1, R4-M6a, DECISIONS R5-M99: a flag point is written where its location completes, in the MIR dump's order (StorageLive
    // clears, the definition sets, the Drop's own point clears after its test, the Move clears, StorageDead clears): a statement's after
    // it, a call's after the call and before its branch; the Drop loads its flag and branches on it as a bool to a block that calls the
    // Place's glue once on its address and joins the other path, whose join writes the Drop's clear point, then the Drop's branch.
    [Fact]
    public void AFlagFollowsItsPointsAndGuardsItsDrop()
    {
        var (c, ir) = Flagged();
        var local = Assert.Single(FunctionsOf(ir, c, "local"));
        var flag = Assert.Single(FlagsOf(local));
        Assert.Equal(
            ["store i8 0, ptr %, align 1", "store i8 0, ptr %, align 1", "store i8 1, ptr %, align 1", "store i8 0, ptr %, align 1",
                "store i8 0, ptr %, align 1", "store i8 0, ptr %, align 1"],
            StoresOf(local.Instructions, flag));
        var text = Assert.Single(local.Instructions, static x => x.Shape.StartsWith("store %kimi.string {", StringComparison.Ordinal));
        Assert.Equal("store i8 1, ptr %, align 1", local.Instructions[local.Instructions.IndexOf(text) + 1].Shape);
        var test = Assert.Single(local.Blocks, x => x.Terminator.Shape == "br i1 %, label %, label %");
        var set = Assert.Single(test.Terminator.Operands);
        Assert.Equal("trunc i8 % to i1", set.Shape);
        Assert.Equal(("load i8, ptr %, align 1", flag), (set.Operands[0].Shape, Assert.Single(set.Operands[0].Operands)));
        var (drop, joined) = (test.Successors[0], test.Successors[1]);
        var destroy = Assert.Single(drop.Instructions, static x => x.Opcode == "call");
        Assert.Same(Assert.Single(text.Operands), destroy.Operands[0]);
        Assert.Same(ir.Function("__kimi_destroy_string"), Assert.Single(CalleesOf(destroy.Callee!)));
        Assert.Equal([joined], drop.Successors);
        Assert.Equal(["store i8 0, ptr %, align 1", "br label %"], joined.Instructions.Select(static x => x.Shape));
        Assert.Same(flag, Assert.Single(joined.Instructions[0].Operands));

        var guard = Assert.Single(FunctionsOf(ir, c, "guard"));
        var init = Assert.Single(guard.Blocks, static x => x.Instructions.Any(static x => x.Shape == "store i8 1, ptr %, align 1")).Instructions;
        Assert.Equal(["call void @()", "store i8 1, ptr %, align 1", "br label %"], init.Skip(init.Count - 3).Select(static x => x.Shape));
    }

    // DECISIONS R5-M99, R5-M95: a Flag destroys exactly when its instance Type may need a drop: give<i32>'s keeps its flag's slot and points
    // but writes no test or destruction, give<string>'s tests its flag; a zero-sized Guard's glue is called on the zero-sized symbol and
    // calls its destructor with no argument. A pair-generic body writes only its view's points: replacing the value through its layer
    // destroys the old one, clearing the flag, then sets it, in the owner case only (A2's [clear f0 in v0] and [set f0 in v0], which block
    // 0's points interleave by view); every view's shared points are written once; and only the owner case's Drop tests its flag (the uniq
    // case's verdict is Elide).
    [Fact]
    public void AFlagDestroysWhereItsInstanceMayNeedADrop()
    {
        var (c, ir) = Flagged();
        var give = FunctionsOf(ir, c, "give");
        var number = Assert.Single(give, static x => x.Signature == "internal void (i1, i32)");
        var flag = Assert.Single(FlagsOf(number));
        Assert.DoesNotContain(number.Instructions, x => x.Opcode == "load" && x.Operands.Contains(flag));
        Assert.DoesNotContain(number.Blocks, static x => x.Terminator.Shape == "br i1 %, label %, label %");
        Assert.Single(Assert.Single(give, x => x != number).Blocks, static x => x.Terminator.Shape == "br i1 %, label %, label %");

        var guard = Assert.Single(FunctionsOf(ir, c, "guard"));
        var test = Assert.Single(guard.Blocks, static x => x.Terminator.Shape == "br i1 %, label %, label %");
        var destroy = Assert.Single(test.Successors[0].Instructions, static x => x.Opcode == "call");
        Assert.StartsWith("call void @(ptr @, ptr @, i64 ", destroy.Shape, StringComparison.Ordinal);
        Assert.Equal("internal void ()", Assert.Single(CalleesOf(destroy.Callee!)).Signature);

        var views = FunctionsOf(ir, c, "view");
        var owner = Assert.Single(views, static x => x.Blocks.Any(static x => x.Terminator.Shape == "br i1 %, label %, label %"));
        var unique = Assert.Single(views, x => x != owner);
        var (set, clear) = ("store i8 1, ptr %, align 1", "store i8 0, ptr %, align 1");
        Assert.Equal([clear, clear, set, clear, set, clear, clear, clear], StoresOf(owner.Instructions, Assert.Single(FlagsOf(owner))));
        Assert.Equal([clear, clear, set, clear, clear, clear], StoresOf(unique.Instructions, Assert.Single(FlagsOf(unique))));
    }

    // SPEC 16.3.2, R4-M6a (DECISIONS R5-M100): an Open Drop destroys the remaining parts in destruction order, each through its own glue at
    // its address, never the whole value's glue: a child row by its verdict (c Keep, always; b Flag, under its flag) and a part without one
    // (a) by the own bit, set on every path in field (no test); in own, where the whole is moved on the other path, under the own bit's
    // flag, each part tested, and the moved part nothing.
    [Fact]
    public void AnOpenRowDestroysTheRemainingPartsInDestructionOrder()
    {
        var (c, ir) = Opened();
        var text = GlueOf(ir, c, "take");
        var field = Assert.Single(FunctionsOf(ir, c, "field"));
        var calls = Destructions(field, text);
        Assert.Equal(["getelementptr i8, ptr %, i64 48", "getelementptr i8, ptr %, i64 24"], calls[..2].Select(static x => x.Operands[0].Shape));
        Assert.All(calls[..2], x => Assert.Same(field.Parameters[1], x.Operands[0].Operands[0]));
        Assert.Same(field.Parameters[1], calls[2].Operands[0]);
        Assert.Equal([null, Assert.Single(FlagsOf(field)), null], calls.Select(x => GuardOf(field, x)));

        var own = Assert.Single(FunctionsOf(ir, c, "own"));
        calls = Destructions(own, text);
        Assert.Equal(["getelementptr i8, ptr %, i64 48", "ptr"], calls.Select(AddressOf));
        Assert.Same(own.Parameters[1], calls[1].Operands[0]);
        var flag = Assert.Single(FlagsOf(own));
        Assert.Equal([flag, flag], calls.Select(x => GuardOf(own, x)));
    }

    // SPEC 16.3.2, 15.1.3 (DECISIONS R5-M100): an Open fixed array's elements by decreasing index, each with a row by its verdict (element
    // 3, moved, nothing; element 1 under its flag at its offset) and each run of the others in one counted loop under the own bit (runs
    // [4], [2] and [0], each from its last element's offset), so the text does not grow with the length.
    [Fact]
    public void AnOpenFixedArrayDestroysItsRemainingElementsByRuns()
    {
        var (c, ir) = Opened();
        var text = GlueOf(ir, c, "take");
        var element = Assert.Single(FunctionsOf(ir, c, "element"));
        var calls = Destructions(element, text);
        Assert.Equal(
            ["getelementptr i8, ptr %, i64 %", "getelementptr i8, ptr %, i64 %", "getelementptr i8, ptr %, i64 24", "getelementptr i8, ptr %, i64 %"],
            calls.Select(static x => x.Operands[0].Shape));
        Assert.Equal(
            ["sub i64 96, %", "sub i64 48, %", "sub i64 0, %"],
            calls.Where(static (_, i) => i != 2).Select(static x => x.Operands[0].Operands[1].Shape));
        Assert.Equal(3, element.Instructions.Count(static x => x.Shape == "icmp eq i64 %, 1"));
        Assert.Equal([null, null, Assert.Single(FlagsOf(element)), null], calls.Select(x => GuardOf(element, x)));
    }

    // SPEC 16.3.2, 15.1.6 (DECISIONS R5-M100): an owned match Subject's Open Drop switches on its tag, as its glue does, each Case's
    // payloads by that Case's row, though A2 lists case 1's row first: case 0's unnamed payload 1 always and its payload 0 under its flag,
    // case 1's payload under another flag.
    [Fact]
    public void AnOpenCaseIsDestroyedUnderItsTag()
    {
        var (c, ir) = Opened();
        var text = GlueOf(ir, c, "take");
        var cases = Assert.Single(FunctionsOf(ir, c, "cases"));
        var tag = Assert.Single(cases.Blocks, static x => x.Terminator.Shape == "switch i32 %, label % [ i32 0, label % i32 1, label % ]");
        Assert.Equal("load i32, ptr %, align 4", tag.Terminator.Operands[0].Shape);
        var (first, second) = (tag.Successors[1], tag.Successors[2]);
        var subject = tag.Terminator.Operands[0].Operands[0];
        Assert.Equal(["getelementptr i8, ptr %, i64 32"], CallsOf(first, text).Select(static x => x.Operands[0].Shape));
        Assert.Empty(CallsOf(second, text));
        var (zero, one) = (CallsOf(first.Successors[0], text), CallsOf(second.Successors[0], text));
        Assert.Equal("getelementptr i8, ptr %, i64 8", Assert.Single(zero).Operands[0].Shape);
        Assert.Equal("getelementptr i8, ptr %, i64 8", Assert.Single(one).Operands[0].Shape);
        Assert.All(CallsOf(first, text).Concat(zero).Concat(one), x => Assert.Same(subject, x.Operands[0].Operands[0]));
        var (guard0, guard1) = (GuardOf(cases, zero[0]), GuardOf(cases, one[0]));
        Assert.NotNull(guard0);
        Assert.NotNull(guard1);
        Assert.NotSame(guard0, guard1);
    }

    // SPEC 16.3.2, X1 (DECISIONS R5-M100): a nested Open row destroys its Field's remaining parts one level down at that Field's address
    // (y: c, b under its flag, a), then the own Fields (x), then its base's by the base's own rows at the value's own address (q, p under
    // another flag); neither Triple's nor Base's glue is called.
    [Fact]
    public void ANestedOpenRowDestroysEachLayerByItsRows()
    {
        var (c, ir) = Opened();
        var text = GlueOf(ir, c, "take");
        var layers = Assert.Single(FunctionsOf(ir, c, "layers"));
        var calls = Destructions(layers, text);
        var value = layers.Parameters[1];
        var y = calls[2].Operands[0];
        Assert.Equal("getelementptr i8, ptr %, i64 72", y.Shape);
        Assert.Equal(
            ["getelementptr i8, ptr %, i64 48", "getelementptr i8, ptr %, i64 24", "getelementptr i8, ptr %, i64 72", "getelementptr i8, ptr %, i64 48",
                "getelementptr i8, ptr %, i64 24"],
            calls[..5].Select(static x => x.Operands[0].Shape));
        Assert.Equal([y, y, value, value], calls[..5].Where(static (_, i) => i != 2).Select(static x => x.Operands[0].Operands[0]));
        Assert.Same(value, calls[5].Operands[0]);
        var guards = calls.Select(x => GuardOf(layers, x)).ToArray();
        Assert.Equal([false, true, false, false, false, true], guards.Select(static x => x is not null));
        Assert.NotSame(guards[1], guards[5]);
    }

    // DECISIONS R5-M100, R5-M99: an Open Drop destroys exactly where its instance Type may need a drop: give<i32>'s writes no test or
    // destruction, give<string>'s destroys the unnamed element always and the moved one under its flag; a pair-generic body's owner case
    // destroys its remaining element and its ref case nothing (A2's open in v0, elide in v1).
    [Fact]
    public void AnOpenRowDestroysWhereItsInstanceMayNeedADrop()
    {
        var (c, ir) = Opened();
        var text = GlueOf(ir, c, "take");
        var gives = FunctionsOf(ir, c, "give");
        Assert.Equal(2, gives.Length);
        var number = Assert.Single(gives, x => Destructions(x, text).Length == 0);
        Assert.DoesNotContain(number.Blocks, static x => x.Terminator.Shape == "br i1 %, label %, label %");
        var pair = Assert.Single(gives, x => x != number);
        Assert.Equal(["getelementptr i8, ptr %, i64 24", "ptr"], Destructions(pair, text).Select(AddressOf));
        Assert.Equal([null, Assert.Single(FlagsOf(pair))], Destructions(pair, text).Select(x => GuardOf(pair, x)));

        var views = FunctionsOf(ir, c, "view");
        var owner = Assert.Single(views, static x => x.Signature == "internal void (ptr, ptr)");
        Assert.Equal("getelementptr i8, ptr %, i64 24", Assert.Single(Destructions(owner, text)).Operands[0].Shape);
        Assert.Empty(Destructions(Assert.Single(views, x => x != owner), text));
    }

    // PD12: in a body gate step 2 writes, A1 and A2 judged every runtime Drop, so one without a verdict group is a broken contract
    // (InternalInvariant_Kd at the Drop); the program's one drop row is moved to another block to make it so.
    [Fact]
    public void ARuntimeDropWithoutAVerdictIsABrokenContract()
    {
        var c = Analyze("Console.writeLine(\"a\")\n");
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl: null });
        var first = program.Bodies.ToArray().Take(body.Ordinal).Sum(x => program.Drops.RowsOf(x.Ordinal).Length);
        var row = Assert.Single(program.Drops.RowsOf(body.Ordinal).ToArray());
        program.Drops.Rows[first] = row with { Block = body.Blocks.Length };
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("InternalInvariant_Kd", (string?)"\"a\"")], Records(c));
    }

    // Gate step 2 (PD12, package C 3.8; DECISIONS R5-M96, R4-M102): a body A1 or A2 left inexact is not written; its refusal is the problem's
    // own code at its subject in the Emission partition, never InternalInvariant_Kd, and no module is written. The old analysis accepts the
    // program, so the gate is what refuses it under MIR code generation.
    [Fact]
    public void AnInexactBodyIsRefusedWithItsProblem()
    {
        var c = Analyze(WholeIncomplete);
        Assert.True(c.Accepts);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("MovedPlace_Kd", (string?)"a.indices")], Records(c));
        Assert.True(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
    }

    // R4-M102, R4-M69: a problem found in an inline default copy, which MirReport drops as its template's, marks the caller, which is refused
    // with that problem: the template's code at the template's subject.
    [Fact]
    public void ACopysProblemRefusesItsCaller()
    {
        var c = Analyze(CopiedIncomplete);
        Assert.True(c.Accepts);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("MovedPlace_Kd", (string?)"a.indices")], Records(c));
    }

    // R4-M15a, PD12: a body A1 skips at the arena limit has no drop rows and is inexact. No program the old analysis accepts reaches this
    // before G2 (the old arena trips first), so the skip is simulated: the version's table is rebuilt with f's rows, points and views
    // cleared and its fact set, A1's record at the body, every other body's kept. Interim (DECISIONS R5-M96): the refusal is one located
    // Unsupported_Kd at the body (f's), never InternalInvariant_Kd or a throw, until a generation record carries the arguments
    // OwnershipStorageLimit_Kd takes, which A1's arena problem holds as facts (G213, R4-M132).
    [Fact]
    public void AnArenaSkippedBodyIsUnsupportedUntilARecordCarriesItsArguments()
    {
        var c = Analyze("func f()\n    Console.writeLine(\"a\")\nf()\n");
        var (table, bodies) = (c.BuildMir().Drops, c.BuildMir().Bodies.ToArray());
        var ends = bodies.Select(x => (Rows: table.RowsOf(x.Ordinal).ToArray(), Points: table.PointsOf(x.Ordinal).ToArray(),
            Flags: table.FlagsOf(x.Ordinal), Views: table.ViewsOf(x.Ordinal), Fact: table.Incomplete(x.Ordinal))).ToArray();
        table.Clear();
        for (var i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" })
            {
                table.Close(0, 0, (DiagnosticCode.OwnershipStorageLimit_Kd, default, -1));
                continue;
            }

            table.Rows.AddRange(ends[i].Rows);
            table.Points.AddRange(ends[i].Points);
            table.Close(ends[i].Flags, ends[i].Views, ends[i].Fact);
        }

        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("Unsupported_Kd", (string?)"f()\n    Console.writeLine(\"a\")")], Records(c));
    }

    // PD12, R5-M4: only A1's and A2's problems refuse; a Loan problem A5 records in a body, or a control-flow problem A3 records (like A4's),
    // leaves the module written.
    [Theory]
    [InlineData("A5")]
    [InlineData("A3")]
    public void AnA3OrA5ProblemLeavesTheModuleWritten(string analysis)
    {
        var c = Analyze("Console.writeLine(\"a\")\n");
        var program = c.BuildMir();
        var (code, requirement) = analysis == "A3" ? (DiagnosticCode.RequireFallthrough_Kd, DiagnosticRequirement.ControlFlow)
            : (DiagnosticCode.ComparisonLoanConflict_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict));
        program.Problems.Add(new(code, requirement, program.Bodies[0].Source, 0, 1));
        Assert.True(c.Generate(TextWriter.Null));
        Assert.Empty(Records(c));
    }

    // Package C 3.9, SPEC 23.1.2 (the planning session's ruling Q2): a refusal skips its body only; every other item is still written, so
    // another body's own refusal is recorded beside it (g's, the same form), and no module is written.
    [Fact]
    public void ARefusalLeavesTheOtherBodiesRecorded()
    {
        var c = Analyze(WholeIncomplete + "func g()\n    let b: [2 of string] = [\"c\", \"d\"]\n    _ = b[0]@move\n    for j in b.indices\n" +
            "        Console.writeLine(\"j\")\ng()\n");
        Assert.True(c.Accepts);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("MovedPlace_Kd", (string?)"a.indices"), ("MovedPlace_Kd", "b.indices")], Records(c));
    }

    // The glue a Library function's body calls on its parameter (its one DropGlue call).
    private static IrFunction GlueOf(GeneratedIr ir, Compilation c, string name)
        => ir.Function(Regex.Match(Assert.Single(ItemTexts(c, name)), @"call void @(__kimi_drop\d+)\(").Groups[1].Value);

    // The Flags Library analyzed and generated under MIR code generation.
    private static (Compilation C, GeneratedIr Ir) Flagged()
    {
        var c = Analyze(Flags, OutputKind.Library);
        return (c, GeneratedIr.Parse(Generate(c)));
    }

    // The Opens Library analyzed and generated under MIR code generation.
    private static (Compilation C, GeneratedIr Ir) Opened()
    {
        var c = Analyze(Opens, OutputKind.Library);
        return (c, GeneratedIr.Parse(Generate(c)));
    }

    // A function's calls of a glue, in order: its destructions through that glue.
    private static IrValue[] Destructions(IrFunction function, IrFunction glue) => [.. function.Instructions.Where(x => x.Callee == glue)];

    private static IrValue[] CallsOf(IrBlock block, IrFunction glue) => [.. block.Instructions.Where(x => x.Callee == glue)];

    // A destruction's address: its offset's shape, or "ptr" for a value's own address, a parameter.
    private static string AddressOf(IrValue call) => call.Operands[0].Opcode == "getelementptr" ? call.Operands[0].Shape : "ptr";

    // The flag whose test guards the block of an instruction (DECISIONS R5-M99: the flag loaded, narrowed and branched on, its set path
    // that block), or null for none.
    private static IrValue? GuardOf(IrFunction function, IrValue instruction)
    {
        var block = Assert.Single(function.Blocks, x => x.Instructions.Contains(instruction));
        var test = function.Blocks.SingleOrDefault(x => x.Terminator.Shape == "br i1 %, label %, label %" && x.Successors[0] == block);
        return test?.Terminator.Operands[0].Operands[0].Operands[0];
    }

    // The functions of a definition's body items, one per instance (ItemTexts).
    private static IrFunction[] FunctionsOf(GeneratedIr ir, Compilation c, string name)
        => [.. ItemTexts(c, name).Select(x => ir.Function(Regex.Match(x, @"define internal \S+ @(__kimi_f\d+)\(").Groups[1].Value))];

    // A function's drop flags (DECISIONS R5-M99): its entry block's i8 slots, each stored only the constants 0 and 1.
    private static IrValue[] FlagsOf(IrFunction function) => [.. function.Blocks[0].Instructions.Where(x => x.Shape == "alloca i8, align 1" &&
        StoresOf(function.Instructions, x).All(static x => x is "store i8 0, ptr %, align 1" or "store i8 1, ptr %, align 1") &&
        StoresOf(function.Instructions, x).Length > 0)];

    // The stores into a slot among instructions, in order.
    private static string[] StoresOf(List<IrValue> instructions, IrValue slot)
        => [.. instructions.Where(x => x.Opcode == "store" && x.Operands[^1] == slot).Select(static x => x.Shape)];

    // A function's calls, and their callees in order.
    private static IrValue[] CallsOf(IrFunction function) => [.. function.Instructions.Where(static x => x.Opcode == "call")];

    private static IrFunction[] CalleesOf(IrFunction function) => CalleesOf(function.Instructions);

    private static IrFunction[] CalleesOf(List<IrValue> instructions) => [.. instructions.Where(static x => x.Opcode == "call").Select(static x => x.Callee!)];
}
