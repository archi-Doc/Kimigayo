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
        CompilationTestHelper.AssertBindingRecordsAtLines("chain 10000", OneValueChain(10000), string.Empty);
        CompilationTestHelper.AssertBindingRecordsAtLines("ring 10000", TwoValueRing(10000), "Resource: Self is RC | ...");
    }

    private static string OneValueChain(int n)
        => "contract CC\n    associate X\n    func a(self: ref/Self) -> Self.X\n" +
            string.Concat(Enumerable.Range(0, n).Select(static i => $"struct C{i}\n    Self is CC\n    public func a(self: ref/Self) -> C{i + 1}.(CC).X => $abort(\"a\")\n")) +
            $"struct C{n}\n    Self is CC\n    public func a(self: ref/Self) -> i32 => 0\n()";

    private static string TwoValueRing(int n)
        => "contract RC\n    associate X\n    associate Y\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            "    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\n" +
            string.Concat(Enumerable.Range(0, n).Select(i => $"struct R{i}\n    Self is RC\n" +
                $"    public func a(self: ref/Self) -> R{(i + 1) % n}.(RC).X => $abort(\"a\")\n" +
                $"    public func b(self: ref/Self) -> R{(i + 1) % n}.(RC).Y => $abort(\"b\")\n" +
                "    public func c(self: ref/Self) -> i32 => 0\n" +
                $"    public func d(self: ref/Self) -> R{(i + 1) % n}.(RC).X => $abort(\"d\")\n")) + "()";
}
