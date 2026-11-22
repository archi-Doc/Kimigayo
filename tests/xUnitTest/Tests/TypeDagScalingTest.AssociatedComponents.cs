// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C4 commit 0 (PLAN G172; design Revision 2 §3.3 and §7, the delta check's D2-C2): associated-Type inference evaluates one
    // component of definitions per request, in an order where each component follows those it reads, so no request nests in another.
    // A one-value chain of 10,000 definitions (valid by SPEC 8.5.5: every X is i32) is 10,000 requests in sequence; a two-value ring of
    // 10,000 definitions (valid by SPEC 8.3.3 Finality: every X and Y is i32) is one component whose held states pass OQ2's items
    // (65,536), one Resource record at its least definition by name (SPEC 8.3.3 Limits, 23.1.1), as is a ring of 9 (the model's 65,536
    // states: past the bound). The record is AnalysisLimit_Kd naming its bound (the owner's O-484 (3 ii)); dev's nested batches recurse once
    // per definition of the chain.
    // R7-C4 (C4 ruling 11; design §3.3 and §6): a request evaluates the model's held states (`value_model_r2.py shapes`): each held state
    // keyed by (binding, hold n Reach(binding)), its own run collecting only its binding's sources, the requirements' Types read with the
    // definition's bindings held. Rings of 4, 6 and 8 definitions (valid by SPEC 8.3.3 Finality) are one component each; a two-value chain
    // of 30 (valid by SPEC 8.5.5) is 31 components of one definition. Each is accepted, every read lies in its reader's Reach.
    [Theory]
    [InlineData("ring", 4, 364)]
    [InlineData("ring", 6, 4122)]
    [InlineData("ring", 8, 41080)]
    [InlineData("two-value chain", 30, 62)]
    public void AComponentEvaluatesTheModelsHeldStates(string shape, int size, int states)
    {
        var c = MinimalEmissionTest.Analyze(shape == "ring" ? Verification.AssociatedInferenceWorkloads.TwoValueRing(size) : Verification.AssociatedInferenceWorkloads.TwoValueChain(size));
        Assert.Equal((shape, size, states, 0, 0), (shape, size, c.Binding.InferenceStates, c.Binding.InferenceReachMisses, CompilationTestHelper.BindingErrors(c).Length));
    }

    [Fact]
    public void AChainAndARingOfTenThousandDefinitionsStayWithinTheStack()
    {
        CompilationTestHelper.AssertBindingRecordsAtLines("chain 10000", Verification.AssociatedInferenceWorkloads.OneValueChain(10000), string.Empty);
        AssertOneLimitRecord(10000);
    }

    [Fact]
    public void ARingPastTheBoundIsOneResourceRecordAtItsLeastDefinition() => AssertOneLimitRecord(9);

    // A two-value ring past OQ2's items: its request holds 65,536 states, and its one Resource record is at R0's conformance, naming the bound.
    private static void AssertOneLimitRecord(int size)
    {
        var program = Verification.AssociatedInferenceWorkloads.TwoValueRing(size);
        var c = MinimalEmissionTest.Analyze(program);
        var limit = Assert.Single(CompilationTestHelper.BindingErrors(c), static x => x.Code == nameof(Kimi.DiagnosticCode.AnalysisLimit_Kd));
        var at = program.IndexOf("struct R0\n", StringComparison.Ordinal) + "struct R0\n    ".Length;
        const string Text = "A judgment here is not decided within the implementation limit of 65536 held states of one component of associated-Type inference";
        Assert.Equal((size, Kimi.Compiler.Binding.InferenceStateLimit, at, Text), (size, c.Binding.InferenceStates, limit.Span.Start, limit.Message));
    }
}
