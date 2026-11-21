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
    [Fact(Explicit = true)]
    public void AChainAndARingOfTenThousandDefinitionsStayWithinTheStack()
    {
        CompilationTestHelper.AssertBindingRecordsAtLines("chain 10000", Verification.AssociatedInferenceWorkloads.OneValueChain(10000), string.Empty);
        CompilationTestHelper.AssertBindingRecordsAtLines("ring 10000", Verification.AssociatedInferenceWorkloads.TwoValueRing(10000), "Resource: Self is RC | ...");
    }

}
