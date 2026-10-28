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
        Assert.Equal(records, string.Join("|", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
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
        Assert.Equal(LayoutNeed.TypeArguments, LegacyRuntimeHelpers.Row(CompilerFunctionKind.Swap)!.Value.Need);
        Assert.Equal(LayoutNeed.None, LegacyRuntimeHelpers.Row(CompilerFunctionKind.ArrayAppend)!.Value.Need);
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
