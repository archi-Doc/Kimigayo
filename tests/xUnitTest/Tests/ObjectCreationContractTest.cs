// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ObjectCreationContractTest
{
    private const string Payload = "struct Payload\n    public let number: i32\n    public init(number: i32)\n        Console.writeLine(\"init\")\n        self.number = number\n    drop => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("obj", false)]
    [InlineData("rc", false)]
    [InlineData("arc", false)]
    [InlineData("obj", true)]
    [InlineData("rc", true)]
    [InlineData("arc", true)]
    public void GetterAcquisitionSurvivesReturnStorageAndCapture(string mode, bool factory)
    {
        var source = Payload + "struct Source\n    public computed value: Payload\n        get(self: ref/Self) -> Payload\n            Console.writeLine(\"get\")\n            return Payload.init(7)\n" +
            $"func make() -> {mode}/Payload\n    let source = Source.init()\n    return {Create("source.value", mode, factory)}\n" +
            "let result = make()\nlet stored = (result@move, 9)\nlet read = func [stored@move] () -> i32 => stored.0.number + stored.1\n" +
            "require read() == 16 and read() == 16 else => $abort(\"payload\")\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture($"ObjectCreationGetter{mode}{factory}", source, 1, 1, 20, "get\ninit\ndone\ndrop\n");
    }

    [Theory]
    [InlineData("obj", false)]
    [InlineData("rc", false)]
    [InlineData("arc", false)]
    [InlineData("obj", true)]
    [InlineData("rc", true)]
    [InlineData("arc", true)]
    public void IndexOperandIsEvaluatedOnce(string mode, bool factory)
    {
        var source = "func index() -> i32\n    Console.writeLine(\"index\")\n    return 1\nlet items: [2 of i32] = [3, 7]\n" +
            $"let value = {Create("items[index()]", mode, factory)}\nrequire value@follow == 7 else => $abort(\"index\")";
        NativeAllocationAudit.WriteFixture($"ObjectCreationIndex{mode}{factory}", source, 1, 1, 20, "index\n");
    }

    [Theory]
    [InlineData("obj", false)]
    [InlineData("rc", false)]
    [InlineData("arc", false)]
    [InlineData("obj", true)]
    [InlineData("rc", true)]
    [InlineData("arc", true)]
    public void LaterArgumentExitDestroysTheAcquiredObject(string mode, bool factory)
    {
        var source = Payload + $"func take(value: {mode}/Payload, rest: ()) => $abort(\"unreachable\")\n" +
            $"func run()\n    take({Create("Payload.init(7)", mode, factory)}, (return))\nrun()\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture($"ObjectCreationAbandoned{mode}{factory}", source, 1, 1, 20, "init\ndrop\ndone\n");
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void FactoriesShareAggregateAbiThroughItemsCallableAndErasure(string mode)
    {
        var factory = Factory(mode);
        var source = Payload + $"func apply<F>(factory: ref/F, value: Payload) -> {mode}/Payload\n    F is Callable<(Payload) -> {mode}/Payload>\n    return factory(value@move)\n" +
            $"let item = {factory}<Payload>\nlet erased: (Payload) -> {mode}/Payload = item\n" +
            "let a = item(Payload.init(1))\nlet b = apply(item, Payload.init(2))\nlet c = erased(Payload.init(3))\n" +
            "require a.number == 1 and b.number == 2 and c.number == 3 else => $abort(\"abi\")\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture($"ObjectCreationFactoryAbi{mode}", source, 3, 3, 60, "init\ninit\ninit\ndone\ndrop\ndrop\ndrop\n");
    }

    [Theory]
    [InlineData("obj", "Adaptation")]
    [InlineData("rc", "Adaptation")]
    [InlineData("arc", "Adaptation")]
    [InlineData("obj", "Direct")]
    [InlineData("rc", "Direct")]
    [InlineData("arc", "Direct")]
    [InlineData("obj", "Item")]
    [InlineData("rc", "Item")]
    [InlineData("arc", "Item")]
    [InlineData("obj", "Erased")]
    [InlineData("rc", "Erased")]
    [InlineData("arc", "Erased")]
    public void AllocationFailureNamesTheWrittenInvocation(string mode, string path)
    {
        var declaration = path switch
        {
            "Item" => $"let factory = {Factory(mode)}<i32>\n",
            "Erased" => $"let factory: (i32) -> {mode}/i32 = {Factory(mode)}\n",
            _ => string.Empty,
        };
        var expression = path is "Item" or "Erased" ? "factory(7)" : Create("7", mode, path == "Direct");
        var source = declaration + $"let first = {expression}\nlet second = {expression}";
        var line = declaration.Length == 0 ? 2 : 3;
        // The second allocation fails after the first object is initialized. Abort promises no unwinding.
        NativeAllocationAudit.WriteFixture($"ObjectCreationAllocationFailure{mode}{path}", source, 2, 0, 40, failAllocation: 2, exit: 1, stderr: $"Hello.kimi:{line}:14: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void NestedDefaultsKeepTheCreationAbortLocation(string mode)
    {
        var source = $"func inner(value: {mode}/i32 = 7@{mode}) -> {mode}/i32 => value@move\nfunc outer(value: {mode}/i32 = inner()) -> {mode}/i32 => value@move\nlet value = outer()";
        var column = source.IndexOf("7@", StringComparison.Ordinal) + 1;
        NativeAllocationAudit.WriteFixture($"ObjectCreationDefaultFailure{mode}", source, 1, 0, 20, failAllocation: 1, exit: 1, stderr: $"Hello.kimi:1:{column}: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MixedCreationEntrypointsReuseTheirPlans()
    {
        const string Source = "let item = Kimi.Intrinsics.makeArc<i32>\nlet erased: (i32) -> arc/i32 = item\nlet a = item(1)\nlet b = erased(2)\nlet c = 3@obj\nlet d = Kimi.Intrinsics.makeRc(4)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Create(string input, string mode, bool factory)
        => factory ? $"{Factory(mode)}({input})" : $"{input}@{mode}";

    private static string Factory(string mode)
        => "Kimi.Intrinsics.make" + (mode == "obj" ? "Obj" : mode == "rc" ? "Rc" : "Arc");
}
