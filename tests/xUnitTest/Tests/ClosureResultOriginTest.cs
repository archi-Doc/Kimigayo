// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ClosureResultOriginTest
{
    private const string Captured = "let n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\n";

    [Theory]
    [InlineData("Array", Captured + "let first = f()\nlet second = f()\nrequire first == 7 and second == 7 else => $abort(\"result\")")]
    [InlineData("BorrowedClosure", Captured + "let borrowed = f@ref\nlet result = borrowed()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Direct", "let n = 7\nlet view = n@ref\nlet f = func [view] () => view\nlet result = f()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Slot", "let n = 7\nlet f = func [n@ref] () => n\nlet result = f()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Tuple", "let n = 7\nlet view = n@ref\nlet f = func [view] () => (view, 9)\nlet result = f()\nrequire result.0 == 7 and result.1 == 9 else => $abort(\"result\")")]
    [InlineData("Option", "let n = 7\nlet view = n@ref\nlet f = func [view] () => Option.Some(view)\nlet result = f()\nmatch result\n    .Some(let item)\n        require item == 7 else => $abort(\"result\")\n    .None => $abort(\"none\")")]
    [InlineData("Input", "func read(n: ref/i32) -> i32\n    let values: [1 of ref/i32] = [n]\n    let f = func [values] () => values[0]\n    let result = f()\n    return result\nlet n = 7\nrequire read(n@ref) == 7 else => $abort(\"result\")")]
    [InlineData("FreshArgument", "var n = 7\nlet view = n@ref\nlet f = func [view] (other: ref/i32) => view\nvar other = 9\nlet result = f(other@ref)\nother = 11\nrequire result == 7 and other == 11 else => $abort(\"result\")")]
    public void ConcreteResultsKeepFixedExternalOrigins(string name, string source)
        => ScalarEmissionTest.EmitFixture("ClosureResultOrigin" + name, source, string.Empty);

    [Theory]
    [InlineData("var n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\nlet result = f()\nn = 9\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("var n = 7\nlet f = func [n@ref] () => n\nlet result = f()\nn = 9\nrequire result == 7 else => $abort(\"result\")")]
    public void TheReturnedViewProtectsTheOwnerAfterTheLastClosureUse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReturningAnExternalReferenceDoesNotRetainTheEnvironment()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginCleanup", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values, owner@move] () => values[0]\nlet result = f@move()\nrequire result == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void StoredObjectResultsKeepHeaderIdentityAndTheOwner()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginObject", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet values: [1 of objref/Item] = [owner@objref]\nlet f = func [values] () => values[0]\nlet result = f()\nrequire result.id == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    [Theory]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nf()")]
    [InlineData("var n = 7\nlet view = n@uniq\nvar f = func [view] () => view\nlet r = f()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet f = func [view] () => view@follow@ref\nf()")]
    [InlineData("func check(shared: ref/i32 during source, exclusive: uniq/i32 during source)\n    let f = func [shared, exclusive] () => exclusive@follow@ref\n    f()")]
    public void ReceiverAndPerCallDependentResultsRemainExplicitlyUnsupported(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void CommonFunctionErasureCannotHideAnExternalOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let erased: () -> ref/i32 = f\nerased()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AReturnedViewCannotOutliveItsExternalOwner()
    {
        var c = MinimalEmissionTest.Analyze("func bad() -> ref/i32 during static\n    let n = 7\n    let values: [1 of ref/i32] = [n@ref]\n    let f = func [values] () => values[0]\n    return f()\nbad()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Failure == BindingFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAnalysisAndEmissionReuseFixedContracts()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let result = f()\nrequire result == 7 else => $abort(\"result\")");
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
