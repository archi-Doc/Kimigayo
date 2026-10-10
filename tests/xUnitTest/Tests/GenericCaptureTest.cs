// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericCaptureTest
{
    private const string Take = "func take<T>(value: T) -> T\n    let action = func [value@move] () -> T => value@move\n    return action@move()\n";

    [Fact]
    public void GenericOwnedCaptureUsesItsConcreteStorage()
    {
        const string Source = Take + "require take(42) == 42 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureOwned", Source, string.Empty);
    }

    [Theory]
    [InlineData("String", "Console.writeLine(take(\"owned\"))", "owned\n")]
    [InlineData("Tuple", "let pair = take((true, 42@i64))\nrequire pair.0 and pair.1 == 42 else => $abort(\"tuple\")", "")]
    [InlineData("Unit", "take(())", "")]
    [InlineData("Array", "var values: Array<i32> = [2, 3]\nlet result = take(values@move)\nrequire result[1] == 3 else => $abort(\"array\")", "")]
    [InlineData("Destructor", "struct Item\n    public let text: string = \"payload\"\n    drop => Console.writeLine(\"drop\")\nlet item = take(Item.init())\nConsole.writeLine(item.text)", "payload\ndrop\n")]
    public void OwnedCapturesShareOrdinaryStorageAndCleanup(string name, string body, string output)
        => ScalarEmissionTest.EmitFixture("GenericCapture" + name, Take + body, output);

    [Fact]
    public void EmptyEnvironmentsKeepTheirEnclosingSubstitution()
    {
        const string Source = "func identity<T>(value: T) -> T\n    let action = func [] (inner: T) -> T => inner@move\n    return action(value@move)\nrequire identity(42) == 42 else => $abort(\"integer\")\nConsole.writeLine(identity(\"string\"))\nrequire identity(true) else => $abort(\"boolean\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureEmptyContexts", Source, "string\n");
    }

    [Fact]
    public void EmptyEnvironmentIdentityIncludesUnstoredTypeArguments()
    {
        const string Source = "func identity<T>(value: T) -> T\n    let action = func [] (inner: T) -> T => inner@move\n    return action(value@move)\n_ = identity(1)\n_ = identity(2)\n_ = identity(true)";
        var c = MinimalEmissionTest.Analyze(Source);
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.CallOf()?.Target.Name == "identity").Select(x => x.CallOf()!).ToArray();
        var first = c.Binding.InstantiateStorageType(closure, calls[0])!;
        Assert.Same(first, c.Binding.InstantiateStorageType(closure, calls[1]));
        var other = c.Binding.InstantiateStorageType(closure, calls[2])!;
        Assert.NotSame(first, other);
        Assert.False(Binding.FitsType(first, other));
        Assert.False(Binding.FitsType(other, first));
        Assert.Empty(first.Components);
    }

    [Fact]
    public void CopyAndExclusiveCapturesKeepTheirDefinitionProof()
    {
        const string Source = "func replaceTwice<T>(first: T, second: T) -> T\n    T is Copy\n    var action = func [var first] (next: T) -> T\n        let previous = first\n        first = next\n        return previous\n    _ = action(second)\n    return action(first)\nrequire replaceTwice(1, 9) == 9 else => $abort(\"copy\")\nrequire replaceTwice(false, true) else => $abort(\"boolean\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureExclusive", Source, string.Empty);
    }

    [Fact]
    public void GenericEnvironmentsPassToCallableParameters()
    {
        const string Source = "func shared<F>(action: ref/F) -> i32\n    F is Callable<() -> i32>\n    return action()\nfunc inspect<T, F>(value: T, visitor: ref/F) -> i32\n    F is Callable<(ref/T) -> i32>\n    let action = func [value@move, visitor] () -> i32 => visitor(value@ref)\n    return shared(action@ref) + shared(action@ref)\nfunc visit(value: ref/i32) -> i32 => value@follow\nrequire inspect(7, visit) == 14 else => $abort(\"callable\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureCallable", Source, string.Empty);
    }

    [Theory]
    [InlineData("[value]")]
    [InlineData("")]
    public void UnknownCopyCannotBecomeAValidCaptureAtInstantiation(string capture)
    {
        var source = "func invalid<T>(value: T)\n    let action = func " + capture + " () => _ = value\n    action()\ninvalid(42)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "TransferRequired_Kd");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmGenericCapturePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Take + "require take(42) == 42 else => $abort(\"warm\")\nConsole.writeLine(take(\"string\"))");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
