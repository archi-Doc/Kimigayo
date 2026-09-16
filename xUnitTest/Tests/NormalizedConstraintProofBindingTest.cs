// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class NormalizedConstraintProofBindingTest
{
    private const string Head = "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n";

    [Theory]
    [InlineData("Source.Origin.Item", "i32", ConstraintProof.Proven)]
    [InlineData("i32", "Source.Origin.Item", ConstraintProof.Proven)]
    [InlineData("Source.Origin.Item or string", "i32 or string", ConstraintProof.Proven)]
    [InlineData("i32 or string", "Source.Origin.Item or string", ConstraintProof.Proven)]
    [InlineData("not Source.Origin.Item", "i32", ConstraintProof.Refuted)]
    [InlineData("not i32", "Source.Origin.Item", ConstraintProof.Refuted)]
    [InlineData("[2 of Source.Origin.Item] or string", "[2 of i32] or string", ConstraintProof.Proven)]
    [InlineData("(Source.Origin.Item, string) or bool", "(i32, string) or bool", ConstraintProof.Proven)]
    [InlineData("Source.Origin.Item or string", "i32", ConstraintProof.Unknown)]
    [InlineData("Source.Origin.Item or string\n    T is not i32", "string", ConstraintProof.Unknown)]
    [InlineData("Source.Origin.Item\n    T is not i32", "bool", ConstraintProof.Error)]
    public void EquivalentRequirementSpellingsUseTheSameEvidence(string evidence, string requirement, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func context<T>(value: T)\n    T is " + evidence + "\n    ()\nfunc query<T>(value: T)\n    T is " + requirement + "\n    ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        AssertProof(c, expected);
        c.Bind();
        AssertProof(c, expected);
        var restored = Reload(c);
        restored.Bind();
        AssertProof(restored, expected);
    }

    [Theory]
    [InlineData("Source.Origin.Item or string", "i32 or string")]
    [InlineData("i32 or string", "Source.Origin.Item or string")]
    [InlineData("not Source.Origin.Item", "not i32")]
    [InlineData("not i32", "not Source.Origin.Item")]
    public void DependentCallsUseEquivalentCompoundPremises(string evidence, string requirement)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is " + requirement + "\n    ()\nfunc caller<U>()\n    U is " + evidence + "\n    take<U>()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Assert.IsType<InvocationKoto>(Function(c, "caller").Body!.Items.Single()).BoundCall);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConcreteProofNormalizesTheAssociatedRequirement(string argument, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func query<T>(value: T)\n    T is Source.Origin.Item\n    ()\nfunc context(value: " + argument + ") => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertProof(c, expected);
    }

    [Theory]
    [InlineData("Source.Origin.Item and Copy", "i32 and Copy", ConstraintProof.Proven)]
    [InlineData("not (Source.Origin.Item and string)", "not i32", ConstraintProof.Unknown)]
    [InlineData("not (Source.Origin.Item and string)", "not (i32 and string)", ConstraintProof.Proven)]
    [InlineData("[2 of Source.Origin.Item]", "[3 of i32]", ConstraintProof.Unknown)]
    [InlineData("ref/Source.Origin.Item from static", "i32", ConstraintProof.Unknown)]
    public void NormalizationDoesNotAddLogicalRulesOrEraseTypeStructure(string evidence, string requirement, ConstraintProof expected)
        => this.EquivalentRequirementSpellingsUseTheSameEvidence(evidence, requirement, expected);

    [Fact]
    public void ReplacingAssociatedIdentityRebuildsProofEvidence()
    {
        const string functions = "func context<T>(value: T)\n    T is Source.Origin.Item or string\n    ()\nfunc query<T>(value: T)\n    T is i32 or string\n    ()";
        var c = MinimalEmissionTest.Analyze(Head + functions);
        AssertProof(c, ConstraintProof.Proven);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(Head.Replace("is i32", "is bool", StringComparison.Ordinal) + functions);
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Unknown);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Proven);
    }

    [Fact]
    public void WarmNormalizedProofsAndCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is [2 of i32] or string\n    ()\nfunc caller<U>()\n    U is [2 of Source.Origin.Item] or string\n    take<U>()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Normalized constraint proof failed.");
            }
        }));
    }

    private static void AssertProof(Compilation c, ConstraintProof expected)
    {
        var context = Function(c, "context");
        var query = Function(c, "query");
        var proposition = ((IsKoto)query.TypeConstraints[0]).BoundConstraint!;
        Assert.Equal(expected, c.Binding.Prove(proposition, query, [context.Parameters[0].Type.BoundType], context));
    }

    private static FunctionKoto Function(Compilation c, string name)
        => Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

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
