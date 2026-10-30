// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementBorrowOwnerEmissionTest
{
    private const string Same = "func same(left: ref/string, right: ref/string) -> bool => left == right\n";
    private const string Make = "func make() -> (string, i32) => (\"held\", 42)\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Parameter", "func f(a: (string, i32)) -> bool => same(a.0, a.0)\nif f((\"held\", 0)) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterArray", "func f(a: [2 of string]) -> bool => a[0] < a[1]\nlet a: [2 of string] = [\"first\", \"last\"]\nif f(a@move) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterRuntimeIndex", "func f(a: [1 of string], i: isize) -> bool => same(a[i], \"held\")\nif f([\"held\"], 0) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterMoveAfter", "func f(a: (string, i32)) -> (string, i32)\n    let equal = same(a.0, a.0)\n    return a@move\nif f((\"held\", 0)).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterNested", "func f(a: (string, (string, i32))) -> bool => same(a.0, a.1.0)\nif f((\"held\", (\"held\", 42))) => Console.writeLine(\"ok\")", "ok\n" },
        { "ParameterDefer", "func f(a: (string, i32))\n    defer\n        if a.0 == \"held\" => Console.writeLine(\"ok\")\nf((\"held\", 0))", "ok\n" },
        { "Literal", "if (\"held\", 42).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "LiteralArguments", "if same((\"held\", 0).0, (\"held\", 1).0) => Console.writeLine(\"ok\")", "ok\n" },
        { "CallComparison", "if make().0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "CallArguments", "if same(make().0, make().0) => Console.writeLine(\"ok\")", "ok\n" },
        { "CallArray", "func names() -> [2 of string] => [\"first\", \"last\"]\nif names()[0] < names()[1] => Console.writeLine(\"ok\")", "ok\n" },
        { "If", "func f(flag: bool) -> bool => same((if flag => (\"held\", 1) else => (\"held\", 2)).0, \"held\")\nif f(true) and f(false) => Console.writeLine(\"ok\")", "ok\n" },
        { "Do", "if (label work: do\n    exit to work (\"held\", 42)\n).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "LoopResult", "if same((loop\n    exit (\"held\", 42)\n).0, \"held\") => Console.writeLine(\"ok\")", "ok\n" },
        { "MatchResult", "if (match true\n    true => (\"held\", 42)\n    false => (\"other\", 0)\n).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "NestedResult", "func nested() -> (i32, (string, i32)) => (0, (\"held\", 42))\nif same(nested().1.0, (if true => nested() else => nested()).1.0) => Console.writeLine(\"ok\")", "ok\n" },
        { "ShortCircuit", "func f() -> (string, i32)\n    Console.writeLine(\"bad\")\n    return (\"held\", 0)\nlet skipped = false and same(f().0, \"held\")\nlet skippedAgain = true or f().0 == \"held\"\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Evaluation", "func f() -> (string, i32)\n    Console.writeLine(\"make\")\n    return (\"held\", 0)\nif same(f().0, f().0) => Console.writeLine(\"ok\")", "make\nmake\nok\n" },
        { "Guard", "match \"other\"\n    let s if same(make().0, s) => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"ok\")", "ok\n" },
        { "NestedGuard", "func three(a: ref/string, b: ref/string, c: ref/string) -> bool => b == c\nmatch \"other\"\n    let s if three(make().0, s, \"other\") => Console.writeLine(\"ok\")\n    _ => Console.writeLine(\"bad\")", "ok\n" },
        { "IndependentResult", "func inspect(a: ref/string) -> (string, i32) => (\"held\", 0)\nif inspect(make().0).0 == \"held\" => Console.writeLine(\"ok\")", "ok\n" },
        { "MixedLocal", "let a = (\"held\", 0)\nif same(a.0, make().0) and same(make().0, a.0) => Console.writeLine(\"ok\")\nlet moved = a@move", "ok\n" },
        { "DeferClones", "func f(flag: bool)\n    defer\n        if same((label work: do\n            exit to work (\"held\", 0)\n        ).0, make().0) => Console.writeLine(\"ok\")\n    if flag => return\nf(true)\nf(false)", "ok\nok\n" },
        { "Repeated", "var n = 0\nloop\n    if not same(make().0, (\"held\", 0).0) => exit\n    n += 1\n    if n < 3 => continue\n    exit\nif n == 3 => Console.writeLine(\"ok\")", "ok\n" },
        { "Unreachable", "func f()\n    return\n    let equal = same(make().0, (\"held\", 0).0)\nf()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Covered", "match true\n    _ => Console.writeLine(\"ok\")\n    true => (label work: do\n        let equal = same(make().0, (\"held\", 0).0)\n    )", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void OwnersSupportStringBorrowing(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ElementBorrowOwner" + name, Same + Make + source, stdout);

    [Theory]
    [InlineData("func inspect(a: ref/string, b: (string, i32)) => ()\nfunc f(a: (string, i32)) => inspect(a.0, a@move)", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("func take(a: (string, i32)) -> string => \"held\"\nfunc f(a: (string, i32)) -> bool => a.0 == take(a@move)", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("func f(a: (string, i32)) -> bool\n    let moved = a@move\n    return same(a.0, a.0)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let value = make().0", DiagnosticCode.Unsupported_Kd)]
    public void BorrowingDoesNotGrantMove(string source, DiagnosticCode failure)
    {
        var c = MinimalEmissionTest.Analyze(Same + Make + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, x => x.Code == failure);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    public static TheoryData<string, string, string, string, int[]> Audits => new()
    {
        { "LiteralCondition", "if (\"head\", \"tail\").0 == \"head\" => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;ok=1", [1, 0, 2] },
        { "CallCondition", "func make() -> (string, string) => (\"head\", \"tail\")\nif make().0 == \"head\" => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;ok=1", [1, 0, 2] },
        { "ParameterCleanup", "func f(a: (string, string)) -> bool => a.0 == a.0\nif f((\"head\", \"tail\")) => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;ok=1", [1, 0, 2] },
        { "ReverseTemporaries", Same + "if same((\"head\", \"tail\").0, (\"head\", \"last\").0) => Console.writeLine(\"ok\")", "ok\n", "head=2;tail=1;last=1;ok=1", [2, 0, 1, 0, 3] },
        { "ConditionalLiteral", "func f(flag: bool) -> bool => flag and (\"head\", \"tail\").0 == \"head\"\nif f(true) and not f(false) => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;ok=1", [1, 0, 2] },
        { "ConditionalCall", "func make() -> (string, string) => (\"head\", \"tail\")\nfunc f(flag: bool) -> bool => flag and make().0 == \"head\"\nif f(true) and not f(false) => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;ok=1", [1, 0, 2] },
        { "ConditionalSelection", "func f(flag: bool) -> bool => flag and (if flag => (\"head\", \"tail\") else => (\"bad\", \"bad\")).0 == \"head\"\nif f(true) and not f(false) => Console.writeLine(\"ok\")", "ok\n", "head=1;tail=1;bad=0;ok=1", [1, 0, 3] },
        { "RepeatedConditional", "var n = 0\nwhile n < 4\n    let equal = n % 2 == 0 and (if n == 0 => (\"head\", \"tail\") else => (\"head\", \"tail\")).0 == \"head\"\n    n += 1", string.Empty, "head=2;tail=2", [1, 0, 1, 0] },
        { "DeferredConditional", "func f(flag: bool, early: bool)\n    defer\n        let equal = flag and (if flag => (\"head\", \"tail\") else => (\"bad\", \"bad\")).0 == \"head\"\n    if early => return\nf(true, true)\nf(false, false)\nf(true, false)\nf(false, true)", string.Empty, "head=2;tail=2;bad=0", [1, 0, 1, 0] },
        { "ArgumentTransfer", "func inspect(a: ref/string, b: bool) => ()\nfunc f() -> string\n    defer => Console.writeLine(\"cleanup\")\n    inspect((\"head\", \"tail\").0, (return \"out\"))\n    return \"bad\"\nConsole.writeLine(f())", "cleanup\nout\n", "head=1;tail=1;cleanup=1;out=1;bad=0", [1, 0, 2, 3] },
        { "ComparisonTransfer", "func f() -> string\n    defer => Console.writeLine(\"cleanup\")\n    (\"head\", \"tail\").0 == (return \"out\")\n    return \"bad\"\nConsole.writeLine(f())", "cleanup\nout\n", "head=1;tail=1;cleanup=1;out=1;bad=0", [1, 0, 2, 3] },
        { "LastUseIsNotDestruction", "func later() -> i32\n    Console.writeLine(\"later\")\n    return 42\nfunc inspect(a: bool, b: i32) => ()\ninspect((\"head\", \"tail\").0 == \"head\", later())\nConsole.writeLine(\"ok\")", "later\nok\n", "head=1;tail=1;later=1;ok=1", [2, 1, 0, 3] },
        { "CalleeCleanup", "func inspect(a: ref/string) -> string\n    defer\n        if a == a => Console.writeLine(\"cleanup\")\n    return \"out\"\nConsole.writeLine(inspect((\"head\", \"tail\").0))", "cleanup\nout\n", "head=1;tail=1;cleanup=1;out=1", [2, 3, 1, 0] },
    };

    [Theory]
    [MemberData(nameof(Audits))]
    public void OwnersKeepTheirSpecifiedLifetime(string name, string source, string stdout, string destructions, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowOwnerLifetime" + name, source, stdout);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowOwnerLifetime" + name, ir, stdout, destructions, order: order);
    }

    [Theory]
    [InlineData("Argument", "func inspect(a: ref/string, b: i32) => ()\nlet empty: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\ninspect((\"head\", \"tail\").0, empty[0])", 4, 29)]
    [InlineData("Construction", "let empty: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\nlet equal = ((\"head\", empty[0]), \"tail\").0.0 == \"head\"", 3, 23)]
    public void AbortDoesNotDestroyBorrowOwners(string name, string source, int line, int column)
    {
        var error = $"Hello.kimi:{line}:{column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowOwnerAbort" + name, source, string.Empty, 1, error);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowOwnerAbort" + name, ir, string.Empty, "head=0;tail=0;bad=0", 1, error);
    }

    [Theory]
    [InlineData("Call", "func forever(a: ref/string) -> Never\n    loop => ()\nforever((\"held\", 0).0)")]
    [InlineData("ResultCleanup", "let equal = (label work: do\n    defer => loop => ()\n    exit to work (\"held\", 0)\n).0 == \"held\"")]
    public void NonterminationCannotReleaseOrDeliverAnOwner(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementBorrowOwnerDivergent" + name, source, string.Empty, observationMilliseconds: 300);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAndOwnerBorrowEmissionAllocateNothing()
    {
        const string Source = Same + Make + "func f(a: (string, i32), flag: bool) -> bool\n    defer\n        let equal = flag and (if flag => make() else => (\"held\", 0)).0 == a.0\n    if flag => return same(a.0, make().0)\n    return flag or same((\"held\", 0).0, a.0)\nf(make(), true)\nf(make(), false)";
        var c = MinimalEmissionTest.Analyze(Source);
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
