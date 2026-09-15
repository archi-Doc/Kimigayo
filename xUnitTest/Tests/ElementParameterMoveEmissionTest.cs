// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementParameterMoveEmissionTest
{
    private const string Same = "func same(a: ref/string, b: ref/string) -> bool => a == b\n";
    private const string Conditional = "func f(a: (string, string), take: bool)\n    if take\n        let moved = a.0\nf((\"first\", \"last\"), true)\nf((\"first\", \"last\"), false)";

    public static TheoryData<string, string, string, string, int[]> Fixtures => new()
    {
        { "Return", "func f(a: (string, string)) -> string => a.0\nwriteLine(f((\"first\", \"last\")))", "first\n", "first=1;last=1", [1, 0] },
        { "Local", "func f(a: (string, string))\n    let moved = a.0\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [0, 1] },
        { "Conditional", Conditional, string.Empty, "first=2;last=2", [0, 1, 1, 0] },
        { "Both", "func f(a: (string, string))\n    let first = a.0\n    let last = a.1\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [1, 0] },
        { "CopyAfter", "func f(a: (string, i32)) -> i32\n    let moved = a.0\n    return a.1\nif f((\"first\", 42)) == 42 => writeLine(\"ok\")", "ok\n", "first=1;ok=1", [0, 1] },
        { "Nested", "func f(a: (string, (string, string))) -> string => a.1.0\nwriteLine(f((\"outer\", (\"first\", \"last\"))))", "first\n", "outer=1;first=1;last=1", [2, 0, 1] },
        { "Aggregate", "func f(a: ((string, string), string)) -> (string, string) => a.0\nlet moved = f(((\"first\", \"last\"), \"outer\"))", string.Empty, "first=1;last=1;outer=1", [2, 1, 0] },
        { "Array", "func f(a: [5 of string]) -> string => a[(0x2)]\nlet a: [5 of string] = [\"a\", \"b\", \"c\", \"d\", \"e\"]\nwriteLine(f(a))", "c\n", "a=1;b=1;c=1;d=1;e=1", [4, 3, 1, 0, 2] },
        { "ArrayNested", "func f(a: (string, [2 of string])) -> [2 of string] => a.1\nlet a: (string, [2 of string]) = (\"outer\", [\"first\", \"last\"])\nlet moved = f(a)", string.Empty, "outer=1;first=1;last=1", [0, 2, 1] },
        { "BorrowAfter", Same + "func f(a: (string, string))\n    let moved = a.0\n    if same(a.1, a.1) => writeLine(\"ok\")\nf((\"first\", \"last\"))", "ok\n", "first=1;last=1;ok=1", [2, 0, 1] },
        { "BorrowBefore", Same + "func f(a: (string, string)) -> string\n    let equal = same(a.0, a.0)\n    return a.0\nwriteLine(f((\"first\", \"last\")))", "first\n", "first=1;last=1", [1, 0] },
        { "SharedSibling", "func inspect(a: ref/string, b: string)\n    if a == a => writeLine(b)\nfunc f(a: (string, string)) => inspect(a.0, a.1)\nf((\"first\", \"last\"))", "last\n", "first=1;last=1", [1, 0] },
        { "DeferMove", "func f(a: (string, string))\n    defer\n        let moved = a.0\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [0, 1] },
        { "DeferClones", "func f(a: (string, string), early: bool)\n    defer\n        let moved = a.0\n    if early => return\nf((\"first\", \"last\"), true)\nf((\"first\", \"last\"), false)", string.Empty, "first=2;last=2", [0, 1, 0, 1] },
        { "Selection", "func f(a: (string, string), take: bool) -> string => if take => a.0 else => a.1\nwriteLine(f((\"first\", \"last\"), true))\nwriteLine(f((\"first\", \"last\"), false))", "first\nlast\n", "first=2;last=2", [1, 0, 0, 1] },
        { "WholeOrPart", "func f(a: (string, string), whole: bool)\n    if whole\n        let moved = a\n    else\n        let moved = a.0\nf((\"first\", \"last\"), true)\nf((\"first\", \"last\"), false)", string.Empty, "first=2;last=2", [1, 0, 0, 1] },
        { "Parameters", "func f(a: (string, string), b: (string, string)) -> string\n    let moved = b.0\n    return a.0\nwriteLine(f((\"a\", \"b\"), (\"c\", \"d\")))", "a\n", "a=1;b=1;c=1;d=1", [2, 3, 1, 0] },
        { "LoopExit", "func f(a: (string, string)) -> string\n    loop\n        return a.0\nwriteLine(f((\"first\", \"last\")))", "first\n", "first=1;last=1", [1, 0] },
        { "Dead", "func f(a: (string, string))\n    return\n    let moved = a.0\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [1, 0] },
        { "Covered", "func f(a: (string, string))\n    match true\n        _ => ()\n        true => (work: do\n            let moved = a.0\n        )\nf((\"first\", \"last\"))", string.Empty, "first=1;last=1", [1, 0] },
        { "Transfer", "func inspect(a: string, b: bool) => ()\nfunc f(a: (string, string)) -> string\n    inspect(a.0, (return \"out\"))\n    return \"bad\"\nwriteLine(f((\"first\", \"last\")))", "out\n", "first=1;last=1;out=1;bad=0", [0, 1, 2] },
        { "ZeroSize", "func f(a: ([0 of string], string)) -> [0 of string] => a.0\nlet a: ([0 of string], string) = ([], \"last\")\nlet moved = f(a)", string.Empty, "last=1", [0] },
        { "ConditionalNested", "func f(a: ((string, string), string), take: bool)\n    if take\n        let moved = a.0.0\n    else\n        let moved = a.0\nf(((\"first\", \"last\"), \"outer\"), true)\nf(((\"first\", \"last\"), \"outer\"), false)", string.Empty, "first=2;last=2;outer=2", [0, 2, 1, 1, 0, 2] },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ParametersTransferOnlyTheSelectedResponsibility(string name, string source, string stdout, string destructions, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("ElementParameterMove" + name, source, stdout);
        StringEmissionTest.WriteAuditedFixture("ElementParameterMove" + name, source, ir, stdout, destructions, order: order);
    }

    [Theory]
    [InlineData("func f(a: (string, string))\n    let moved = a.0\n    let twice = a.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, string))\n    let moved = a.0\n    let whole = a", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, string), take: bool) -> string\n    if take\n        let moved = a.0\n    return a.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, string))\n    loop\n        let moved = a.0\n        continue", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, string))\n    let moved = a.0\n    let equal = a.0 == a.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, string))\n    defer\n        let whole = a\n    let moved = a.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func inspect(a: ref/string, b: string) => ()\nfunc f(a: (string, string)) => inspect(a.0, a.0)", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("func take(a: string) -> string => a\nfunc f(a: (string, string)) -> bool => a.0 == take(a.0)", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("func f(a: [2 of string], i: isize) -> string => a[i]", OwnershipFailure.Unsupported)]
    [InlineData("func f(a: [2 of string]) -> string => a[0 + 0]", OwnershipFailure.Unsupported)]
    [InlineData("let moved = (\"first\", \"last\").0", OwnershipFailure.Unsupported)]
    [InlineData("func f(a: ([0 of string], string))\n    let moved = a.0\n    let twice = a.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: ((string, string), string))\n    let moved = a.0\n    let nested = a.0.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, string))\n    return\n    let moved = a.0\n    let twice = a.0", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(a: (string, [1 of i32])) -> i32\n    return a.1[(work: do\n        let moved = a.0\n        exit to work: 0\n    )]", OwnershipFailure.ComparisonLoanConflict)]
    public void InvalidMovesFailBeforeEmission(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nwriteLine(\"ok\")");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("a.0 = \"new\"")]
    [InlineData("a = (\"new\", 1)")]
    [InlineData("a.1 += 1")]
    [InlineData("++a.1")]
    public void ParametersRemainImmutable(string statement)
    {
        var c = MinimalEmissionTest.Analyze("func f(a: (string, i32))\n    let moved = a.0\n    " + statement + "\nf((\"first\", 0))");
        Assert.False(c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("value")]
    [InlineData("duplicate")]
    public void ConditionalPathFlagsRequireParameterInitialization(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Conditional);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[1];
        var function = module.GetFunction(1);
        Assert.NotEmpty(function.PathFlags);
        Assert.All(function.PathFlags, path =>
        {
            var root = body.GetMovePath(path).Root;
            Assert.Equal(OwnershipPlaceKind.Parameter, body.Places[root].Kind);
            Assert.Contains(function.Instructions, x => x.Opcode == EmissionOpcode.StorePathFlag && x.Place == path && x.Constant == 1 &&
                body.Operations[x.Operation].Kind == OwnershipOperationKind.Produce && body.Operations[x.Operation].Place == root);
        });
        var index = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.StorePathFlag && x.Constant == 1);
        var instruction = function.Instructions[index];
        if (defect == "missing")
        {
            function.Instructions.RemoveAt(index);
        }
        else if (defect == "value")
        {
            function.Instructions[index] = instruction with { Constant = 0 };
        }
        else
        {
            function.Instructions.Insert(index, instruction);
        }

        Assert.False(BodyLowering.ValidatePathFlags(body, function, new int[body.Operations.Count]));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("receipt")]
    [InlineData("source")]
    [InlineData("acquisition")]
    public void InvalidParameterPlansPublishNoIrAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Conditional);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[1];
        var projection = Assert.Single(body.Projections);
        var root = projection.Root;
        var receipt = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Place == root);
        switch (defect)
        {
            case "kind": body.PlaceStorage[root] = body.Places[root] with { Kind = OwnershipPlaceKind.Temporary }; break;
            case "receipt": body.OperationStorage[receipt] = body.Operations[receipt] with { Kind = OwnershipOperationKind.Declare }; break;
            case "source": body.OperationStorage[receipt] = body.Operations[receipt] with { Source = body.Operations[projection.Operation].Source }; break;
            case "acquisition": body.OperationStorage[projection.Output] = body.Operations[projection.Output] with { Acquisition = AcquisitionKind.None }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void ParameterStorageIsUsedWithoutAWholeAggregateTransfer()
    {
        var c = MinimalEmissionTest.Analyze("func f(a: (string, string)) -> string => a.0\nwriteLine(f((\"first\", \"last\")))");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[1];
        var function = module.GetFunction(1);
        var root = Assert.Single(body.Projections).Root;
        Assert.Equal(EmissionOperandKind.Argument, function.SlotAddresses[root].Kind);
        Assert.DoesNotContain(function.Slots, x => x.Place == root);
        Assert.DoesNotContain(function.Instructions, x => x.Opcode == EmissionOpcode.TransferAggregate);
        Assert.Contains(function.Instructions, x => x.Opcode == EmissionOpcode.MoveString && function.Operands[x.OperandStart].Kind == EmissionOperandKind.ElementAddress);
    }

    [Fact]
    public void BoundsAbortDoesNotDestroyTheMovedValueOrParameterRemainder()
    {
        const string Source = "func f(a: (string, string))\n    let moved = a.0\n    let empty: [0 of i32] = []\n    let n = empty[0]\nf((\"first\", \"last\"))";
        const string Error = "Hello.kimi:4:13: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementParameterMoveBounds", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementParameterMoveBounds", Source, ir, string.Empty, "first=0;last=0", 1, Error);
    }

    [Fact]
    public void AbortingDeferPreventsRemainingDestructionAndReturnDelivery()
    {
        const string Source = "func f(a: (string, string)) -> string\n    defer\n        var n = 2147483647\n        n += 1\n    return a.0\nwriteLine(f((\"first\", \"last\")))";
        const string Error = "Hello.kimi:4:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementParameterMoveCleanupAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementParameterMoveCleanupAbort", Source, ir, string.Empty, "first=0;last=0", 1, Error);
    }

    [Fact]
    public void NonterminatingDeferPreventsRemainingDestructionAndReturnDelivery()
        => ScalarEmissionTest.EmitFixture("ElementParameterMoveDivergent", "func f(a: (string, string)) -> string\n    defer => loop => ()\n    return a.0\nwriteLine(f((\"first\", \"last\")))", string.Empty, timeoutMilliseconds: 300);

    [Fact]
    public void WarmParameterMovesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Same + "func f(a: ((string, string), string), take: bool)\n    defer\n        let equal = same(a.1, a.1)\n    if take\n        let moved = a.0.0\n    else\n        let moved = a.0\nf(((\"first\", \"last\"), \"out\"), true)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var valid = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
