// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>C-527 (the owner's C-527 Q1 (a); F's C-527 ruling 5, B1): the predicate that bounds a kept root's reads (Binding's
/// CanCarryLoan, a fold over Holdings) decides each distinct part of the Type a call binds the callee's kept T to once per query. T bound
/// to a doubling Tuple of u8 leaves, 2^30 positions in 31 Types none of which can carry a Loan or an address (the walk's worst case: no
/// early answer), is decided in milliseconds, where a walk over positions would not end; the root then keeps no read through the input.</summary>
public partial class TypeDagScalingTest
{
    [Fact]
    public void AKeptRootsBoundIsDecidedOncePerDistinctPart()
    {
        const int Size = 30;
        var lets = string.Concat(Enumerable.Range(1, Size).Select(static i => $"    let v{i} = (v{i - 1}, v{i - 1})\n"));
        var c = MinimalEmissionTest.Analyze($"func keepO<T>(value: raw/T) -> Option<T> => .None\npublic func main() -> ()\n    let v0: u8 = 0\n{lets}    let r = keepO(v{Size}@raw)\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(x => CallPlan.Of(c.Binding, x, 0))
            .Single(static x => x is { Kind: not CalleeKind.Value } && x.Target.Name == "keepO")!;
        var main = c.Binding.Semantics.SymbolOf(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "main"));
        var retention = c.Types.Retains(call.Target, c.Types.SubstOf(call), main, []);
        Assert.DoesNotContain(retention.Result.Kinds.ToArray(), static kind => kind != RetentionKind.None);
    }
}
