// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class GenericCaptureLifecycleTest
{
    private const string Take = "func take<T>(value: T) -> T\n    let action = func [value@move] () -> T => value@move\n    return action@move()\n";
    private const string Item = "struct Item\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop\n        if self.id == 1 => Console.writeLine(\"one\")\n        else => Console.writeLine(\"two\")\n";

    [Theory]
    [InlineData("Option", "let input: Option<string> = .Some(\"owned\")\nlet result = take(input@move)\nmatch result@move\n    .Some(let text) => Console.writeLine(text)\n    .None => $abort(\"option\")", "owned\n")]
    [InlineData("Dictionary", "let input = [1: \"owned\"]\nlet result = take(input@move)\nConsole.writeLine(result[1])", "owned\n")]
    [InlineData("Object", Item + "let result = take(Kimi.Intrinsics.makeObj(Item.init(1)))\nrequire result.id == 1 else => $abort(\"object\")", "one\n")]
    [InlineData("Function", "let text = \"owned\"\nlet input: () -> bool = func [text@move] () => text == \"owned\"\nlet result = take(input@move)\nrequire result() and result() else => $abort(\"function\")", "")]
    [InlineData("Zero", "struct Zero\n    drop => Console.writeLine(\"zero\")\nlet result = take(Zero.init())", "zero\n")]
    public void ConcreteCaptureSubstitutionsPreserveOwningValues(string name, string body, string output)
        => ScalarEmissionTest.EmitFixture("GenericCaptureLifecycle" + name, Take + body, output);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RcCaptureTransfersTheCloneWithoutAnotherAllocation()
    {
        const string Source = Take + Item + "let first = Kimi.Intrinsics.makeRc(Item.init(1))\nlet result = take(Kimi.Intrinsics.clone(first@ref))\nrequire result.id == 1 and first.id == 1 else => $abort(\"rc\")";
        NativeAllocationAudit.WriteFixture("GenericCaptureLifecycleRc", Source, 1, 1, 20, "one\n");
    }

    [Fact]
    public void AbandonedGenericEnvironmentsDestroyCapturesInReverseOrder()
    {
        const string Source = Item + "func discard<T, U>(first: T, second: U)\n    let action = func [first@move, second@move] () => ()\n    action()\nlet first: Option<Item> = .Some(Item.init(1))\nlet second = [Kimi.Intrinsics.makeObj(Item.init(2))]\ndiscard(first@move, second@move)\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureLifecycleReverse", Source, "two\none\ndone\n");
    }

    [Fact]
    public void ZeroSizedOwnedErasureStillRunsItsDestructor()
    {
        const string Source = "struct Zero\n    drop => Console.writeLine(\"zero\")\nfunc keep<T>(value: T) -> () -> i32\n    T is Owned\n    let action = func [value@move] () -> i32 => 7\n    return action@move\nlet result = keep(Zero.init())\nrequire result() == 7 else => $abort(\"zero\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureLifecycleZeroErasure", Source, "zero\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCompoundGenericCapturePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Take + "let input: Option<string> = .Some(\"owned\")\nlet result = take(input@move)\nlet values = take([1: \"value\"])");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
