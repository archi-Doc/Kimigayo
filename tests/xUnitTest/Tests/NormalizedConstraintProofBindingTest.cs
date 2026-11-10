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
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        AssertProof(c, expected);
        c.Bind();
        AssertProof(c, expected);
        var restored = CompilationTestHelper.Reload(c);
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
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.NotNull(Assert.IsType<InvocationKoto>(Function(c, "caller").Body!.Items.Single()).CallOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConcreteProofNormalizesTheAssociatedRequirement(string argument, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func query<T>(value: T)\n    T is Source.Origin.Item\n    ()\nfunc context(value: " + argument + ") => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        AssertProof(c, expected);
    }

    // Pins Binding's proof value Unknown for the [3 of i32] and i32 rows, which SPEC 8.2.3 refutes, pending PLAN G170's C2-C4 (R4-M115).
    [Theory]
    [InlineData("Source.Origin.Item and Copy", "i32 and Copy", ConstraintProof.Proven)]
    [InlineData("not (Source.Origin.Item and string)", "not i32", ConstraintProof.Unknown)]
    [InlineData("not (Source.Origin.Item and string)", "not (i32 and string)", ConstraintProof.Proven)]
    [InlineData("[2 of Source.Origin.Item]", "[3 of i32]", ConstraintProof.Unknown)]
    [InlineData("ref/Source.Origin.Item during static", "i32", ConstraintProof.Unknown)]
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
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Unknown);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Proven);
    }

    // DECISIONS R7-C2e1 (F5): a requirement's signature is formed in its declaration's view under the call's reference (SPEC 8.6.4), so
    // P's clause reads `A.(Src).Item is not Marker` under P<A> and `B.(Src).Item is not Marker` under P<B>, and each call's candidate is
    // refuted under its own reference (SPEC 8.3.2): a reading is one per requirement Contract. Both orders.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadingIsOnePerRequirementReference(bool reversed)
    {
        const string Declarations = "contract Marker\ncontract Src\n    associate Item\nstruct Box<T>\n    T is Marker\n    public init() => ()\n" +
            "contract P<E>\n    E is Src\n    E.(Src).Item is not Marker\n    func read(self: ref/Self) -> Box<E.(Src).Item>\n";
        var (first, second) = (Use("via", "A"), Use("via2", "B"));
        var source = Declarations + (reversed ? second + first : first + second);
        Assert.Equal("2*NoApplicableOverload_Kd 3*UnsatisfiedConstraint_Kd", DiagnosticCorpus.RecordProfile(source));

        static string Use(string name, string slot)
            => $"func {name}<T, {slot}>(t: ref/T)\n    T is P<{slot}>\n    {slot} is Src\n    {slot}.(Src).Item is not Marker\n    t.read()\n";
    }

    // DECISIONS R7-C2e1 (F6): the builtin specification of `T.(Addable<T>).Output` proves T's arithmetic conditions in f's view, where
    // both premises naming the projection meet the open normalization and stay unreduced. Those readings are not the view's: `g<V>()`
    // reads the second premise as `V is Marker` (SPEC 8.2.3). Both orders; without that premise `V is Marker` is unproven.
    [Theory]
    [InlineData("T.(Addable<T>).Output is V\n    T.(Addable<T>).Output is Marker\n", "clean")]
    [InlineData("T.(Addable<T>).Output is Marker\n    T.(Addable<T>).Output is V\n", "clean")]
    [InlineData("T.(Addable<T>).Output is V\n", "1*UnprovenConstraint_Kd")]
    public void AReadingFormedUnderANormalizationIsNotKept(string premises, string records)
        => Assert.Equal(
            records,
            DiagnosticCorpus.RecordProfile("contract Marker\nfunc g<X>()\n    X is Marker\n    ()\nfunc f<T, V>()\n    T is Addable<T>\n    " + premises + "    g<V>()\n"));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNormalizedProofsAndCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is [2 of i32] or string\n    ()\nfunc caller<U>()\n    U is [2 of Source.Origin.Item] or string\n    take<U>()");
        Assert.True(c.Bind().IsComplete);
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
        var proposition = ((IsKoto)query.TypeConstraints[0]).ConstraintOf()!;
        Assert.Equal(expected, c.Binding.Prove(proposition, query, [context.Parameters[0].Type.TypeOf()], context));
    }

    private static FunctionKoto Function(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);
}
