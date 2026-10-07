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
        Assert.NotNull(direct.VirtualDispatch);
        Assert.True(direct.VirtualDispatch.IsDirect);
        Assert.Equal("B", direct.VirtualDispatch.ImplementingType!.Symbol!.Name);
        Assert.True(direct.VirtualDispatch.Implementation!.IsOverride);
        Assert.NotNull(dynamic.VirtualDispatch);
        Assert.False(dynamic.VirtualDispatch.IsDirect);
        Assert.Null(dynamic.VirtualDispatch.Implementation);
        Assert.Equal(direct.VirtualDispatch.Slot, dynamic.VirtualDispatch.Slot);
    }

    [Fact]
    public void ABaseWithoutAnOverrideRetainsTheInheritedOriginalEntry()
    {
        var c = MinimalEmissionTest.Analyze(Types.Replace("    override func read(self: objref/Self, n: i32) -> i32 => n + 10\n", string.Empty, StringComparison.Ordinal) + "()");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto }).CallStorage!;
        Assert.Same(call.Target.Declaration, call.VirtualDispatch!.Implementation);
        Assert.Equal("B", call.VirtualDispatch.BaseLookupType!.Symbol!.Name);
        Assert.Equal("A", call.VirtualDispatch.ImplementingType!.Symbol!.Name);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("i64")]
    public void BoundGenericAncestorsKeepTheOriginalSlotAndNearestImplementation(string argument)
    {
        var source = "open struct A<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nopen struct B<U> : A<U>\n    override func read(self: objref/Self) -> i32 => 2\nstruct C : B<" + argument + ">\n    override func read(self: objref/Self) -> i32 => (base.read)()\n()";
        var c = MinimalEmissionTest.Analyze(source);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallStorage?.Target.Name == "read").CallStorage!;
        var selection = Assert.IsType<BoundVirtualCall>(call.VirtualDispatch);
        Assert.Equal("A", selection.Slot.DeclaringType.Symbol!.Name);
        Assert.Equal("B", selection.ImplementingType!.Symbol!.Name);
        Assert.Same(selection.BaseLookupType, selection.ImplementingType);
        Assert.Same(selection.Slot.DeclaringType.Components[0], selection.ImplementingType.Components[0]);
        Assert.Equal(argument, selection.ImplementingType.Components[0].Name);
    }

    [Fact]
    public void UnboundAndFunctionItemCallsRetainDynamicSelection()
    {
        var c = MinimalEmissionTest.Analyze(Types + "func call(value: objref/A) -> i32\n    let operation = C.read\n    return A.read(value, 2) + operation(value, 3)\n()");
        var direct = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallStorage?.Target.Name == "read" && x.CallStorage.Receiver is null).CallStorage!;
        var value = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is IdentifierNameKoto { IdentifierName: "operation" }).ValueCallStorage!;
        var item = Assert.IsType<BoundCall>(c.Binding.FunctionItemContext(value.ReceiverType));
        Assert.All(new[] { direct, item }, call =>
        {
            Assert.NotNull(call.VirtualDispatch);
            Assert.False(call.VirtualDispatch.IsDirect);
            Assert.Null(call.VirtualDispatch.Implementation);
            Assert.Equal("A", call.VirtualDispatch.Slot.DeclaringType.Symbol!.Name);
        });
    }

    [Fact]
    public void InstantiationClosesBothTheSlotAndDirectImplementation()
    {
        const string Source = "open struct A<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nopen struct B<U> : A<U>\n    override func read(self: objref/Self) -> i32 => 2\nstruct C<V> : B<V>\n    V is Owned\n    override func read(self: objref/Self) -> i32 => base.read()\nfunc use(value: objref/C<i32>) => ()\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var syntax = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto });
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride && x.BoundSymbol!.Scope.Owner is StructKoto { Name: "C" });
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "use");
        var outer = new BoundCall();
        outer.Set(implementation.BoundSymbol!, BoundType.I32, null, [], [], declaringType: use.Parameters[0].Type.BoundType!.Components[0]);
        Assert.NotNull(syntax.CallStorage);
        var concrete = c.Binding.InstantiateForwardedCall(syntax.CallStorage, outer)!;
        var selection = concrete.VirtualDispatch!;
        Assert.Same(BoundType.I32, selection.Slot.DeclaringType.Components[0]);
        Assert.Equal("B", selection.ImplementingType!.Symbol!.Name);
        Assert.Same(BoundType.I32, selection.ImplementingType.Components[0]);
        Assert.Same(concrete, c.Binding.InstantiateForwardedCall(syntax.CallStorage!, outer, concrete));
        Assert.Same(selection, concrete.VirtualDispatch);
    }

    [Fact]
    public void InvalidOverridesNeverSelectTheOriginalAsFallback()
    {
        var c = MinimalEmissionTest.Analyze(Types.Replace("n: i32) -> i32 => n + 10", "other: i32) -> i32 => other + 10", StringComparison.Ordinal) + "()");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto }).CallStorage!;
        Assert.Equal("B", call.VirtualDispatch!.ImplementingType!.Symbol!.Name);
        Assert.Equal(BindingFailure.OverrideContractMismatch, call.VirtualDispatch.Implementation!.BindingFailure);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RebindingUsesTheChangedBaseAndReusesTheCallRecord()
    {
        const string Other = "open struct Other : A\n    override func read(self: objref/Self, n: i32) -> i32 => n + 20\n";
        var c = MinimalEmissionTest.Analyze(Other + Types + "()");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto });
        var previous = call.CallStorage!.VirtualDispatch;
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "C");
        var donor = MinimalEmissionTest.Analyze(Other + Types.Replace("C : B", "C : Other", StringComparison.Ordinal) + "()");
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "C");
        Assert.True(KotoHelper.Replace(derived, derived.Bases[0], changed.Bases[0]));
        Assert.Null(call.BoundCall);
        c.Bind();
        Assert.Same(previous, call.CallStorage!.VirtualDispatch);
        Assert.Equal("Other", call.CallStorage.VirtualDispatch!.ImplementingType!.Symbol!.Name);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSelectionReusesTheSemanticRecords()
    {
        var c = MinimalEmissionTest.Analyze(Types + "()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
    }
}
