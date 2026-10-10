// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ValueCallBindingTest
{
    [Fact]
    public void EditingAValueCallContractRevokesTheRetainedPlan()
    {
        const string Source = "func apply(f: (i32) -> bool) -> bool => f(42)";
        var c = MinimalEmissionTest.Analyze(Source);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.NotNull(call.ValueCallOf());
        var function = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.Name == "apply");
        var donor = MinimalEmissionTest.Analyze(Source.Replace("i32", "i64", StringComparison.Ordinal));
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.Name == "apply").Parameters[0];
        Assert.True(KotoHelper.Replace(function, function.Parameters[0].Type, changed.Type));
        Assert.Null(call.ValueCallOf());
        Assert.True(c.Bind().IsComplete);
        Assert.NotNull(call.ValueCallOf());
    }

    [Theory]
    [InlineData("ElementBorrow", "let values: Array<i32> = [42]\nlet f = func (x: ref/i32) -> i32 => x\nlet result = f(values[0])")]
    [InlineData("ReferentRead", "let n: i32 = 42\nlet r = n@ref\nlet f = func (x: i32) -> i32 => x\nlet result = f(r)")]
    [InlineData("Reborrow", "var n: i32 = 42\nlet r = n@uniq\nlet f = func (x: ref/i32) -> i32 => x\nlet result = f(r)")]
    public void ExpectedAdaptationsKeepTheOriginalCallableArgument(string name, string source)
        => ScalarEmissionTest.EmitFixture("ValueCallAdaptation" + name, source + "\nrequire result == 42 else => $abort(\"argument\")", string.Empty);

    [Theory]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(42)")]
    [InlineData("func apply(f: () -> i32) -> i32 => f()")]
    [InlineData("func apply(f: (()) -> bool) -> bool => f(())")]
    [InlineData("func apply(f: ((i32, bool)) -> bool) -> bool => f((42, true))")]
    [InlineData("func apply(f: (i64, bool) -> i32) -> i32 => (f)(42, true)")]
    [InlineData("func apply<T>(f: (T) -> bool, value: T) -> bool => f(value@move)")]
    [InlineData("func apply(f: () -> Never) -> i32 => f()")]
    public void BindsPositionalSignature(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var plan = Assert.IsType<CallPlan>(call.ValueCallOf());
        Assert.Null(call.CallOf());
        Assert.Same(call.Method, plan.CalleeValue);
        Assert.Same(call.TypeOf(), plan.ReturnType);
        Assert.Equal(call.ArgumentNodes.Count, plan.ArgumentOperations.Length);
    }

    // SPEC 7.3, 13.5.5.1: an object callee records the acquisition of its complete payload; a value reference keeps none.
    [Theory]
    [InlineData("func apply(f: objref/(() -> i32)) -> i32 => f()", true)]
    [InlineData("func apply(f: rc/(() -> i32)) -> i32 => f()", true)]
    [InlineData("func apply(f: rc/(() -> i32)) -> i32 => f@follow()", true)]
    [InlineData("func apply(f: ref/(() -> i32)) -> i32 => f()", false)]
    public void ObjectCalleesRecordTheirPayloadReceiver(string source, bool payload)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var plan = Assert.IsType<CallPlan>(call.ValueCallOf());
        Assert.Same(call.Method, plan.CalleeValue);
        Assert.Equal(payload ? ArgumentOperationKind.PayloadProjection : ArgumentOperationKind.Value, plan.ReceiverOperation.Kind);
        if (payload)
        {
            Assert.Same(KotoHelper.UnwrapParentheses(call.Method) is ConversionKoto follow ? follow.Left : call.Method, plan.ReceiverOperation.Source);
            Assert.Equal((SemanticsKind.Ref, BoundTypeKind.Function), (plan.ReceiverOperation.AdaptedType!.Semantics, plan.ReceiverOperation.AdaptedType.Components[0].Kind));
        }
    }

    [Theory]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f()")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(1, 2)")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(true)")]
    [InlineData("func apply(f: (i8) -> bool) -> bool => f(128)")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(value: 1)")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f<i32>(1)")]
    [InlineData("func apply(f: (i32) -> bool) -> i32 => f(1)")]
    [InlineData("func f(value: i32) -> bool => true\nfunc apply(f: i32) -> bool => f(1)")]
    public void RejectsInvalidValueCalls(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("var count: i32 = 0\nvar next = func [var count] (value: i32) -> i32\n    count = count + 1\n    return value + count\nlet r = next<i32>(1)")]
    [InlineData("let text = \"a\"\nlet once = func [text@move] (value: i32) -> i32 => value\nlet r = once<i32>(1)")]
    public void ExplicitTypeArgumentsOnAClosureCallSelectNothing(string source)
    {
        // A function value has no Type parameters (SPEC 7.6, 12.4.2); an exclusive or consuming closure receiver is not read
        // before the generic application is rejected.
        var c = MinimalEmissionTest.Analyze(source);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(Kimi.DiagnosticCode.NoApplicableOverload_Kd, issue.Code);
        Assert.IsType<InvocationKoto>(issue.Node);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReloadAndWarmBindingRetainValuePlan()
    {
        var c = MinimalEmissionTest.Analyze("func apply<T>(f: (T) -> bool, value: T) -> bool => f(value@move)");
        Assert.True(c.Binding.Result.IsComplete);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var plan = call.ValueCallOf();
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.Same(plan, call.ValueCallOf());
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        Assert.NotNull(Assert.Single(KotoTree.Walk(restored.Kotonoha.RootKoto).OfType<InvocationKoto>()).ValueCallOf());
    }
}
