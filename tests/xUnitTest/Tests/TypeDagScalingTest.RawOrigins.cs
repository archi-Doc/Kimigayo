// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G307's Binding part (DECISIONS BU-58): the raw-Origin views A5 and MirValidator read and the Binding walks over a
/// written Type decide each distinct part once per call. Each row's DAG is 40 deep, so a walk that re-visits shared parts meets 2^40
/// paths, a hang the runner's timeout reports, while the DAG walk takes milliseconds; the rows build their Types through TyCtxt.</summary>
public partial class TypeDagScalingTest
{
    // StatesUnstated (MirValidator's Local row) over T_{k+1} = (T_k, T_k), T_0 = `raw/i32 during static`, and U_{k+1} = (T_k, U_k), U_0 a
    // layer stating Unstated: each raw-Origin part is entered once, so its seen keys are exactly the distinct parts below the root.
    [Fact]
    public void StatesUnstatedEntersEachRawPartOnce()
    {
        var types = MirBuildTest.Compile("public func main() => ()\n").Types;
        var t = new BoundType[41];
        var u = new BoundType[41];
        t[0] = types.Pointer(BoundType.I32, BoundOrigin.Static);
        u[0] = types.Pointer(BoundType.I32, BoundOrigin.Unstated);
        for (var k = 0; k < 40; k++)
        {
            t[k + 1] = types.Tuple([t[k], t[k]]);
            u[k + 1] = types.Tuple([t[k], u[k]]);
        }

        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.False(TyCtxt.StatesUnstated(t[40], seen));
        Assert.True(seen.SetEquals(t[..40]));
        Assert.True(TyCtxt.StatesUnstated(u[40], seen));
        Assert.True(seen.SetEquals(t[..40].Concat(u[..40])));
    }

    // WithoutRawOrigins, AdtDef's view of an instance's kept parts, over D<T_40> with `struct D<T> { x: T }`: the kept Field is T_40
    // itself and AdtDef's is its erased twin E_{k+1} = (E_k, E_k), E_0 = `raw/i32`.
    [Fact]
    public void AnInstancesKeptPartIsErasedOncePerPart()
    {
        var c = MirBuildTest.Compile("struct D<T>\n    public var x: T\npublic func main() => ()\n");
        var types = c.Types;
        var t = types.Pointer(BoundType.I32, BoundOrigin.Static);
        var e = types.Pointer(BoundType.I32);
        for (var k = 0; k < 40; k++)
        {
            t = types.Tuple([t, t]);
            e = types.Tuple([e, e]);
        }

        var symbol = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(static x => x.Name == "D").SymbolOf()!;
        var d = types.Intern(BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, [t]);
        Assert.Same(t, types.StoredParts(d, out _)[0]);
        Assert.Same(e, AdtDef.FieldType(d, 0));
        Assert.Same(e, c.Binding.WithoutRawOrigins(t));
    }

    // A closed projection writes a Tuple DAG of 2^n positions from O(n) text (ProjectionChain): Bind (TypeAccessCovers,
    // CollectRequirementEdges, WithUnstatedOrigins), the kept part and WrittenAt down the written DAG are linear in n, and the leaf steps
    // by its declared layer. The raw leaves reach Binding's raw-Origin walks, which stay exponential until PLAN G309 (Explicit until then).
    [Theory]
    [InlineData("i32", 40, "")]
    [InlineData("raw/i32 during static", 40, "Static", Explicit = true)]
    [InlineData("raw/i32 during s", 40, "Shared", Explicit = true)]
    public void AWrittenProjectionDagIsWalkedOncePerPart(string leaf, int size, string step)
    {
        var c = MirBuildTest.Compile(ProjectionChain(leaf, size));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var holder = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "pv").Parameters[1].Type.TypeOf()!;
        var kept = c.Types.StoredParts(holder, out _)[1]!;
        var path = new int[size];
        for (var k = 0; k < size; k++)
        {
            var written = TyCtxt.WrittenAt(holder, 1, path.AsSpan(0, k))!;
            Assert.Equal((BoundTypeKind.Tuple, BoundTypeKind.Tuple), (written.Kind, kept.Kind));
            Assert.Same(written.Components[0], written.Components[1]);
            Assert.Same(kept.Components[0], kept.Components[1]);
            kept = kept.Components[0];
        }

        Assert.Equal(step, TyCtxt.WrittenAt(holder, 1, path) is null ? string.Empty : c.Types.RawStep(holder, 1, path).ToString());
    }

    // BU's projection chain (G307's design): `H {s} { x: Dup<…Dup<L>.Item…>.Item }`, n projections deep, with `associate HasItem.Item is
    // (T, T)`, read by `rd` and passed by value to `pv`; s is a `ref` slot.
    internal static string ProjectionChain(string leaf, int n)
    {
        var type = leaf;
        for (var i = 0; i < n; i++)
        {
            type = $"Dup<{type}>.Item";
        }

        return "contract HasItem\n    associate Item\nstruct Dup<T>\n    Self is HasItem\n    associate HasItem.Item is (T, T)\n" +
            $"struct H {{s}}\n    internal let loan: Loan<ref/i32 during s>\n    internal var x: {type}\n" +
            "func pv(q: ref/i32, hv: H during q) => ()\nfunc rd(q: ref/i32, h: ref/H during q) -> ()\n    let v = h.x\n    return\npublic func main() => ()\n";
    }
}
