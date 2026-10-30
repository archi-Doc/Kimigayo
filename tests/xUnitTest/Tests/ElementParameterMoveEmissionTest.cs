// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementParameterMoveEmissionTest
{
    private const string Same = "func same(a: ref/string, b: ref/string) -> bool => a == b\n";
    private const string Conditional = "func f(a: (string, string), take: bool)\n    if take\n        let moved = a.0@move\nf((\"first\", \"last\"), true)\nf((\"first\", \"last\"), false)";

    public static TheoryData<string, string, string, string, int[]> Fixtures => new()
    {
        { "Return", "func f(a: (string, string)) -> string => a.0@move\nConsole.writeLine(f((\"first\", \"last\")))", "first\n", "first=1;last=1", [1, 0] },
        { "Local", "func f(a: (string, string))\n    let moved = a.0@move\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [0, 1] },
        { "Conditional", Conditional, string.Empty, "first=2;last=2", [0, 1, 1, 0] },
        { "Both", "func f(a: (string, string))\n    let first = a.0@move\n    let last = a.1@move\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [1, 0] },
        { "CopyAfter", "func f(a: (string, i32)) -> i32\n    let moved = a.0@move\n    return a.1\nif f((\"first\", 42)) == 42 => Console.writeLine(\"ok\")", "ok\n", "first=1;ok=1", [0, 1] },
        { "Nested", "func f(a: (string, (string, string))) -> string => a.1.0@move\nConsole.writeLine(f((\"outer\", (\"first\", \"last\"))))", "first\n", "outer=1;first=1;last=1", [2, 0, 1] },
        { "Aggregate", "func f(a: ((string, string), string)) -> (string, string) => a.0@move\nlet moved = f(((\"first\", \"last\"), \"outer\"))", string.Empty, "first=1;last=1;outer=1", [2, 1, 0] },
        { "Array", "func f(a: [5 of string]) -> string => a[(0x2)]@move\nlet a: [5 of string] = [\"a\", \"b\", \"c\", \"d\", \"e\"]\nConsole.writeLine(f(a@move))", "c\n", "a=1;b=1;c=1;d=1;e=1", [4, 3, 1, 0, 2] },
        { "ArrayNested", "func f(a: (string, [2 of string])) -> [2 of string] => a.1@move\nlet a: (string, [2 of string]) = (\"outer\", [\"first\", \"last\"])\nlet moved = f(a@move)", string.Empty, "outer=1;first=1;last=1", [0, 2, 1] },
        { "BorrowAfter", Same + "func f(a: (string, string))\n    let moved = a.0@move\n    if same(a.1, a.1) => Console.writeLine(\"ok\")\nf((\"first\", \"last\"))", "ok\n", "first=1;last=1;ok=1", [2, 0, 1] },
        { "BorrowBefore", Same + "func f(a: (string, string)) -> string\n    let equal = same(a.0, a.0)\n    return a.0@move\nConsole.writeLine(f((\"first\", \"last\")))", "first\n", "first=1;last=1", [1, 0] },
        { "SharedSibling", "func inspect(a: ref/string, b: string)\n    if a == a => Console.writeLine(b)\nfunc f(a: (string, string)) => inspect(a.0, a.1@move)\nf((\"first\", \"last\"))", "last\n", "first=1;last=1", [1, 0] },
        { "DeferMove", "func f(a: (string, string))\n    defer\n        let moved = a.0@move\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [0, 1] },
        { "DeferClones", "func f(a: (string, string), early: bool)\n    defer\n        let moved = a.0@move\n    if early => return\nf((\"first\", \"last\"), true)\nf((\"first\", \"last\"), false)", string.Empty, "first=2;last=2", [0, 1, 0, 1] },
        { "Selection", "func f(a: (string, string), take: bool) -> string => if take => a.0@move else => a.1@move\nConsole.writeLine(f((\"first\", \"last\"), true))\nConsole.writeLine(f((\"first\", \"last\"), false))", "first\nlast\n", "first=2;last=2", [1, 0, 0, 1] },
        { "WholeOrPart", "func f(a: (string, string), whole: bool)\n    if whole\n        let moved = a@move\n    else\n        let moved = a.0@move\nf((\"first\", \"last\"), true)\nf((\"first\", \"last\"), false)", string.Empty, "first=2;last=2", [1, 0, 0, 1] },
        { "Parameters", "func f(a: (string, string), b: (string, string)) -> string\n    let moved = b.0@move\n    return a.0@move\nConsole.writeLine(f((\"a\", \"b\"), (\"c\", \"d\")))", "a\n", "a=1;b=1;c=1;d=1", [2, 3, 1, 0] },
        { "LoopExit", "func f(a: (string, string)) -> string\n    loop\n        return a.0@move\nConsole.writeLine(f((\"first\", \"last\")))", "first\n", "first=1;last=1", [1, 0] },
        { "Covered", "func f(a: (string, string))\n    match true\n        _ => ()\n        true => (label work: do\n            let moved = a.0@move\n        )\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [1, 0] },
        { "Transfer", "func inspect(a: string, b: bool) => ()\nfunc f(a: (string, string)) -> string\n    inspect(a.0@move, (return \"out\"))\n    return \"bad\"\nConsole.writeLine(f((\"first\", \"last\")))", "out\n", "first=1;last=1;out=1;bad=0", [0, 1, 2] },
        { "ZeroSize", "func f(a: ([0 of string], string)) -> [0 of string] => a.0@move\nlet a: ([0 of string], string) = ([], \"last\")\nlet moved = f(a@move)", string.Empty, "last=1", [0] },
        { "GenericNested", "func f<T>(a: ((T, string), string)) -> T => a.0.0@move\nConsole.writeLine(f<string>(((\"first\", \"last\"), \"outer\")))", "first\n", "first=1;last=1;outer=1", [2, 1, 0] },
        { "GenericAggregate", "func f<T>(a: ((T, string), string)) -> T => a.0.0@move\nlet moved = f<(string, string)>((((\"first\", \"second\"), \"last\"), \"outer\"))", string.Empty, "first=1;second=1;last=1;outer=1", [3, 2, 1, 0] },
        { "ConditionalNested", "func f(a: ((string, string), string), take: bool)\n    if take\n        let moved = a.0.0@move\n    else\n        let moved = a.0@move\nf(((\"first\", \"last\"), \"outer\"), true)\nf(((\"first\", \"last\"), \"outer\"), false)", string.Empty, "first=2;last=2;outer=2", [0, 2, 1, 1, 0, 2] },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ParametersTransferOnlyTheSelectedResponsibility(string name, string source, string stdout, string destructions, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("ElementParameterMove" + name, source, stdout);
        StringEmissionTest.WriteAuditedFixture("ElementParameterMove" + name, ir, stdout, destructions, order: order);
    }

    [Theory]
    [InlineData("func f(a: (string, string))\n    let moved = a.0@move\n    let twice = a.0@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: (string, string))\n    let moved = a.0@move\n    let whole = a@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: (string, string), take: bool) -> string\n    if take\n        let moved = a.0@move\n    return a.0@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: (string, string))\n    loop\n        let moved = a.0@move\n        continue", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: (string, string))\n    let moved = a.0@move\n    let equal = a.0 == a.0", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: (string, string))\n    defer\n        let whole = a@move\n    let moved = a.0@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func inspect(a: ref/string, b: string) => ()\nfunc f(a: (string, string)) => inspect(a.0, a.0@move)", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("func take(a: string) -> string => a@move\nfunc f(a: (string, string)) -> bool => a.0 == take(a.0@move)", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("func f(a: [2 of string], i: isize) -> string => a[i]@move", DiagnosticCode.StaticMovePathRequired_Kd)]
    [InlineData("func f(a: [2 of string]) -> string => a[0 + 0]@move", DiagnosticCode.StaticMovePathRequired_Kd)]
    [InlineData("let moved = (\"first\", \"last\").0", DiagnosticCode.Unsupported_Kd)]
    [InlineData("func f(a: ([0 of string], string))\n    let moved = a.0@move\n    let twice = a.0@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: ((string, string), string))\n    let moved = a.0@move\n    let nested = a.0.0@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(a: (string, [1 of i32])) -> i32\n    return a.1[(label work: do\n        let moved = a.0@move\n        exit to work 0\n    )]", DiagnosticCode.ComparisonLoanConflict_Kd)]
    public void InvalidMovesFailBeforeEmission(string source, DiagnosticCode failure)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nConsole.writeLine(\"ok\")");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, x => x.Code == failure);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("a.0 = \"new\"")]
    [InlineData("a = (\"new\", 1)")]
    [InlineData("a.1 += 1")]
    [InlineData("++a.1")]
    public void ParametersRemainImmutable(string statement)
    {
        var c = MinimalEmissionTest.Analyze("func f(a: (string, i32))\n    let moved = a.0@move\n    " + statement + "\nf((\"first\", 0))");
        Assert.False(c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void BoundsAbortDoesNotDestroyTheMovedValueOrParameterRemainder()
    {
        const string Source = "func f(a: (string, string))\n    let moved = a.0@move\n    let empty: [0 of i32] = []\n    let n = empty[0]\nf((\"first\", \"last\"))";
        const string Error = "Hello.kimi:4:13: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementParameterMoveBounds", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementParameterMoveBounds", ir, string.Empty, "first=0;last=0", 1, Error);
    }

    [Fact]
    public void AbortingDeferPreventsRemainingDestructionAndReturnDelivery()
    {
        const string Source = "func f(a: (string, string)) -> string\n    defer\n        var n = 2147483647\n        n += 1\n    return a.0@move\nConsole.writeLine(f((\"first\", \"last\")))";
        const string Error = "Hello.kimi:4:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementParameterMoveCleanupAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementParameterMoveCleanupAbort", ir, string.Empty, "first=0;last=0", 1, Error);
    }

    [Fact]
    public void NonterminatingDeferPreventsRemainingDestructionAndReturnDelivery()
        => ScalarEmissionTest.EmitFixture(
            "ElementParameterMoveDivergent",
            "func f(a: (string, string)) -> string\n    defer => loop => ()\n    return a.0@move\nConsole.writeLine(f((\"first\", \"last\")))",
            string.Empty,
            observationMilliseconds: 300);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmParameterMovesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Same + "func f(a: ((string, string), string), take: bool)\n    defer\n        let equal = same(a.1, a.1)\n    if take\n        let moved = a.0.0@move\n    else\n        let moved = a.0@move\nf(((\"first\", \"last\"), \"out\"), true)");
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        var bindingBytes = AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        var bytes = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.Generate(TextWriter.Null);
        });
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
