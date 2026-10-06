// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class NestedGenericCaptureTest
{
    [Fact]
    public void NestedMovesRetainTheEnclosingSubstitution()
    {
        const string Source = "func take<T>(value: T) -> T\n    let outer = func [value@move] () => func [value@move] () -> T => value@move\n    let inner = outer@move()\n    return inner@move()\nrequire take(42) == 42 else => $abort(\"nested\")\nConsole.writeLine(take(\"owned\"))";
        ScalarEmissionTest.EmitFixture("NestedGenericCaptureMove", Source, "owned\n");
    }

    [Fact]
    public void ThreeEmptyEnvironmentsRetainUnstoredTypeArguments()
    {
        const string Source = "func identity<T>(value: T) -> T\n    let first = func [] () => func [] () => func [] (inner: T) -> T => inner@move\n    let second = first()\n    let third = second()\n    return third(value@move)\nrequire identity(true) else => $abort(\"nested\")\nConsole.writeLine(identity(\"empty\"))";
        ScalarEmissionTest.EmitFixture("NestedGenericCaptureEmpty", Source, "empty\n");
    }

    [Fact]
    public void ClosuresInGenericContainerMethodsInheritTheContainer()
    {
        const string Source = "struct Factory<T>\n    public func take(value: T) -> T\n        let action = func [value@move] () -> T => value@move\n        return action@move()\nrequire Factory<i32>.take(7) == 7 else => $abort(\"container\")\nConsole.writeLine(Factory<string>.take(\"owned\"))";
        ScalarEmissionTest.EmitFixture("NestedGenericCaptureContainer", Source, "owned\n");
    }

    [Fact]
    public void NestedOwnedErasureKeepsOneFinalDestructor()
    {
        const string Source = "struct Item\n    public let text: string = \"owned\"\n    drop => Console.writeLine(\"drop\")\nfunc make<T>(value: T) -> () -> i32\n    T is Owned\n    let outer = func [value@move] () => func [value@move] () -> i32 => 9\n    let inner = outer@move()\n    return inner@move\nlet action = make(Item.init())\nrequire action() == 9 and action() == 9 else => $abort(\"nested\")";
        ScalarEmissionTest.EmitFixture("NestedGenericCaptureErasure", Source, "drop\n");
    }

    [Fact]
    public void NestedMovesCannotReuseANonCopyEnvironment()
    {
        const string Source = "func invalid<T>(value: T)\n    let outer = func [value@move] () => func [value@move] () -> T => value@move\n    let first = outer@move()\n    let second = outer@move()\ninvalid(\"owned\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "MovedPlace_Kd");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNestedGenericPlansAllocateNothing()
    {
        const string Source = "func take<T>(value: T) -> T\n    let outer = func [value@move] () => func [value@move] () -> T => value@move\n    let inner = outer@move()\n    return inner@move()\nConsole.writeLine(take(\"owned\"))";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
