// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C4 commit 0 (PLAN G172; design Revision 2 §3.3 and §7, the delta check's D2-C2): associated-Type inference evaluates one
    // component of definitions per request, in an order where each component follows those it reads, so no request nests in another.
    // A one-value chain of 10,000 definitions (valid by SPEC 8.5.5: every X is i32) is 10,000 requests in sequence; a two-value ring of
    // 10,000 definitions (valid by SPEC 8.3.3 Finality: every X and Y is i32) is one component whose held states pass OQ2's items
    // (65,536), one Resource record at its least definition by name (SPEC 8.3.3 Limits, 23.1.1). Explicit until commit 5 lands the bound:
    // dev's nested batches recurse once per definition of the chain.
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

    [Fact(Explicit = true)]
    public void AChainAndARingOfTenThousandDefinitionsStayWithinTheStack()
    {
        CompilationTestHelper.AssertBindingRecordsAtLines("chain 10000", Verification.AssociatedInferenceWorkloads.OneValueChain(10000), string.Empty);
        CompilationTestHelper.AssertBindingRecordsAtLines("ring 10000", Verification.AssociatedInferenceWorkloads.TwoValueRing(10000), "Resource: Self is RC | ...");
    }
}
