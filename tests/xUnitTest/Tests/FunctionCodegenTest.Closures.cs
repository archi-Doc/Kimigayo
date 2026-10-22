// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The closure rows of MIR code generation (R5 C5a; package C 3.2 I8, 3.6 and row 14; DECISIONS R5-M102): a Closure's environment, its body's
// environment parameter, value calls of a Closure, a function Item and a common Function value, function item constants, a Closure
// environment's glue and Open rows, the callee operand's copy slot, and the operand destroyed after an owner call of a borrowing callee
// (SPEC 8.10).
public sealed partial class FunctionCodegenTest
{
    // A Library's Closures (SPEC 7.7.2-7.7.4; DECISIONS R5-M102): captures by value and by reference in a wide environment, a drop-needing
    // one of eight bytes and none; Shared, Exclusive and Consuming calls; a function item constant called; a common Function value called
    // through a borrow with a string argument.
    private const string Closures =
        "struct Guard\n    public let id: i32 = 1\n    drop => ()\nfunc take(s: string) -> i64 => 1\nfunc add(a: i32, b: i32) -> i32 => a + b\n" +
        "func wide(a: i8, b: i64, s: string) -> i64\n    let n: i64 = 3\n    let f = func [a, b, s@move, n@ref] () -> i64 => b + n@follow\n" +
        "    return f()\nfunc guarded(g: Guard) -> i32\n    let f = func [g@move] () -> i32 => g.id\n    return f()\n" +
        "func empty() -> i32\n    let f = func [] () -> i32 => 4\n    return f()\nfunc counter() -> i32\n    var n: i32 = 0\n" +
        "    var f = func [var n] () -> i32\n        n += 1\n        return n\n    return f@uniq()\nfunc consume(s: string) -> i64\n" +
        "    let f = func [s@move] () -> i64 => take(s@move)\n    return f@move()\nfunc item() -> i32\n    let g = add\n    return g(1, 2)\n" +
        "func erased(f: ref/((ref/string) -> i32), s: ref/string) -> i32 => f(s)\n";

    // SPEC 8.10 (DECISIONS R5-M102): an owner Callable call of a Shared Closure body borrows the transferred value and destroys it after the
    // call; of a Consuming one, the body destroys it.
    private const string Owners =
        "func invoke<F>(action: F) -> i32\n    F is Callable<owner, () -> i32>\n    return action@move()\nfunc shared(s: string) -> i32\n" +
        "    let f = func [s@move] () -> i32 => 1\n    return invoke(f@move)\nfunc take(s: string) -> i32 => 2\nfunc consuming(s: string) -> i32\n" +
        "    let f = func [s@move] () -> i32 => take(s@move)\n    return invoke(f@move)\n";

    // SPEC 7.7.3 (DECISIONS R5-M102): a Consuming call Copies a Copy Closure, so each call's environment is its own copy, which the body
    // changes; destroying an environment destroys its captures last to first (SPEC 16.4), a Consuming body what remains of its own.
    public static TheoryData<string, string, string> ClosurePrograms => new()
    {
        {
            "ClosureRowsCopyReceiver",
            "func twice() -> i32\n    let n: i32 = 7\n    let f = func [var n] () -> i32\n        n += 1\n        return n@move\n    return f() + f()\n" +
            "require twice() == 16 else => $abort(\"copy\")\n",
            string.Empty
        },
        {
            "ClosureRowsDestruction",
            "struct Noisy\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n" +
            "func take(n: Noisy) => ()\nfunc run()\n    let a = Noisy.init(\"a\")\n    let b = Noisy.init(\"b\")\n" +
            "    let f = func [a@move, b@move] () => take(a@move)\n    f@move()\n    Console.writeLine(\"after\")\nfunc order()\n" +
            "    let first = Noisy.init(\"first\")\n    let second = Noisy.init(\"second\")\n    let f = func [first@move, second@move] () => ()\n" +
            "    Console.writeLine(\"made\")\nrun()\norder()\n",
            "a\nb\nafter\nmade\nsecond\nfirst\n"
        },
    };

