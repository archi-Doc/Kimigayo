// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class VirtualDispatchTest
{
    private const string Hierarchy = "open struct A\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public virtual func value(self: objref/Self, bias: i32 = 1) -> i32 => self.n + bias\n    public virtual func nested(self: objref/Self) -> i32 => self.value(3) + 100\nopen struct B : A\n    public init(n: i32) : base(n) => ()\n    override func value(self: objref/Self, bias: i32) -> i32 => base.value(bias) + 10\n    public virtual func extra(self: objref/Self) -> i32 => 7\nstruct C : B\n    public init(n: i32) : base(n) => ()\n    override func value(self: objref/Self, bias: i32) -> i32 => base.value(bias) + 20\n    public func direct(self: objref/Self) -> i32 => base.value()\n";

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SharedDispatchUsesTheCompleteObjectsMostDerivedEntry(string mode)
    {
        var source = Hierarchy + "let c = C.init(3)@" + mode + "\nrequire c.value() == 34 else => $abort(\"member\")\nrequire c.nested() == 136 else => $abort(\"nested\")\nrequire c.extra() == 7 else => $abort(\"inherited slot\")\nrequire c.direct() == 14 else => $abort(\"base\")\nrequire A.value(c@objref/A, 2) == 35 else => $abort(\"qualified\")";
        NativeAllocationAudit.WriteFixture("VirtualDispatch" + mode, source, 1, 1, 20, string.Empty);
    }

    [Fact]
    public void FunctionItemsAndErasedValuesDispatchOnEveryReceiver()
    {
        const string Source = Hierarchy + "func invoke<F>(f: ref/F, value: objref/A) -> i32\n    F is Callable<(objref/A, i32) -> i32>\n    return f(value, 2)\nlet a = A.init(3)@obj\nlet c = C.init(3)@obj\nlet f = C.value\nrequire f(a@objref, 2) == 5 else => $abort(\"original\")\nrequire f(c@objref/A, 2) == 35 else => $abort(\"item\")\nrequire invoke(f, c@objref/A) == 35 else => $abort(\"callable\")\nlet erased: (objref/A, i32) -> i32 = f\nrequire erased(a@objref, 2) == 5 else => $abort(\"erased original\")\nrequire erased(c@objref/A, 2) == 35 else => $abort(\"erased override\")";
        ScalarEmissionTest.EmitFixture("VirtualDispatchItems", Source, string.Empty);
    }

    [Fact]
    public void SlotsShareTheOrdinaryAggregateAndBorrowResultAbi()
    {
        const string Source = "open struct Base\n    public init() => ()\n    public virtual func pair(zero: (), self: objref/Self, text: string) -> (i32, string) => (1, text@move)\n    public virtual func view(self: objref/Self) -> ref/i32 => self.number@ref\n    public let number: i32 = 2\nstruct Derived : Base\n    public init() => ()\n    public let tail: i32 = 42\n    override func pair(zero: (), self: objref/Self, text: string) -> (i32, string) => (3, text@move)\n    override func view(self: objref/Self) -> ref/i32 => self.tail@ref\nlet d = Derived.init()@obj\nlet p = d.pair((), \"payload\")\nrequire p.0 == 3 and p.1 == \"payload\" else => $abort(\"aggregate\")\nlet r = d.view()\nrequire r@follow == 42 else => $abort(\"borrow\")\nlet f: ((), objref/Base, string) -> (i32, string) = Base.pair\nlet q = f((), d@objref/Base, \"erased\")\nrequire q.0 == 3 and q.1 == \"erased\" else => $abort(\"erased aggregate\")";
        ScalarEmissionTest.EmitFixture("VirtualDispatchAbi", Source, string.Empty);
    }

    [Fact]
    public void DirectDispatchUsesIndexedCallsWithoutAnItemAdapter()
    {
        var source = Hierarchy + "let c = C.init(3)@obj\nrequire c.nested() == 136 else => $abort(\"dispatch\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        var descriptor = Assert.Single(module.Objects);
        Assert.Equal(3, descriptor.VirtualSlots.Length);
        var calls = Enumerable.Range(0, module.FunctionCount).SelectMany(i => module.GetFunction(i).Instructions).Where(x => x.Opcode == EmissionOpcode.VirtualCall).ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, call =>
        {
            Assert.InRange(call.Constant, 0, 2);
            Assert.Equal("ptr", call.Callee!.Parameters[call.Place].Type);
        });
        Assert.DoesNotContain(Enumerable.Range(0, module.FunctionCount), i => module.GetFunction(i).Abi.Name.StartsWith("__kimi_virtual_item", StringComparison.Ordinal));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTableAndDispatchGenerationReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Hierarchy + "let c = C.init(3)@obj\nlet f: (objref/A, i32) -> i32 = C.value\nrequire f(c@objref/A, 2) == 35 else => $abort(\"erased\")");
        Assert.True(c.Emission.Validate(out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Fact]
    public void EditingAnOverrideRevokesAndRebuildsItsGeneratedBody()
    {
        const string Source = "open struct Base\n    public init() => ()\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived : Base\n    public init() => ()\n    override func read(self: objref/Self) -> i32 => 2\nlet d = Derived.init()@obj\nConsole.writeLine(\"\\(d.read())\")";
        var c = MinimalEmissionTest.Analyze(Source);
        ScalarEmissionTest.WriteFixture("VirtualDispatchBeforeEdit", CompilationTestHelper.WriteIr(c), "2\n", 0);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        var donor = MinimalEmissionTest.Analyze(Source.Replace("-> i32 => 2", "-> i32 => 9", StringComparison.Ordinal));
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsOverride);
        Assert.True(KotoHelper.Replace(original, original.ExpressionBody!, changed.ExpressionBody!));
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.WriteFixture("VirtualDispatchAfterEdit", CompilationTestHelper.WriteIr(c), "9\n", 0);
    }
}
