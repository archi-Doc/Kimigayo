// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallbackEmissionTest
{
    [Theory]
    [InlineData("Empty", "func apply(f?: (i32) -> bool) -> bool => f(6)\nrequire apply(func (v: i32) => v == 6) else => $abort(\"callback\")")]
    [InlineData("Capture", "func apply(f?: (i32) -> bool) -> bool => f(6) and not f(5)\nlet target: i32 = 6\nrequire apply(func [target] (v: i32) => v == target) else => $abort(\"capture\")")]
    [InlineData("Snapshot", "var target: i32 = 6\nlet f: (i32) -> bool = func [target] (v: i32) => v == target\ntarget = 7\nrequire f(6) and not f(7) else => $abort(\"snapshot\")")]
    [InlineData("Return", "func make(target?: i32) -> (i32) -> bool => func [target] (v: i32) => v == target\nlet f = make(6)\nlet g = f\nrequire g(6) else => $abort(\"return\")")]
    [InlineData("Implicit", "let target: i32 = 6\nlet f: (i32) -> bool = func (v: i32) => v == target\nrequire f(6) else => $abort(\"implicit\")")]
    [InlineData("Aligned", "let small: i8 = 3\nlet target: i32 = 6\nlet flag = true\nlet f: (i32) -> bool = func [small, target, flag] (v: i32) => flag and small == 3 and v == target\nrequire f(6) else => $abort(\"aligned\")")]
    [InlineData("NestedCall", "let f: (i32) -> i32 = func (v: i32) => v + 1\nrequire f(f(4)) == 6 else => $abort(\"nested\")")]
    [InlineData("Repeated", "let f: (i32) -> i32 = func (v: i32) => v + 1\nvar n = 0\nwhile n < 4\n    n = f(n)\nrequire n == 4 else => $abort(\"repeat\")")]
    [InlineData("Choice", "var f: (i32) -> bool = if true => func (v: i32) => v == 6 else => func (v: i32) => false\nf = func (v: i32) => v == 7\nrequire f(7) else => $abort(\"replace\")")]
    [InlineData("Temporary", "func make() -> () -> i32 => func () => 6\nrequire make()() == 6 else => $abort(\"temporary\")")]
    [InlineData("Unused", "let value: i32 = 6\nlet f: () -> i32 = func [value] () => 7\nrequire f() == 7 else => $abort(\"unused\")")]
    [InlineData("Block", "let n: i32 = 6\nlet f: () -> i32 = func [n] ()\n    return n\nrequire f() == 6 else => $abort(\"block\")")]
    [InlineData("Unit", "let n = ()\nlet f: () -> () = func [n] () => n\nf()\nf()")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("Callback" + name, source, string.Empty);

    [Fact]
    public void ReceiverAndArgumentsExecuteOnceInOrder()
        => ScalarEmissionTest.EmitFixture(
            "CallbackEvaluationOrder",
            "func make() -> (i32) -> bool\n    Console.writeLine(\"receiver\")\n    return func (n: i32)\n        Console.writeLine(\"body\")\n        return n == 7\nfunc argument() -> i32\n    Console.writeLine(\"argument\")\n    return 7\nrequire make()(argument()) else => $abort(\"order\")",
            "receiver\nargument\nbody\n");

    [Fact]
    public void CreationDoesNotExecuteNeverBody()
        => ScalarEmissionTest.EmitFixture(
            "CallbackNeverCreation",
            "let f: () -> Never = func () => $abort(\"not called\")\nConsole.writeLine(\"created\")",
            "created\n");

    [Theory]
    [InlineData("let a: u128 = 1\nlet f: () -> u128 = func [a] () => a")]
    [InlineData("let a: i64 = 1\nlet b: i64 = 2\nlet f: () -> i64 = func [a, b] () => a")]
    public void LargerEnvironmentsRemainExplicitlyUnsupported(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out var failure));
        Assert.Contains("inline", failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("let target = 6\nlet f: () -> i32 = func [] () => target")]
    [InlineData("let target = 6\nlet f: () -> i32 = func [target, target] () => target")]
    [InlineData("let target = 6\nlet f: (i32) -> i32 = func [target] (target: i32) => target")]
    [InlineData("var target = 6\nlet f: () -> () = func [target] () => target = 7")]
    [InlineData("let f: (i32) -> bool = func (v: bool) => v")]
    [InlineData("let f: () -> i32 = func () => true")]
    [InlineData("let f: () -> i32 = func () => 6\nlet g = f\nf()")]
    [InlineData("let n: i32\nlet f: () -> i32 = func [n] () => 6")]
    [InlineData("func eat(f?: (i32) -> bool) -> i32 => 0\nlet f: (i32) -> bool = func (v: i32) => true\nf(eat(f))")]
    [InlineData("let n: i32 = 6\nlet f: () -> i32 = func [n] ()\n    func nested() -> i32 => n\n    return nested()")]
    public void RejectsInvalidClosures(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void ReloadAndReanalysisRetainCaptureIdentity()
    {
        const string Source = "let n: i32 = 6\nlet f: () -> i32 = func [n] () => n\nf()";
        var c = MinimalEmissionTest.Analyze(Source);
        using var first = new StringWriter();
        Assert.True(c.Emission.WriteIr(first, out var failure), MinimalEmissionTest.Describe(c, failure));
        for (var i = 0; i < 20; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var second = new StringWriter();
        Assert.True(restored.Emission.WriteIr(second, out failure), MinimalEmissionTest.Describe(restored, failure));
        Assert.Equal(first.ToString(), second.ToString());
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("signature")]
    [InlineData("argument")]
    [InlineData("loan")]
    public void CorruptCallbackPlansAreRejected(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let n: i32 = 6\nlet f: (i32) -> bool = func [n] (v: i32) => v == n\nf(6)");
        Assert.True(c.Emission.Validate(out var failure), failure);
        var body = c.Ownership.Bodies.Single(x => x.Values.Any(v => v.Kind == OwnershipValueKind.Closure));
        var create = body.OperationStorage.FindIndex(x => x.Source is FunctionKoto { BoundClosure: not null } && x.Kind == OwnershipOperationKind.Produce);
        var closure = ((FunctionKoto)body.Operations[create].Source).BoundClosure!;
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call && x.Source is InvocationKoto { BoundValueCall: not null });
        var invocation = (InvocationKoto)body.Operations[call].Source;
        switch (defect)
        {
            case "capture":
                body.Values[create] = body.Values[create] with { Count = 0 };
                break;
            case "signature":
                closure.Signature = BoundType.Unit;
                break;
            case "argument":
                var plan = invocation.BoundValueCall!;
                var arguments = plan.Arguments.ToArray();
                arguments[0] = arguments[0] with { ParameterType = BoundType.Unit };
                plan.Set(plan.Receiver, plan.Signature, arguments);
                break;
            case "loan":
                var loan = body.ComparisonLoans[0];
                body.ComparisonLoans[0] = loan with { Place = 0 };
                break;
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out failure), failure);
    }
}
