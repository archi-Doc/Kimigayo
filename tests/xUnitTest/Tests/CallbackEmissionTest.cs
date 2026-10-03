// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallbackEmissionTest
{
    [Theory]
    [InlineData("Empty", "func apply(f: (i32) -> bool) -> bool => f(6)\nrequire apply(func (v: i32) => v == 6) else => $abort(\"callback\")")]
    [InlineData("Capture", "func apply(f: (i32) -> bool) -> bool => f(6) and not f(5)\nlet target: i32 = 6\nrequire apply(func [target] (v: i32) => v == target) else => $abort(\"capture\")")]
    [InlineData("Snapshot", "var target: i32 = 6\nlet f: (i32) -> bool = func [target] (v: i32) => v == target\ntarget = 7\nrequire f(6) and not f(7) else => $abort(\"snapshot\")")]
    [InlineData("Return", "func make(target: i32) -> (i32) -> bool => func [target] (v: i32) => v == target\nlet f = make(6)\nlet g = f@move\nrequire g(6) else => $abort(\"return\")")]
    [InlineData("Implicit", "let target: i32 = 6\nlet f: (i32) -> bool = func (v: i32) => v == target\nrequire f(6) else => $abort(\"implicit\")")]
    [InlineData("Aligned", "let small: i8 = 3\nlet target: i32 = 6\nlet flag = true\nlet f: (i32) -> bool = func [small, target, flag] (v: i32) => flag and small == 3 and v == target\nrequire f(6) else => $abort(\"aligned\")")]
    [InlineData("NestedCall", "let f: (i32) -> i32 = func (v: i32) => v + 1\nrequire f(f(4)) == 6 else => $abort(\"nested\")")]
    [InlineData("Repeated", "let f: (i32) -> i32 = func (v: i32) => v + 1\nvar n = 0\nwhile n < 4\n    n = f(n)\nrequire n == 4 else => $abort(\"repeat\")")]
    [InlineData("Choice", "var f: (i32) -> bool = if true => func (v: i32) => v == 6 else => func (v: i32) => false\nf = func (v: i32) => v == 7\nrequire f(7) else => $abort(\"replace\")")]
    [InlineData("Temporary", "func make() -> () -> i32 => func () => 6\nrequire make()() == 6 else => $abort(\"temporary\")")]
    [InlineData("Unused", "let value: i32 = 6\nlet f: () -> i32 = func [value] () => 7\nrequire f() == 7 else => $abort(\"unused\")")]
    [InlineData("Block", "let n: i32 = 6\nlet f: () -> i32 = func [n] ()\n    return n\nrequire f() == 6 else => $abort(\"block\")")]
    [InlineData("Unit", "let n = ()\nlet f: () -> () = func [n] () => n\nf()\nf()")]
    // SPEC 7.6.4: a stored concrete Closure converts to the parameter's common Function Type at an argument, as at an
    // initialization; it was NoApplicableOverload_Kd unless the argument was a literal.
    [InlineData("StoredArgument", "func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nlet g = func [] (value: i32) -> i32 => value * 2\nrequire apply(g, 3) == 6 and apply(g, 4) == 8 and g(5) == 10 else => $abort(\"stored\")")]
    [InlineData("MovedArgument", "func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nlet k: i32 = 3\nlet g = func [k] (value: i32) -> i32 => value * k\nrequire apply(g@move, 3) == 9 else => $abort(\"moved\")")]
    // SPEC 10.5, 7.6.4: an omitted parameter Type takes the fixed expected input (the SPEC's own `makeAdder` example was a
    // TypeMismatch_Kd), at a return, an initialization, an argument, a generic argument and between overloads by arity.
    [InlineData("OmittedReturn", "func makeAdder(offset: i32) -> (i32) -> i32\n    return func [offset] (value) => value + offset\nlet f = makeAdder(10)\nrequire f(5) == 15 else => $abort(\"return\")")]
    [InlineData("OmittedInitializer", "let f: (i32) -> i32 = func [] (value) => value + 1\nrequire f(5) == 6 else => $abort(\"initializer\")")]
    [InlineData("OmittedArgument", "func apply(f: (i32) -> bool) -> bool => f(1)\nlet k: i32 = 1\nrequire apply(func [k] (v) => v == k) else => $abort(\"argument\")")]
    [InlineData("OmittedGeneric", "func apply<T>(f: (T) -> T, v: T) -> T\n    T is Copy\n    return f(v)\nlet two: i32 = 2\nrequire apply(func [] (value) => value + 1, two) == 3 else => $abort(\"generic\")")]
    [InlineData("OmittedArity", "func apply(f: (i32) -> i32) -> i32 => f(1)\nfunc apply(f: (i32, i32) -> i32) -> i32 => f(1, 2)\nrequire apply(func [] (a) => a + 1) == 2 and apply(func [] (a, b) => a + b) == 3 else => $abort(\"arity\")")]
    // A closure body that returns through a result slot took (ret, environment, ...) while every common call and erasure
    // adapter passes (environment, ret, ...): calling such a common value wrote the result through the environment word
    // and crashed (exit 139) or returned garbage.
    [InlineData("FunctionResult", "func outer(n: i32) -> () -> () -> i32\n    return func [n] () -> () -> i32 => func [n] () -> i32 => n\nlet f = outer(4)\nlet g = f()\nrequire g() == 4 else => $abort(\"nested\")")]
    [InlineData("StringResult", "let f: () -> string = func [] () -> string => \"abc\"\nrequire f() == \"abc\" else => $abort(\"string\")")]
    [InlineData("ArrayResult", "let n: i32 = 7\nlet f: () -> Array<i32> = func [n] () -> Array<i32> => [n, n, n]\nlet a = f()\nrequire a.length == 3 and a[2] == 7 else => $abort(\"array\")")]
    [InlineData("TupleResult", "let f: (i32) -> (i32, i64, i32) = func [] (v) => (v, 2@i64, 3)\nlet t = f(1)\nrequire t.0 == 1 and t.1 == 2 and t.2 == 3 else => $abort(\"tuple\")")]
    [InlineData("DirectStringResult", "let n: i32 = 7\nlet f = func [n] () -> Array<i32> => [n, n]\nlet a = f()\nrequire a.length == 2 and a[1] == 7 else => $abort(\"direct\")")]
    // SPEC 7.6.4: a Function Item converts to a fixed common Function Type without an environment; the erasure adapter calls
    // the function with its own ABI. Ownership analysis read the item as a missing local, and an argument was unsupported.
    [InlineData("FunctionItemInitializer", "func inc(value: i32) -> i32 => value + 1\nlet f: (i32) -> i32 = inc\nrequire f(2) == 3 else => $abort(\"initializer\")")]
    [InlineData("FunctionItemArgument", "func inc(value: i32) -> i32 => value + 1\nfunc apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nrequire apply(inc, 2) == 3 else => $abort(\"argument\")")]
    [InlineData("FunctionItemOverload", "func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nfunc inc(v: i32) -> i32 => v + 1\nfunc inc(v: i64) -> i64 => v + 2\nrequire apply(inc, 2) == 3 else => $abort(\"overload\")")]
    [InlineData("FunctionItemReturn", "func make() -> (i32) -> string\n    return describe\nfunc describe(v: i32) -> string => if v > 0 => \"pos\" else => \"neg\"\nlet f = make()\nrequire f(3) == \"pos\" and f(-1) == \"neg\" else => $abort(\"return\")")]
    // Common Function values as container elements and payloads: the library's owning iteration reads them through a raw
    // pointer and a match moves them out of Option; both were generation failures after the check passed.
    [InlineData("FunctionArrayIteration", "let n: i32 = 3\nvar fs: Array<(i32) -> i32> = []\nfs.append(func [n] (v) => v + n)\nfs.append(func [] (v) => v * 10)\nvar acc = 1\nfor f in fs@move\n    acc = f(acc)\nrequire acc == 40 else => $abort(\"iteration\")")]
    [InlineData("FunctionOption", "func choose(flag: bool) -> Option<(i32) -> i32>\n    if flag => return Option.Some(func [] (v) => v + 5)\n    return Option.None\nmatch choose(true)@move\n    .Some(let f) => require f(1) == 6 else => $abort(\"option\")\n    .None => $abort(\"none\")")]
    [InlineData("FunctionArrayRemove", "var fs: Array<(i32) -> i32> = []\nfs.append(func [] (v) => v + 1)\nlet g = fs.remove(0)\nrequire g(1) == 2 and fs.length == 0 else => $abort(\"remove\")")]
    // A Dictionary of common Function values: the library's entry lookups return `Option<(ref/K, ref/V)>`, whose `ref/V` had no
    // representation for a Function V, so building `tryInsert` failed after the check passed.
    [InlineData("FunctionDictionary", "var ops: Dictionary<i32, (i32) -> i32> = [:]\nlet a = ops.tryInsert(1, func [] (v) => v + 1)\nlet b = ops.tryInsert(2, func [] (v) => v * 2)\nrequire ops.length == 2 else => $abort(\"length\")\nmatch ops.remove(2)\n    .Some((let k, let f)) => require f(5) == 10 else => $abort(\"f\")\n    .None => $abort(\"none\")")]
    [InlineData("FunctionReferenceArgument", "func twice(f: ref/((i32) -> i32), v: i32) -> i32 => f(f(v))\nlet k = 1\nlet g: (i32) -> i32 = func [k] (v) => v + k\nrequire twice(g@ref, 0) == 2 else => $abort(\"ref\")")]
    [InlineData("FunctionReferenceLocal", "let k = 5\nlet g: (i32) -> i32 = func [k] (v) => v + k\nlet r = g@ref\nrequire r(3) == 8 and r(4) == 9 else => $abort(\"local\")")]
    [InlineData("FunctionUniqueReference", "func apply(f: uniq/((i32) -> i32), v: i32) -> i32 => f(v)\nvar g: (i32) -> i32 = func [] (v) => v * 2\nlet r = g@uniq\nrequire r(3) == 6 else => $abort(\"local\")\nrequire apply(g@uniq, 4) == 8 else => $abort(\"argument\")")]
    [InlineData("FunctionPartReference", "struct H\n    public var f: (i32) -> i32\n    public init(f: (i32) -> i32) => self.f = f@move\nlet h = H.init(func [] (v) => v * 3)\nlet r = h.f@ref\nlet fs: Array<(i32) -> i32> = [func [] (v) => v + 1, func [] (v) => v + 2]\nlet e = fs[1]@ref\nrequire r(2) == 6 and e(1) == 3 else => $abort(\"part\")")]
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
    [InlineData("let f: () -> i32 = func () => 6\nlet g = f@move\nf()")]
    [InlineData("let f: () -> i32 = func () => 6\nlet g = f")]
    [InlineData("let n: i32\nlet f: () -> i32 = func [n] () => 6")]
    [InlineData("func eat(f: (i32) -> bool) -> i32 => 0\nlet f: (i32) -> bool = func (v: i32) => true\nf(eat(f@move))")]
    [InlineData("let n: i32 = 6\nlet f: () -> i32 = func [n] ()\n    func nested() -> i32 => n\n    return nested()")]
    [InlineData("func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nlet h = func [] (value: i64) -> i64 => value\nlet x = apply(h, 3)")]
    [InlineData("let f = func [] (value) => value + 1")]
    [InlineData("func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nfunc wide(v: i64) -> i64 => v\nlet r = apply(wide, 2)")]
    [InlineData("func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nfunc id<T>(x: T) -> T => x@move\nlet r = apply(id, 2)")]
    [InlineData("func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nunsafe func raw(v: i32) -> i32 => v\nlet r = apply(raw, 2)")]
    [InlineData("func apply(f: (string) -> i32) -> i32 => f(\"ab\")\nlet n = apply(func [] (s) => s + 1)")]
    [InlineData("func apply(f: (i32) -> i32, v: i32) -> i32 => f(v)\nvar k: i32 = 2\nlet g = func [k@ref] (value: i32) -> i32 => value + k\nlet x = apply(g, 3)")]
    [InlineData("let g: (i32) -> i32 = func [] (v) => v * 2\nlet r = g@ref\nlet h = g@move\nlet x = r(3)")]
    public void RejectsInvalidClosures(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Trait("Purpose", "Allocation")]
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
