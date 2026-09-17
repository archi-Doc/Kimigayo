// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalProjectionCertificateBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidProjectionPremiseCannotCertifyConformance(bool lateWitness)
    {
        var c = MinimalEmissionTest.Analyze(Prefix(lateWitness, false) + Consumer);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidNestedPremisesKeepTheirEvidence(bool lateWitness)
    {
        var c = MinimalEmissionTest.Analyze(Prefix(lateWitness, true) + Consumer.Replace("T is Source.Origin.Item", "T is ([2 of Source.Origin.Item], i32)", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardInvalidWitnessAlsoRevokesPremiseEvidence(bool lateWitness)
    {
        var c = MinimalEmissionTest.Analyze(Consumer + "\n" + Prefix(lateWitness, false));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentConformanceAndTypeFormationRemainUsable(bool lateWitness)
    {
        var source = Prefix(lateWitness, false) + Consumer.Replace("contract C", "contract D\ncontract C", StringComparison.Ordinal) + "\n    Self is D when T is Copy\nfunc accept(x: S<string>) => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.True(Definition(c, "D").IsVerified);
        Assert.Equal(BindingState.Resolved, Type(c).BindingState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalCopyCannotUseInvalidProjectionPremise(bool valid)
    {
        var source = Prefix(true, valid) + "struct S<T>\n    Self is Copy when T is Source.Origin.Item\nfunc take<U>()\n    U is Copy\n    ()\ntake<S<i32>>()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var call = Assert.IsType<InvocationKoto>(c.Kotonoha.GeneratedFunction!.Body!.Items.Last());
        Assert.Equal(valid, call.BoundCall is not null);
        var copy = c.Binding.GetConformanceDefinition(Type(c).BoundType!, c.Library.Copy)!;
        Assert.Equal(valid, copy.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalMembersFollowPremiseValidity(bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix(true, valid) + Consumer + "\n        public func value() -> i32 => 1\nS<i32>.value()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
        var call = Assert.IsType<InvocationKoto>(c.Kotonoha.GeneratedFunction!.Body!.Items.Last());
        Assert.Equal(valid, call.BoundCall is not null);
    }

    [Fact]
    public void RepairingWitnessRestoresConditionalEvidence()
    {
        var source = Prefix(true, false) + Consumer + PropertyMember;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(ConditionalProperty(c).IsVerified);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single();
        var original = function.Parameters[1].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("x: Local.Hidden.Item", "x: i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single().Parameters[1].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
        Assert.True(ConditionalProperty(c).IsVerified);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.False(ConditionalProperty(c).IsVerified);
    }

    [Fact]
    public void WarmConditionalProjectionChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix(true, true) + Consumer);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Conditional premise verification failed.");
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifierConstraintsUseTheConditionalEnvironment(bool reverse)
    {
        var conditions = reverse ? "U is Input<T>.Origin.Item, T is Copy" : "T is Copy, U is Input<T>.Origin.Item";
        var c = MinimalEmissionTest.Analyze("contract Origin\n    associate Item\nstruct Input<T>\n    T is Copy\n    Self is Origin\n    associate Origin.Item is i32\ncontract C\nstruct S<T, U>\n    Self is C when " + conditions);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvisionalAndFinalPassesKeepInvalidPremisesUncertified(bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix(true, valid) + Consumer);
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(valid, provisional.InvalidCount == 0);
        Assert.Equal(valid, Definition(c).IsVerified);
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
    }

    private const string Consumer = "contract C\nstruct S<T>\n    Self is C when T is Source.Origin.Item";

    private const string PropertyMember = "\n        public computed value: i32\n            get(self: ref/Self) -> i32 => 1";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ComputedPropertiesRequireValidEnclosingPremises(bool lateWitness, bool valid)
    {
        var source = Prefix(lateWitness, valid) + Consumer + PropertyMember + "\n    public computed independent: i32\n        get(self: ref/Self) -> i32 => 2";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, ConditionalProperty(c).IsVerified);
        Assert.True(Type(c).Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!.IsVerified);
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, ConditionalProperty(c).IsVerified);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, ConditionalProperty(restored).IsVerified);
    }

    [Fact]
    public void WarmConditionalPropertyChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix(true, true) + Consumer + PropertyMember);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Conditional Property validation failed.");
            }
        }));
    }

    private static BoundProperty ConditionalProperty(Compilation c)
    {
        var conditional = Type(c).Members.OfType<SyntaxFormKoto>().Single();
        return Assert.IsType<CodeBlockKoto>(conditional.Operands[2]).Items.OfType<PropertyKoto>().Single().BoundSymbol!.Property!;
    }

    private static string Prefix(bool lateWitness, bool valid)
        => lateWitness
            ? (valid ? "public" : "internal") + " contract Hidden\n    associate Item\npublic struct Local\n    Self is Hidden\n    associate Hidden.Item is i32\npublic contract Origin\n    associate Item\n    func f(self: ref/Self, x: i32) -> i32\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n    public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x\n"
            : "contract Origin\n    associate Item\nstruct Input<T>\n    T is i32\n    Self is Origin\n    associate Origin.Item is i32\nstruct Source\n    Self is Origin\n    associate Origin.Item is Input<" + (valid ? "i32" : "string") + ">.Origin.Item\n";

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static DeclarationContainerKoto Type(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");

    private static BoundConformance Definition(Compilation c, string contractName = "C")
        => c.Binding.GetConformanceDefinition(Type(c).BoundType!, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == contractName).BoundSymbol!)!;
}
