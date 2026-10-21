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
    // zero-sized Guard declaring a drop, made by a call; a generic value at i32, which needs no drop, and at string; a pair-generic value in
    // its owner case (Flag) and its reference case (Elide).
    private const string Flags =
        "struct Guard\n    drop => ()\nfunc take(s: string) => ()\nfunc keep(g: Guard) => ()\nfunc sink<T>(x: T) => ()\n" +
        "func argument(c: bool, s: string)\n    if c => take(s@move)\nfunc local(c: bool)\n    let s = \"x\"\n    if c => take(s@move)\n" +
        "func guard(c: bool)\n    let g = Guard.init()\n    if c => keep(g@move)\nfunc give<T>(c: bool, x: T)\n    if c => sink(x@move)\n" +
        "func view<s/T>(c: bool, v: s/T)\n    s is owner or ref\n    if c => sink(v@move)\nfunc use()\n    give(true, 1)\n    give(true, \"a\")\n" +
        "    let k = \"k\"\n    view(true, \"b\")\n    view(true, k@ref)\n";

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
    public void AFlagIsABoolSlotClearedAtEntryWhereAnArgumentsIsSet()
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
    // calls its destructor with no argument. A pair-generic body's points are its every view's, and only the owner case's Drop tests its
    // flag (the reference case's verdict is Elide).
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
        var owner = Assert.Single(views, static x => x.Signature == "internal void (i1, ptr)");
        var reference = Assert.Single(views, x => x != owner);
        Assert.Equal(StoresOf(owner.Instructions, Assert.Single(FlagsOf(owner))), StoresOf(reference.Instructions, Assert.Single(FlagsOf(reference))));
        Assert.Single(owner.Blocks, static x => x.Terminator.Shape == "br i1 %, label %, label %");
        Assert.DoesNotContain(reference.Blocks, static x => x.Terminator.Shape == "br i1 %, label %, label %");
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
    // Unsupported_Kd at the body (f's), never InternalInvariant_Kd or a throw, until A1's arena problem carries the arguments
    // OwnershipStorageLimit_Kd takes.
    [Fact]
    public void AnArenaSkippedBodyIsUnsupportedUntilA1PublishesItsArguments()
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
                table.Close(0, 0, (DiagnosticCode.OwnershipStorageLimit_Kd, bodies[i].Source));
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
    // its own gaps are recorded beside the refusal (g's Closure environment and value call, C5a's rows), and no module is written.
    [Fact]
    public void ARefusalLeavesTheOtherBodiesGapsRecorded()
    {
        var c = Analyze(WholeIncomplete + "func g() -> i32\n    let k = func () -> i32 => 1\n    return k()\nlet r = g()\n");
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("MovedPlace_Kd", (string?)"a.indices"), ("Unsupported_Kd", "func () -> i32 => 1"), ("Unsupported_Kd", "k()")], Records(c));
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
