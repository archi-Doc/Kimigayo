// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class GenericClosureErasureTest
{
    private const string Keep = "func keep<T>(value: T) -> () -> i32\n    T is Owned\n    let action = func [value@move] () -> i32 => 7\n    return action@move\n";

    [Fact]
    public void GenericErasureUsesItsClosedEnvironmentAndSignature()
    {
        const string Source = "func make<T>(value: T) -> () -> T\n    T is Copy and Owned\n    let action = func [value] () -> T => value\n    return action\nlet integer = make(7)\nlet boolean = make(true)\nrequire integer() == 7 and boolean() else => $abort(\"erasure\")";
        ScalarEmissionTest.EmitFixture("GenericClosureErasureSignature", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Integer", "let action = keep(9)\nrequire action() == 7 and action() == 7 else => $abort(\"inline\")", 0, 0, "")]
    [InlineData("String", "let action = keep(\"owned\")\nrequire action() == 7 and action() == 7 else => $abort(\"heap\")", 1, 24, "")]
    [InlineData("Destructor", "struct Item\n    public let text: string = \"owned\"\n    drop => Console.writeLine(\"drop\")\nlet action = keep(Item.init())\nrequire action() == 7 else => $abort(\"heap\")", 1, 24, "drop\n")]
    public void ErasedGenericEnvironmentsRetainOrdinaryAllocationAndDestruction(string name, string body, int allocations, int bytes, string output)
        => NativeAllocationAudit.WriteFixture("GenericClosureErasure" + name, Keep + body, allocations, allocations, bytes, output);

    [Fact]
    public void EmptyGenericEnvironmentErasesItsSubstitutedInputAndResult()
    {
        const string Source = "func make<T>(value: T) -> (T) -> T\n    let action = func [] (inner: T) -> T => inner@move\n    return action\nlet integer = make(1)\nlet text = make(\"first\")\nrequire integer(7) == 7 else => $abort(\"input\")\nConsole.writeLine(text(\"second\"))";
        ScalarEmissionTest.EmitFixture("GenericClosureErasureEmpty", Source, "second\n");
    }

    [Fact]
    public void UnknownOwnedCannotBeSuppliedByAnInstantiation()
    {
        const string Source = "func invalid<T>(value: T) -> () -> i32\n    let action = func [value@move] () -> i32 => 7\n    return action@move\n_ = invalid(42)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "TypeMismatch_Kd");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmGenericErasurePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let integer = keep(1)\nlet text = keep(\"owned\")\nrequire integer() == text() else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
