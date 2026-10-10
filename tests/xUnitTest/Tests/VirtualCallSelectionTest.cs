// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class VirtualCallSelectionTest
{
    private const string Types = "open struct A\n    public virtual func read(self: objref/Self, n: i32 = 1) -> i32 => n\nopen struct B : A\n    override func read(self: objref/Self, n: i32) -> i32 => n + 10\nstruct C : B\n    override func read(self: objref/Self, n: i32) -> i32 => base.read() + self.read(n)\n";

    [Fact]
    public void DirectBaseAndDynamicCallsRetainDifferentImplementationChoices()
    {
        var c = MinimalEmissionTest.Analyze(Types + "()");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.Method is MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "read" } }).ToArray();
        Assert.Equal(2, calls.Length);
        var direct = calls.Single(x => ((MemberAccessKoto)x.Method).Left is BaseReferenceKoto).CallStorage!;
        var dynamic = calls.Single(x => ((MemberAccessKoto)x.Method).Left is not BaseReferenceKoto).CallStorage!;
        Assert.Same(direct.Target, dynamic.Target);
        Assert.Equal("A", direct.DeclaringType!.Symbol!.Name);
        Assert.Single(direct.DefaultArguments.ToArray());
        Assert.Equal(CalleeKind.Virtual, direct.Kind);
        Assert.True(direct.VirtualIsDirect);
        Assert.Equal("B", direct.VirtualImplementingType!.Symbol!.Name);
        Assert.True(direct.VirtualImplementation!.IsOverride);
        Assert.Equal(CalleeKind.Virtual, dynamic.Kind);
        Assert.False(dynamic.VirtualIsDirect);
        Assert.Null(dynamic.VirtualImplementation);
        Assert.Equal(direct.VirtualSlot, dynamic.VirtualSlot);
    }

    [Fact]
    public void ABaseWithoutAnOverrideRetainsTheInheritedOriginalEntry()
    {
        var c = MinimalEmissionTest.Analyze(Types.Replace("    override func read(self: objref/Self, n: i32) -> i32 => n + 10\n", string.Empty, StringComparison.Ordinal) + "()");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto }).CallStorage!;
        Assert.Same(call.Target.Declaration, call.VirtualImplementation);
        Assert.Equal("B", call.VirtualBaseLookupType!.Symbol!.Name);
        Assert.Equal("A", call.VirtualImplementingType!.Symbol!.Name);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("i64")]
    public void BoundGenericAncestorsKeepTheOriginalSlotAndNearestImplementation(string argument)
    {
        var source = "open struct A<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nopen struct B<U> : A<U>\n    override func read(self: objref/Self) -> i32 => 2\nstruct C : B<" + argument + ">\n    override func read(self: objref/Self) -> i32 => (base.read)()\n()";
        var c = MinimalEmissionTest.Analyze(source);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "read").BoundCall!;
        Assert.Equal(CalleeKind.Virtual, call.Kind);
        Assert.Equal("A", call.VirtualSlot.DeclaringType.Symbol!.Name);
        Assert.Equal("B", call.VirtualImplementingType!.Symbol!.Name);
        Assert.Same(call.VirtualBaseLookupType, call.VirtualImplementingType);
        Assert.Same(call.VirtualSlot.DeclaringType.Components[0], call.VirtualImplementingType.Components[0]);
        Assert.Equal(argument, call.VirtualImplementingType.Components[0].Name);
    }

    [Fact]
    public void UnboundAndFunctionItemCallsRetainDynamicSelection()
    {
        var c = MinimalEmissionTest.Analyze(Types + "func call(value: objref/A) -> i32\n    let operation = C.read\n    return A.read(value, 2) + operation(value, 3)\n()");
        var direct = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "read" && x.BoundCall.Receiver is null).BoundCall!;
        var value = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is IdentifierNameKoto { IdentifierName: "operation" }).BoundValueCall!;
        var item = Assert.IsType<CallPlan>(c.Binding.FunctionItemContext(value.CalleeType));
        Assert.All(new[] { direct, item }, call =>
        {
            Assert.Equal(CalleeKind.Virtual, call.Kind);
            Assert.False(call.VirtualIsDirect);
            Assert.Null(call.VirtualImplementation);
            Assert.Equal("A", call.VirtualSlot.DeclaringType.Symbol!.Name);
        });
    }

    [Fact]
    public void InstantiationClosesBothTheSlotAndDirectImplementation()
    {
        const string Source = "open struct A<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nopen struct B<U> : A<U>\n    override func read(self: objref/Self) -> i32 => 2\nstruct C<V> : B<V>\n    override func read(self: objref/Self) -> i32 => base.read()\nfunc use(value: objref/C<i32>) => ()\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var syntax = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto });
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride && x.BoundSymbol!.Scope.Owner is StructKoto { Name: "C" });
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "use");
        var outer = new CallPlan();
        outer.Set(implementation.BoundSymbol!, BoundType.I32, null, [], [], declaringType: use.Parameters[0].Type.TypeOf()!.Components[0]);
        Assert.NotNull(syntax.CallStorage);
        var concrete = c.Binding.InstantiateForwardedCall(syntax.CallStorage, outer)!;
        Assert.Same(BoundType.I32, concrete.VirtualSlot.DeclaringType.Components[0]);
        Assert.Equal("B", concrete.VirtualImplementingType!.Symbol!.Name);
        Assert.Same(BoundType.I32, concrete.VirtualImplementingType.Components[0]);
        Assert.Same(concrete, c.Binding.InstantiateForwardedCall(syntax.CallStorage!, outer, concrete));
        Assert.Same(BoundType.I32, concrete.VirtualImplementingType!.Components[0]);
    }

    [Fact]
    public void InvalidOverridesNeverSelectTheOriginalAsFallback()
    {
        var c = MinimalEmissionTest.Analyze(Types.Replace("n: i32) -> i32 => n + 10", "other: i32) -> i32 => other + 10", StringComparison.Ordinal) + "()");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto }).CallStorage!;
        Assert.Equal("B", call.VirtualImplementingType!.Symbol!.Name);
        Assert.Equal(BindingFailure.OverrideContractMismatch, call.VirtualImplementation!.FailureOf());
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RebindingUsesTheChangedBaseAndReusesTheCallRecord()
    {
        const string Other = "open struct Other : A\n    override func read(self: objref/Self, n: i32) -> i32 => n + 20\n";
        var c = MinimalEmissionTest.Analyze(Other + Types + "()");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto });
        var previous = call.CallStorage!;
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "C");
        var donor = MinimalEmissionTest.Analyze(Other + Types.Replace("C : B", "C : Other", StringComparison.Ordinal) + "()");
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "C");
        Assert.True(KotoHelper.Replace(derived, derived.Bases[0], changed.Bases[0]));
        Assert.Null(call.BoundCall);
        c.Bind();
        Assert.Same(previous, call.CallStorage);
        Assert.Equal("Other", previous.VirtualImplementingType!.Symbol!.Name);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSelectionReusesTheSemanticRecords()
    {
        var c = MinimalEmissionTest.Analyze(Types + "()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
    }
}