    [Theory]
    [MemberData(nameof(ClosurePrograms))]
    public void ClosureProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // IMPL 21.1.2 (DECISIONS R5-M102: C5a's inline-environment requirement): a Closure's environment is the Closure value, each capture at its
    // Field offset in the Place's own storage, at every size; forming it allocates nothing, a 48-byte, an 8-byte drop-needing and an empty
    // environment alike: a scalar, a string's record and a borrow's pointer stored, a value in memory copied; an empty one has no storage.
    [Fact]
    public void AClosureEnvironmentIsItsValueAtEverySize()
    {
        var c = Analyze(Closures, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var (wide, guarded, empty) = (Assert.Single(FunctionsOf(ir, c, "wide")), Assert.Single(FunctionsOf(ir, c, "guarded")), Assert.Single(FunctionsOf(ir, c, "empty")));
        Assert.All(new[] { wide, guarded, empty }, x => Assert.DoesNotContain(ir.Function("__kimi_alloc"), CalleesOf(x)));
        var environment = Assert.Single(wide.Blocks[0].Instructions, static x => x.Shape == "alloca { [0 x i64], [48 x i8] }, align 8");
        Assert.Equal(
            ["store %kimi.string %, ptr %, align 8", "store i64 %, ptr %, align 8", "store i8 %, ptr %, align 1", "store ptr %, ptr %, align 8"],
            wide.Instructions.Where(x => x.Opcode == "store" && (x.Operands[^1] == environment ||
                (x.Operands[^1].Opcode == "getelementptr" && x.Operands[^1].Operands[0] == environment))).Select(static x => x.Shape).Order(StringComparer.Ordinal));
        var held = Assert.Single(guarded.Blocks[0].Instructions, static x => x.Shape == "alloca { [0 x i32], [4 x i8] }, align 4");
        Assert.Contains(CallsOf(guarded), x => x.Shape == "call void @(ptr align 4 %, ptr align 4 %, i64 4, i1 false)" && x.Operands[0] == held);
        Assert.DoesNotContain(empty.Blocks[0].Instructions, static x => x.Shape.StartsWith("alloca {", StringComparison.Ordinal));
    }

    // SPEC 7.7.3, Abi's ConcreteClosure (DECISIONS R5-M102): a Closure body takes its environment pointer first and the context last; a
    // Shared and an Exclusive body store the pointer as their environment local, a Consuming body uses it in place; a call passes the
    // environment (a borrow's pointer, or the moved value's own storage) and a null context.
    [Fact]
    public void AClosureCallPassesItsEnvironmentAndANullContext()
    {
        var c = Analyze(Closures, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var shared = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "wide"))), static x => x.Shape == "call i64 @(ptr %, ptr null)");
        Assert.Equal("internal i64 (ptr, ptr)", shared.Callee!.Signature);
        Assert.Contains(shared.Callee.Blocks[0].Instructions, x => x.Opcode == "store" && x.Operands[0] == shared.Callee.Parameters[0]);
        var exclusive = Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, "counter"))), static x => x.Shape == "call i32 @(ptr %, ptr null)");
        Assert.Contains(exclusive.Callee!.Blocks[0].Instructions, x => x.Opcode == "store" && x.Operands[0] == exclusive.Callee.Parameters[0]);
        var consume = Assert.Single(FunctionsOf(ir, c, "consume"));
        var owned = Assert.Single(CallsOf(consume), static x => x.Shape == "call i64 @(ptr %, ptr null)");
        Assert.Equal("alloca { [0 x i64], [24 x i8] }, align 8", owned.Operands[0].Shape);
        Assert.DoesNotContain(CallsOf(consume), static x => x.Callee?.Signature == "internal void (ptr, ptr, i64)");
        var body = owned.Callee!;
        Assert.DoesNotContain(body.Instructions, x => x.Opcode == "store" && x.Operands[0] == body.Parameters[0]);
        Assert.Contains(body.Instructions, x => x.Shape == "load %kimi.string, ptr %, align 8" && x.Operands[0] == body.Parameters[0]);
    }

    // SPEC 7.7.4 (DECISIONS R5-M102): a function item constant writes nothing and its call is a direct call of the function; a common
    // Function value's call loads its environment word and its table, then calls the table's entry with the word, the arguments (a string
    // borrow with its attributes, package C row 14), the table's context and the call's location (SPEC 22.5.1).
    [Fact]
    public void ItemAndErasedCallsReachTheirCode()
    {
        var c = Analyze(Closures, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        Assert.Equal([Assert.Single(FunctionsOf(ir, c, "add"))], CalleesOf(Assert.Single(FunctionsOf(ir, c, "item"))));
        var erased = Assert.Single(FunctionsOf(ir, c, "erased"));
        var call = Assert.Single(CallsOf(erased));
        Assert.Null(call.Callee);
        Assert.Matches(@"^call i32 %\(i64 %, ptr noundef nonnull align 8 dereferenceable\(24\) %, ptr %, ptr @, i64 \d+\)$", call.Shape);
        var (entry, word, context) = (call.Operands[0], call.Operands[1], call.Operands[3]);
        Assert.Equal(["load ptr, ptr %, align 8", "load i64, ptr %, align 8", "load ptr, ptr %, align 8"], new[] { entry, word, context }.Select(static x => x.Shape));
        var table = entry.Operands[0];
        Assert.Equal("load ptr, ptr %, align 8", table.Shape);
        Assert.Equal(["getelementptr i8, ptr %, i64 8", "getelementptr i8, ptr %, i64 8"], new[] { table.Operands[0], context.Operands[0] }.Select(static x => x.Shape));
        Assert.Same(table, context.Operands[0].Operands[0]);
        Assert.Same(word.Operands[0], table.Operands[0].Operands[0]);
    }

    // SPEC 8.10 (DECISIONS R5-M102): the instance's owner call of a Shared body passes the transferred value's address and then destroys it by
    // its Type's glue at the call's location; of a Consuming body it passes it alone, the body destroying what it leaves.
    [Fact]
    public void AnOwnerCallDestroysWhatABorrowingBodyLeaves()
    {
        var c = Analyze(Owners, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var invocations = FunctionsOf(ir, c, "invoke").Select(static x => CallsOf(x).Where(static y => y.Shape.StartsWith("call i32", StringComparison.Ordinal) ||
            y.Callee?.Signature == "internal void (ptr, ptr, i64)").ToArray()).ToArray();
        Assert.Equal(2, invocations.Length);
        var borrowing = Assert.Single(invocations, static x => x.Length == 2);
        Assert.Equal("call i32 @(ptr %, ptr null)", borrowing[0].Shape);
        Assert.Matches(@"^call void @\(ptr %, ptr @, i64 \d+\)$", borrowing[1].Shape);
        Assert.Same(borrowing[0].Operands[0], borrowing[1].Operands[0]);
        Assert.Contains(borrowing[0].Callee!.Blocks[0].Instructions, x => x.Opcode == "store" && x.Operands[0] == borrowing[0].Callee!.Parameters[0]);
        Assert.Equal("call i32 @(ptr %, ptr null)", Assert.Single(Assert.Single(invocations, static x => x.Length == 1)).Shape);
    }

    // SPEC 7.7.3 (DECISIONS R5-M102): a Consuming call of a Copy Closure passes a copy slot of its own per call, numbered after the body's
    // operands by the call's block, each filled from the Closure just before its call.
    [Fact]
    public void ACopyReceiverHasACopySlotPerCall()
    {
        const string Source =
            "func twice() -> i32\n    let n: i32 = 7\n    let f = func [var n] () -> i32\n        n += 1\n        return n@move\n    return f() + f()\n";
        var c = Analyze(Source, OutputKind.Library);
        var twice = Assert.Single(FunctionsOf(GeneratedIr.Parse(Generate(c)), c, "twice"));
        var calls = CallsOf(twice).Where(static x => x.Shape == "call i32 @(ptr %, ptr null)").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.NotSame(calls[0].Operands[0], calls[1].Operands[0]);
        Assert.All(calls, x => Assert.Contains(CallsOf(twice), y => y.Shape == "call void @(ptr align 4 %, ptr align 4 %, i64 4, i1 false)" && y.Operands[0] == x.Operands[0]));
    }
}
