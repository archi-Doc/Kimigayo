// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class VirtualAssociatedHeaderTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedAndDerivedBindingsNormalizeAssociatedSignatures(bool concrete)
    {
        const string Prefix = "contract Catalog\n    associate Element\n    Element is Copy\nstruct Entry\n    Self is Catalog\n    associate Catalog.Element is i32\nopen struct Base<T>\n    public init() => ()\n    public virtual func read(self: objref/Self, value: ref/T.Element) -> T.Element\n        T is Catalog\n        return value@follow\n";
        var derived = concrete ? "struct Derived : Base<Entry>\n" : "struct Derived<U> : Base<U>\n    U is Catalog\n    U.Catalog.Element is i32\n";
        var source = Prefix + derived + "    public init() => ()\n    override func read(self: objref/Self, value: ref/i32) -> i32 => value@follow\n" +
            (concrete ? "let owner = Derived.init()@obj\nlet n = 7\nrequire owner.read(n@ref) == 7 else => $abort(\"projection\")\nlet item = Derived.read\nrequire item(owner@objref/Base<Entry>, n@ref) == 7 else => $abort(\"item\")\nlet erased: (objref/Base<Entry>, ref/i32) -> i32 = Derived.read\nrequire erased(owner@objref/Base<Entry>, n@ref) == 7 else => $abort(\"erased\")" : "()");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        if (concrete)
        {
            ScalarEmissionTest.EmitFixture("VirtualAssociatedClosed", source, string.Empty);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentSlotPremisesDoNotGuessAnAssociatedName(bool qualified)
    {
        var element = qualified ? "U.First.Element" : "U.Element";
        var source = "contract First\n    associate Element\ncontract Second\n    associate Element\nopen struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T.First.Element) -> i32\n        T is First\n        return 1\n    public virtual func read(self: objref/Self, value: ref/T.Second.Element) -> i32\n        T is Second\n        return 2\nstruct Derived<U> : Base<U>\n    override func read(self: objref/Self, value: ref/" + element + ") -> i32 => 3\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(qualified, c.Binding.Result.IsComplete);
        if (qualified)
        {
            var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
            Assert.True(c.Binding.TryGetVirtualOverride(implementation, out var selected));
            Assert.Contains("First.Element", selected.Slot.Original.Parameters[1].Type.ToString(), StringComparison.Ordinal);
        }
        else
        {
            c.Binding.ReportDiagnostics();
            var record = Assert.Single(TestDiagnostics.Of(c));
            Assert.Equal("UnresolvedBinding_Kd", record.Code);
            Assert.Equal("U.Element", record.Text);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizedAssociatedInputsMatchThePublishedType(bool concrete)
    {
        const string Contract = "contract Catalog\n    associate Element\nstruct Entry\n    Self is Catalog\n    associate Catalog.Element is i32\n";
        const string Base = "open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T.Element) -> i32\n        T is Catalog\n        T.Catalog.Element is i32\n        return value@follow\n";
        var derived = concrete ? "struct Derived : Base<Entry>\n    override func read(self: objref/Self, value: ref/i32) -> i32 => value@follow\n"
            : "struct Derived<U> : Base<U>\n    override func read(self: objref/Self, value: ref/U.Element) -> i32 => value@follow\n";
        var c = MinimalEmissionTest.Analyze(Contract + Base + derived + "()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OverrideHeadersInheritAssociatedTypePremises(bool qualified, bool reversed)
    {
        var element = qualified ? "U.Catalog.Element" : "U.Element";
        const string Contract = "contract Catalog\n    associate Element\n    Element is Copy\n";
        const string Base = "open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T.Element) -> T.Element\n        T is Catalog\n        return value@follow\n";
        var derived = $"struct Derived<U> : Base<U>\n    override func read(self: objref/Self, value: ref/{element}) -> {element} => value@follow\n";
        var c = MinimalEmissionTest.Analyze(Contract + (reversed ? derived + Base : Base + derived) + "()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void CommonHeaderPremisesReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("contract Catalog\n    associate Element\n    Element is Copy\nopen struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T.Element) -> T.Element\n        T is Catalog\n        return value@follow\nstruct Derived<U> : Base<U>\n    override func read(self: objref/Self, value: ref/U.Element) -> U.Element => value@follow\n()");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefutedCommonPremisesDoNotSupplyBodyEvidence(bool needsCopy)
    {
        var source = "group Operations\n    public func check<T>()\n        T is Copy\n        return\nopen struct Base<T>\n    public virtual func read(self: objref/Self)\n        T is Copy\n        return\nstruct Derived<U> : Base<U>\n    U is not Copy\n    override func read(self: objref/Self) => " + (needsCopy ? "Operations.check<U>()" : "()") + "\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(!needsCopy, c.Binding.Result.IsComplete);
        if (needsCopy)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.NoApplicableOverload_Kd);
        }
    }

    [Fact]
    public void ResultNamesAreCompletedAfterUniqueInputSelection()
    {
        var source = "contract First\n    associate Element\ncontract Second\n    associate Element\nopen struct Base<T>\n    public virtual func read(self: objref/Self, value: i32) -> T.First.Element\n        T is First\n        $abort(\"unused\")\n    public virtual func read(self: objref/Self, value: bool) -> T.Second.Element\n        T is Second\n        $abort(\"unused\")\nstruct Derived<U> : Base<U>\n    override func read(self: objref/Self, value: i32) -> U.Element => $abort(\"unused\")\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void GenericReferenceInferenceUsesTheSameNormalizedMemberTypes()
    {
        const string Source = "contract Catalog\n    associate Element\n    Element is Copy\nstruct Entry\n    Self is Catalog\n    associate Catalog.Element is i32\nstruct Box<T>\n    public func read<U>(value: ref/T.Element, other: ref/U) -> T.Element\n        T is Catalog\n        return value@follow\nlet f: (ref/i32, ref/bool) -> i32 = Box<Entry>.read\nlet n = 7\nlet b = true\nrequire f(n@ref, b@ref) == 7 else => $abort(\"inference\")";
        ScalarEmissionTest.EmitFixture("VirtualAssociatedReferenceInference", Source, string.Empty);
    }

    [Fact]
    public void ReplacingTheOriginalRebuildsHeaderPremises()
    {
        const string Source = "contract Catalog\n    associate Element\n    Element is Copy\nopen struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T.Element) -> T.Element\n        T is Catalog\n        return value@follow\nstruct Derived<U> : Base<U>\n    override func read(self: objref/Self, value: ref/U.Element) -> U.Element => value@follow\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete);
        var original = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Base");
        var donor = MinimalEmissionTest.Analyze("open struct Base<T>\n    public virtual func read(self: objref/Self, value: i32) -> i32 => value\n()");
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single();
        Assert.True(KotoHelper.Replace(c.Kotonoha.RootKoto, original, replacement));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "U.Element");
        Assert.True(KotoHelper.Replace(c.Kotonoha.RootKoto, replacement, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
