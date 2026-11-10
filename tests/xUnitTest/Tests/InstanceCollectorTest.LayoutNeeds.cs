// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// K 4.8 and K 4.10 with DECISIONS R5-M149 (the owner's LayoutNeed and Q1 (a)): the collector's layout needs past a root's storage, their
// records by Layout's outcome, the one function naming each need's Type, and the checked-only constructor.
public sealed partial class InstanceCollectorTest
{
    private const string BigStruct = "struct Big\n    public var a: [300000000 of i64]\n    public var n: i32\n";

    // APairLayerNeedIsLaidOutWhereItsCaseLeavesStorage's owner-instance storage records (S has no C layout): at g's y, its argument and e.
    private const string OwnerStorage = "InvalidCLayout_Kd (S, i32)|InvalidCLayout_Kd y@move|InvalidCLayout_Kd s/(S, i32)";

    // K 4.8 (DECISIONS R5-M149): a layout need past a root's storage takes its Type's verdict at its form: a part of an oversized Tuple read
    // twice through a raw pointer is OversizedLayout_Kd at each read, a Field of a struct over the size limit read twice its erased Type's one
    // GenerationResourceLimit_Kd, at the first; an element of a fixed array over the limit read through a reference needs only its element's
    // stride, and a displacement over i64 its stride (the counterparts, no record).
    [Theory]
    [InlineData(
        "func f(p: raw/" + Huge + ") -> u8\n    var v: u8 = 0\n    unsafe\n        v = (*p).1\n        v = (*p).1\n    return v\n",
        "OversizedLayout_Kd (*p).1|OversizedLayout_Kd (*p).1")]
    [InlineData(
        BigStruct + "func f(p: raw/Big) -> i32\n    var v = 0\n    unsafe\n        v = (*p).n\n        v = (*p).n\n    return v\n",
        "GenerationResourceLimit_Kd (*p).n")]
    [InlineData("func f(p: ref/[300000000 of i64], i: isize) -> i64 => p[i]\n", "")]
    [InlineData("func f(p: raw/i64) -> raw/i64\n    var q = p\n    unsafe\n        q = p + 1\n    return q\n", "")]
    public void ALayoutNeedPastARootsStorageTakesItsTypesVerdict(string source, string records)
    {
        var c = MirBuildTest.Compile(source);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(records, string.Join("|", c.BuildMir().Records(MirRecordClass.Collection).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
    }

    // PLAN G157 (47) (DECISIONS R5-M149): a need past a pair layer is laid out only where the item's case makes the layer leave its root's
    // storage (InstanceCollector.LeavesStorage). `e@follow.1` reads Field 1 past e's layer, `(e@b0).1`: f's ref instance needs (S, i32),
    // whose S has no C layout, at the read; in the owner instance the layer is e's own storage, whose records are at e and g's y, and the
    // read adds none (the owner side). A read past a Deref and then the layer, `((*r)@b0).1` through `let r = e@ref`, leaves storage at the
    // Deref in every case, as `match e`'s `(*_4).1` does: the owner instance's record at `let k` too (G157 (47)'s finding, which lost it).
    [Theory]
    [InlineData("return e@follow.1", true, OwnerStorage)]
    [InlineData("return e@follow.1", false, "InvalidCLayout_Kd e@follow.1")]
    [InlineData("let r = e@ref\n    match r\n        (_, let k) => return k", true, OwnerStorage + "|InvalidCLayout_Kd let k")]
    [InlineData("match e\n        (_, let k) => return k", true, OwnerStorage + "|InvalidCLayout_Kd let k")]
    public void APairLayerNeedIsLaidOutWhereItsCaseLeavesStorage(string read, bool owner, string records)
    {
        var g = owner ? "x: i32, y: (S, i32)) -> i32 => f(x, y@move)" : "x: ref/i32, y: ref/(S, i32)) -> i32 => f(x, y)";
        var c = MirBuildTest.Compile("#Layout(\"C\")\nstruct S\n    public var n: i32\n    public var u: ()\nfunc f<s/T>(a: s/T, e: s/(S, i32)) -> i32\n" +
            $"    s is value or valueborrow\n    {read}\nfunc g({g}\nlet zz = 1\n");
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(records, string.Join("|", c.BuildMir().Records(MirRecordClass.Collection).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
    }

    // SPEC 8.15.3, 23.2.2 (PLAN G157 (47)'s finding): where the read past a Deref and then the layer, `((*r)@b0).1`, is the only use of
    // (S, i32), whose S has no C layout (h passes a borrow, so f's owner instance stores none), its record rejects the program, which
    // was accepted while the pair layer made that read conditional on its case.
    [Fact]
    public void AReadPastADerefAndAPairLayerIsAUseInEveryCase()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    public var n: i32\n    public var u: ()\n" +
            "func f<s/T>(m: s/T, r: ref/(s/(S, i32) during a) during b) -> i32\n    s is value or valueborrow\n    match r\n" +
            "        (_, let k) => return k\nfunc h(r: ref/(S, i32)) -> i32 => f(1, r)\nlet zz = 1\n");
        Assert.Equal("InvalidCLayout_Kd let k", MinimalEmissionTest.Problems(c));
        Assert.False(c.Accepts);
    }

    // R5-M149: the layout-need rows (the owner's list) and LayoutNeeds.TypeOf, the one function naming each need's Type in its body: a Field
    // step through a raw pointer its prefix, a raw pointer's displacement its pointee; a call's row its Type arguments.
    [Fact]
    public void EachLayoutNeedNamesItsTypeOnce()
    {
        var c = MirBuildTest.Compile("struct S\n    public var n: i32\nfunc f(p: raw/S) -> i32\n    var v = 0\n    unsafe\n        v = (*p).n\n" +
            "        let q = p + 1\n    return v\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var read = body.Statements.ToArray().Where(static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use })
            .Select(x => body.Operands[x.Rvalue.Start].Place).Single(x => x.Projection != 0 && body.Projections[x.Projection].Kind == MirProjectionKind.Field);
        Assert.Equal("S", TyCtxt.Name(LayoutNeeds.TypeOf(body, read)));
        var displacement = body.Statements.ToArray().Single(static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.BinaryOp });
        Assert.Equal(LayoutNeed.Element, LayoutNeeds.Of((MirBinaryOp)displacement.Rvalue.Op, body.TypeOf(body.Operands[displacement.Rvalue.Start].Place)!));
        Assert.Equal("S", TyCtxt.Name(LayoutNeeds.TypeOf(body, displacement.Rvalue)));
        MirProjectionKind[] kinds =
        [
            MirProjectionKind.Field, MirProjectionKind.Downcast, MirProjectionKind.Index, MirProjectionKind.ConstIndex, MirProjectionKind.Deref,
            MirProjectionKind.Base, MirProjectionKind.PairLayer,
        ];
        Assert.Equal(
            [LayoutNeed.Parts, LayoutNeed.Parts, LayoutNeed.Element, LayoutNeed.Element, LayoutNeed.None, LayoutNeed.None, LayoutNeed.None],
            kinds.Select(LayoutNeeds.Of));
        Assert.Equal(LayoutNeed.TypeArguments, IntrinsicRows.Of(IntrinsicId.RawAllocate).Need);
    }

    // SPEC 8.15.3 (PLAN G597): a keyed Storage is a site whose need is its key's in the item's instance (LayoutNeeds.Of(key)): the
    // instance whose key starts at the storage lays out nothing, the one whose key displaces its element (LayoutNeeds.TypeOf), here the
    // one use of S, whose C layout is unavailable. In the generic body the key is open and the row has no need, without a fault
    // (fail-before: no keyed Storage was built, and the displaced form made the stride a use of every instance).
    [Theory]
    [InlineData("h(v, ..)", "")]
    [InlineData("h(v, ..2)", "")]
    [InlineData("h(v, 1..)", "InvalidCLayout_Kd v[r]")]
    [InlineData("h(v, ..) + h(v, 1..)", "InvalidCLayout_Kd v[r]")]
    public void AKeyedStorageNeedsItsElementWhereItsInstancesKeyDisplaces(string calls, string records)
    {
        var c = MirBuildTest.Compile("#Layout(\"C\")\nstruct S\n    public var n: i32\n    public var u: ()\nfunc h<R>(v: ref/Array<S>, r: R) -> isize\n" +
            $"    R is PositionRange\n    return v[r].length\nfunc f(v: ref/Array<S>) -> isize => {calls}\nlet zz = 1\n");
        var program = c.BuildMir();
        _ = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(
            records,
            string.Join("|", program.Records(MirRecordClass.Collection).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "h");
        var keyed = body.Statements.ToArray().Single(static x => x is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Storage, Count: 2 } });
        Assert.Equal("S", TyCtxt.Name(LayoutNeeds.TypeOf(body, keyed.Rvalue)));
        Assert.Null(LayoutNeeds.Of(body.Consts[body.Operands[keyed.Rvalue.Start + 1].Arg].Type));
    }

    // K 4.10 (DECISIONS R5-M149, the owner's Q1 (a)): a root constructor whose Type has no layout, an unavailable C layout or a size over the
    // limit, is called by no program that builds, so a Library does not generate it (checked only, no record); its twin is generated.
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S\n    public init() => ()\n", false)]
    [InlineData(BigStruct + "    public init(n: i32)\n        self.a = [300000000 of 0]\n        self.n = n\n", false)]
    [InlineData("struct S\n    public init() => ()\n", true)]
    public void AConstructorWithoutAReceiverLayoutIsCheckedOnly(string source, bool generated)
    {
        var c = MirBuildTest.Compile(source + "let zz = 1\n");
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var init = Assert.Single(Enumerable.Range(0, set.RootCount), x => program.Bodies[set.Items[x].BodyOrdinal].Locals[0].Role == MirLocalRole.Receiver);
        Assert.Equal(generated, collector.Generates(program, init));
    }
}
