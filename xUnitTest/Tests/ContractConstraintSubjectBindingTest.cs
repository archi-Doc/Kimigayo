// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>
/// Contract-level Constraint Clause subjects (SPEC 8.2/8.4/8.4.3).
/// A Contract body clause may constrain an associated-Type projection rooted in the conforming
/// Type, because that clause is a premise every implementation must discharge. A projection
/// rooted in an unrelated concrete Type states a closed proposition that the declaration cannot
/// assume; it is rejected like the equivalent struct, enum and function clauses instead of
/// silently becoming an unverified premise.
/// </summary>
public class ContractConstraintSubjectBindingTest
{
    private const string Head = "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n";

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void ContractRootedProjectionConstrainsImplementations(string item, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Implementation("Origin.Item is Copy", item));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, Contract(c).BindingState);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void SelfRootedProjectionConstrainsImplementations(string item, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Implementation("Self.Origin.Item is Copy", item));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Source.Origin.Item is i32")]
    [InlineData("Source.Origin.Item is string")]
    [InlineData("Source.Origin.Item is Copy")]
    public void ConcreteRootedProjectionIsNotAValidContractSubject(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Head + "public contract R\n    " + clause);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidConstraint);
        Assert.Equal(BindingState.Invalid, Contract(c).BindingState);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Contract(restored).BindingState);
    }

    [Fact]
    public void AnInvalidContractSubjectCannotCertifyConformance()
    {
        var c = MinimalEmissionTest.Analyze(Head + "public contract R\n    Source.Origin.Item is i32\npublic struct Impl\n    Self is R");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidConstraint);
        var implementation = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Impl");
        Assert.False(c.Binding.GetConformanceDefinition(implementation.BoundType!, Contract(c).BoundSymbol!)?.IsVerified);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    public void ConcreteRootedProjectionStaysInvalidInTypeDeclarations(string kind)
    {
        var c = MinimalEmissionTest.Analyze(Head + "public " + kind + " R<T>\n    Source.Origin.Item is i32" + (kind == "enum" ? "\n    A" : string.Empty));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidConstraint);
    }

    [Fact]
    public void ReplacingTheSubjectRestoresAndRevokesTheContract()
    {
        var c = MinimalEmissionTest.Analyze(Head + "public contract R: Origin\n    Source.Origin.Item is Copy");
        var clause = Contract(c).ConstraintNodes[0];
        var original = clause.Left;
        var donor = MinimalEmissionTest.Analyze(Head + "public contract R: Origin\n    Origin.Item is Copy");
        var replacement = Contract(donor).ConstraintNodes[0].Left;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(BindingState.Resolved, Contract(c).BindingState);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Contract(c).BindingState);
    }

    [Fact]
    public void WarmContractSubjectChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Implementation("Origin.Item is Copy", "i32"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Contract constraint subject check failed.");
            }
        }));
    }

    private static string Implementation(string clause, string item)
        => Head + "public contract R: Origin\n    " + clause + "\npublic struct Impl\n    Self is Origin\n    Self is R\n    associate Origin.Item is " + item;

    private static DeclarationContainerKoto Contract(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R");

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
}
