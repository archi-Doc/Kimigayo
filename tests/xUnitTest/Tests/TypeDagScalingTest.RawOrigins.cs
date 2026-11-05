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
    // by its declared layer. The raw leaves reach Binding's raw-Origin walks (AccumulateRequirements, SubstituteStoredOrigins,
    // WithoutUnstated, HoldsHolder), each distinct part once per call too (PLAN G309).
    [Theory]
    [InlineData("i32", 40, "")]
    [InlineData("raw/i32 during static", 40, "Static")]
    [InlineData("raw/i32 during s", 40, "Shared")]
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

    // ReplaceHolder (SPEC 5.7, Reading a Field): a read through a dereference rebuilds only the parts holding a `during self` layer; the
    // shared raw part beside the layer holds none and is its own image (PLAN G309). The layer's Tuple sits beside a DAG 40 deep: an
    // instance's Field `S<T>.h: (raw/i32 during self, T)` at the end of the chain D_i, or a projection chain beside the layer in H.
    [Theory]
    [InlineData("instance", 40)]
    [InlineData("projection", 40)]
    public void AHolderReadRebuildsOnlyThePartsHoldingTheLayer(string form, int size)
    {
        var c = MirBuildTest.Compile(HolderBeside(form, size));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "v").NameKoto.TypeOf()!;
        Assert.Equal(BoundTypeKind.Tuple, read.Kind);
        Assert.NotEqual(OriginKind.Holder, read.Components[0].RawOrigin?.Kind);
        var part = read.Components[1];
        for (var k = form == "instance" ? 1 : 0; k < size; k++)
        {
            Assert.Same(part.Components[0], part.Components[1]);
            part = part.Components[0];
        }

        Assert.Equal(OriginKind.Static, part.RawOrigin?.Kind);
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

    // The forms of AHolderReadRebuildsOnlyThePartsHoldingTheLayer, each read through `ref/`.
    private static string HolderBeside(string form, int n)
    {
        if (form == "projection")
        {
            var type = "raw/i32 during static";
            for (var i = 0; i < n; i++)
            {
                type = $"Dup<{type}>.Item";
            }

            return "contract HasItem\n    associate Item\nstruct Dup<T>\n    Self is HasItem\n    associate HasItem.Item is (T, T)\n" +
                $"struct H\n    internal var x: (raw/i32 during self, {type})\nfunc rd(h: ref/H) -> ()\n    let v = h.x\n    return\npublic func main() => ()\n";
        }

        var text = new System.Text.StringBuilder("struct S<T>\n    public var h: (raw/i32 during self, T)\n");
        for (var i = 1; i < n; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct D{i}<T>\n    public var x: D{i + 1}<(T, T)>\n");
        }

        return text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct D{n}<T>\n    public var x: S<T>\n")
            .Append(System.Globalization.CultureInfo.InvariantCulture, $"func rd(d: ref/D1<raw/i32 during static>) -> ()\n    let v = d{string.Concat(Enumerable.Repeat(".x", n))}.h\n    return\n")
            .Append("public func main() => ()\n").ToString();
    }
}
