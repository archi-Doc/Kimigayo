// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// IMPL 21.1.3's rows and the sequence handles (SPEC 4.8.6, 4.9.5) in Layout.Of, which the old pool and gate read (R5 P7a), and
// struct, Tuple, fixed-array, enum, Closure and Function Item placement with its OK/Limit/CLayout result (R5 P7b, S2-S4 X1, X18, X31).
public class LayoutTest
{
    private const string CLayoutGeneric = "#Layout(\"C\")\nstruct P<T>\n    var n: i32\n    var value: T\n";

    [Theory]
    [InlineData("i8", "i8", "i8", 1)]
    [InlineData("u8", "i8", "i8", 1)]
    [InlineData("bool", "i8", "i1", 1)]
    [InlineData("i16", "i16", "i16", 2)]
    [InlineData("u16", "i16", "i16", 2)]
    [InlineData("i32", "i32", "i32", 4)]
    [InlineData("u32", "i32", "i32", 4)]
    [InlineData("char", "i32", "i32", 4)]
    [InlineData("f32", "float", "float", 4)]
    [InlineData("i64", "i64", "i64", 8)]
    [InlineData("u64", "i64", "i64", 8)]
    [InlineData("isize", "i64", "i64", 8)]
    [InlineData("usize", "i64", "i64", 8)]
    [InlineData("f64", "double", "double", 8)]
    [InlineData("i128", "i128", "i128", 16)]
    [InlineData("u128", "i128", "i128", 16)]
    public void ScalarsFollowTheProfileRows(string type, string storage, string computation, int size)
    {
        // IMPL 21.1.3: size, alignment and stride are equal; arguments pass the computation Type; the old gate reads the record.
        var value = Layout.Of(BoundType.Primitives[type]).Value!;
        Assert.Equal((storage, size, size, size, computation, computation), Row(value));
        Assert.Same(value, WindowsLowering.GetValue(BoundType.Primitives[type]));
    }

    [Fact]
    public void UnitStringAndNeverUseTheRuntimeRecords()
    {
        Assert.Same(RuntimeAbi.Unit, Layout.Of(BoundType.Unit).Value);
        Assert.Same(RuntimeAbi.String, Layout.Of(BoundType.String).Value);

        // IMPL 21.1.3: Never has size 0, alignment 1 and stride 0, laid out as Unit; the old gate keeps it without a record (X31).
        var never = Layout.Of(BoundType.Never).Value!;
        Assert.Same(RuntimeAbi.Unit, never);
        Assert.Equal(("void", 0, 1, 0, "void", null), Row(never));
        Assert.Null(WindowsLowering.GetValue(BoundType.Never));
    }

    [Fact]
    public void ANeverPayloadIsZeroSizedWhileTheOldPoolKeepsItsNull()
    {
        // SPEC 3.1.3: Option<Never> is valid. Layout lays the stored Never out as zero-sized (X31): the tag, then an empty payload
        // union; the old pool keeps its null (STATUS).
        var c = CompilationTestHelper.BindSuccess("let a: Option<Never> = .None");
        var option = Variable(c, "a");
        Assert.Same(BoundType.Never, option.Components[0]);
        var value = Layout.Of(option).Value!;
        Assert.Equal(("{ i32, { [0 x i8], [0 x i8] } }", 4, 4, 4, "{ i32, { [0 x i8], [0 x i8] } }", "ptr"), Row(value));
        Assert.Equal(new[] { 0, 4 }, value.Layout.FieldOffsets.ToArray());
        Assert.Null(new AggregateLayoutPool().Get(option));
    }

    [Theory]
    [InlineData("struct S\n    var a: u8\n    var b: i64\n    var c: u16", "S", "{ [0 x i64], [16 x i8] }", 16, 8, "10,0,8")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: u8\n    var b: u64\n    var c: u8", "S", "{ i8, i64, i8 }", 24, 8, "0,8,16")]
    [InlineData("", "(u8, i32, bool)", "{ [0 x i32], [8 x i8] }", 8, 4, "4,0,5")]
    [InlineData("", "(i32, ())", "{ [0 x i32], [4 x i8] }", 4, 4, "0,4")]
    [InlineData("", "[3 of i16]", "{ [0 x i16], [6 x i8] }", 6, 2, "")]
    [InlineData("", "[0 of i64]", "{ [0 x i64], [0 x i8] }", 0, 8, "")]
    [InlineData("", "Option<i32>", "{ i32, { [0 x i32], [4 x i8] } }", 8, 4, "0,4")]
    [InlineData("", "Option<i128>", "{ i32, { [0 x i128], [16 x i8] } }", 32, 16, "0,16")]
    [InlineData("", "Option<()>", "{ i32, { [0 x i8], [0 x i8] } }", 4, 4, "0,4")]
    public void PlacementGroupsByAlignmentOrKeepsTheWrittenCOrder(string declarations, string type, string storage, int size, int alignment, string offsets)
    {
        // IMPL 21.1.2: Kimigayo mode groups the parts by descending alignment, each group in written order (a zero-sized part last);
        // C mode keeps the written order; an enum is an i32 tag, then its payload union. The old pool's record is Layout's.
        var c = CompilationTestHelper.BindSuccess(declarations + "\nfunc probe(value: " + type + ") => ()");
        var probed = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!;
        var value = Layout.Of(probed).Value!;
        Assert.Equal((storage, size, alignment, size, storage, "ptr"), Row(value));
        Assert.Equal(offsets, string.Join(',', value.Layout.FieldOffsets.ToArray()));
        Assert.Same(value, new AggregateLayoutPool().Get(probed)!.Value);
    }

    [Fact]
    public void ClosuresAndFunctionItemsPlaceTheirStoredParts()
    {
        // A Closure stores its captures like a Tuple; a Function Item's Components are generic arguments, so it stores nothing.
        var c = CompilationTestHelper.BindSuccess("func g<T>() -> i32 => 1\nlet item = g<i64>\nlet x: u8 = 4\nlet y: i64 = 1\nlet f = func [x, y] () -> i64 => y");
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var environment = Layout.Of(closure).Value!;
        Assert.Equal(("{ [0 x i64], [16 x i8] }", 16, 8), (environment.Layout.StorageType, environment.Layout.Size, environment.Layout.Alignment));
        Assert.Equal(new[] { 8, 0 }, environment.Layout.FieldOffsets.ToArray());
        Assert.Same(environment, new AggregateLayoutPool().Get(closure)!.Value);

        var item = Variable(c, "item");
        Assert.Equal(BoundTypeKind.FunctionItem, item.Kind);
        Assert.Same(Layout.Of(c.Binding.SelfType(c.Library.GetSymbol(KimiDeclarationId.Loan)!)).Value, Layout.Of(item).Value);
    }

    [Theory]
    [InlineData("E", "{ [0 x i32], [16 x i8] }", 16, 4, "12")]
    [InlineData("D", "{ [0 x i64], [32 x i8] }", 32, 8, "24,16")]
    public void TheCompleteBaseIsAPrefixOfTheDerivedLayout(string derivedName, string storage, int size, int alignment, string offsets)
    {
        // S2-S4 X1: B places its C-layout Field (8 bytes, alignment 4) at 0 and t at 8, so its data ends at 9 and its size 12
        // includes 3 bytes of tail padding. E's u follows the complete base at 12 (in the padding it would be at 9, size 12); D's
        // Fields follow it in their groups (w at 16, u at 24). A derived struct has no C layout (SPEC 6.7.1), so the base precedes
        // only Kimigayo-mode Fields. The old pool's records are Layout's.
        var c = CompilationTestHelper.BindSuccess("#Layout(\"C\")\nstruct C\n    var x: u8\n    var y: i32\nopen struct B\n    var c: C\n    var t: u8\nstruct E: B\n    var u: u8\nstruct D: B\n    var u: u8\n    var w: i64\nlet p: raw/" + derivedName + " = null");
        var derived = Variable(c, "p").Components[0];
        var whole = Layout.Of(derived).Value!;
        var prefix = Layout.Of(AdtDef.Base(derived)!).Value!;
        Assert.Equal(("{ [0 x i32], [12 x i8] }", 12, 4), (prefix.Layout.StorageType, prefix.Layout.Size, prefix.Layout.Alignment));
        Assert.Equal(new[] { 0, 8 }, prefix.Layout.FieldOffsets.ToArray());
        Assert.Equal((storage, size, alignment), (whole.Layout.StorageType, whole.Layout.Size, whole.Layout.Alignment));
        Assert.Equal(offsets, string.Join(',', whole.Layout.FieldOffsets.ToArray()));

        var old = new AggregateLayoutPool().Get(derived)!;
        Assert.Same(whole, old.Value);
        Assert.Same(prefix, old.Base!.Value);
    }

    [Theory]
    [InlineData("let x: Option<P<()>> = .None")]
    [InlineData("let x: Option<P<Never>> = .None")]
    [InlineData("let x: Option<(i32, Q<P<[0 of u8]>>)> = .None")]
    public void ACLayoutInstanceWithAZeroSizedFieldHasNoLayoutAtAnyDepth(string use)
    {
        // S2-S4 X18, X31: the result names the C-layout instance that stores a zero-sized Field (a stored Never counts), however deep;
        // the old pool refuses the Type without a resource limit.
        var c = CompilationTestHelper.Parse(CLayoutGeneric + "struct Q<T>\n    var value: T\n" + use);
        c.Bind();
        var option = Variable(c, "x");
        var result = Layout.Of(option);
        Assert.Null(result.Value);
        Assert.Equal("P", result.CLayout!.Name);
        Assert.Equal(0, Layout.Of(result.CLayout.Components[0]).Value!.Layout.Size);
        var pool = new AggregateLayoutPool();
        Assert.Null(pool.Get(option));
        Assert.Null(pool.ResourceLimitFailure);
    }

    [Fact]
    public void AnInferredCLayoutInstanceHasNoLayout()
    {
        // S2-S4 X18: f<T> writes Option<P<T>>, valid for every T at the declaration; the instance f<()> stores P<()>.
        var c = CompilationTestHelper.BindSuccess(CLayoutGeneric + "func f<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = f(())\nlet s = f(1)");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()).Where(static x => x is { Target.Name: "f" }).ToArray();
        var declared = Variable(c, "x");
        var instances = calls.Select(x => c.Types.Apply(declared, c.Types.SubstOf(x!)!)!).ToArray();
        Assert.Equal([BoundType.Unit, BoundType.I32], instances.Select(static x => x.Components[0].Components[0]));
        Assert.Same(instances[0].Components[0], Layout.Of(instances[0]).CLayout);
        Assert.Equal(("{ i32, { [0 x i32], [8 x i8] } }", 12), (Layout.Of(instances[1]).Value!.Layout.StorageType, Layout.Of(instances[1]).Value!.Layout.Size));
    }

    [Theory]
    [InlineData("", "[2147483648 of u8]")]
    [InlineData("", "[1073741824 of i32]")]
    [InlineData("", "(bool, [2147483647 of u8])")]
    [InlineData("enum Big\n    A([2147483647 of u8])\n    B\n", "Big")]
    public void ASizeBeyondTheGenerationLimitIsALimit(string declarations, string type)
    {
        // IMPL 21.3.2: sizes and offsets are bounded by 2^31 - 1 (a size bound here; P7c types the count and depth bounds).
        var c = CompilationTestHelper.BindSuccess(declarations + "func probe(value: " + type + ") => ()");
        var result = Layout.Of(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData(SemanticsKind.Ref)]
    [InlineData(SemanticsKind.Uniq)]
    [InlineData(SemanticsKind.Raw)]
    [InlineData(SemanticsKind.ObjRef)]
    [InlineData(SemanticsKind.ObjUniq)]
    public void BorrowsAndRawPointersAreOnePointer(SemanticsKind semantics)
    {
        // IMPL 21.1.3: 8 / 8 / 8, whatever the referent.
        var value = Layout.Of(new BoundType("p", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.String])).Value!;
        Assert.Same(RuntimeAbi.StringReference, value);
        Assert.Equal(("ptr", 8, 8, 8, "ptr", "ptr"), Row(value));
    }

    [Theory]
    [InlineData(SemanticsKind.Obj)]
    [InlineData(SemanticsKind.Rc)]
    [InlineData(SemanticsKind.Arc)]
    public void ObjectHandlesAreOneWordForEveryPayload(SemanticsKind semantics)
    {
        var handle = new BoundType("h", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.I32]);
        var value = Layout.Of(handle).Value!;
        Assert.Equal(("[8 x i8]", 8, 8, 8, "[8 x i8]", "ptr"), Row(value));
        Assert.Same(value, Layout.Of(new BoundType("h", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.String])).Value);
        Assert.Same(value, new AggregateLayoutPool().Get(handle)!.Value);
    }

    [Fact]
    public void FunctionValuesAreTwoWords()
    {
        // IMPL 21.1.3: a common function value is 16 bytes with alignment 8: code and environment words.
        var function = new BoundType("fn", BoundTypeKind.Function, components: [BoundType.I32, BoundType.I32]);
        var value = Layout.Of(function).Value!;
        Assert.Equal(("[16 x i8]", 16, 8, 16, "[16 x i8]", "ptr"), Row(value));
        Assert.Equal(new[] { 0, 8 }, value.Layout.FieldOffsets.ToArray());
        Assert.Same(value, new AggregateLayoutPool().Get(function)!.Value);
    }

    [Theory]
    [InlineData(BoundTypeKind.Slice, 2)]
    [InlineData(BoundTypeKind.Array, 3)]
    [InlineData(BoundTypeKind.Dictionary, 7)]
    public void SequenceHandlesArePointerAlignedWordsForEveryElement(BoundTypeKind kind, int words)
    {
        // SPEC 4.8.6, 4.9.5: the buffer pointer, then the length and further words; the old pool's handle record is Layout's.
        BoundType[] components = kind == BoundTypeKind.Dictionary ? [BoundType.String, BoundType.I32] : [BoundType.I32];
        var sequence = new BoundType("s", kind, components: components);
        var value = Layout.Of(sequence).Value!;
        var storage = "{ [0 x i64], [" + (words * 8) + " x i8] }";
        Assert.Equal((storage, words * 8, 8, words * 8, storage, "ptr"), Row(value));
        Assert.Equal(Enumerable.Range(0, words).Select(static x => x * 8), value.Layout.FieldOffsets.ToArray());
        Assert.Same(value, Layout.Of(new BoundType("s", kind, components: [.. components.Select(static _ => BoundType.Boolean)])).Value);

        var layout = new AggregateLayoutPool().Get(sequence)!;
        Assert.Same(value, layout.Value);
        Assert.Equal(words, layout.Fields.Length);
    }

    [Fact]
    public void LoanIsPlacedAsAStructWithoutStoredFields()
    {
        // IMPL 21.1.3: Loan<T> has size 0, alignment 1 and stride 0, the one record of a struct without stored Fields.
        var c = CompilationTestHelper.BindSuccess("()");
        var loan = c.Binding.SelfType(c.Library.GetSymbol(KimiDeclarationId.Loan)!);
        var value = Layout.Of(loan).Value!;
        Assert.Equal(("{ [0 x i8], [0 x i8] }", 0, 1, 0, "{ [0 x i8], [0 x i8] }", "ptr"), Row(value));
        Assert.Equal(0, value.Layout.FieldOffsets.Length);
        Assert.Same(value, new AggregateLayoutPool().Get(loan)!.Value);
    }

    [Fact]
    public void ATypeWithoutARowIsAnInternalError()
        => Assert.Throws<InvalidOperationException>(() => Layout.Of(new BoundType("T", BoundTypeKind.Parameter)));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLayoutsAllocateNothing()
    {
        // Rows and placements of every mode and the Limit result: a call that finds its shared records allocates nothing.
        var c = CompilationTestHelper.BindSuccess(CLayoutGeneric + "open struct B\n    var a: i64\n    var b: u8\nstruct D: B\n    var c: u16\nfunc probe(d: D, p: P<u8>, o: Option<(i32, [3 of bool])>, l: [2147483648 of u8]) => ()");
        BoundType[] types =
        [
            BoundType.I32, BoundType.Never, BoundType.String, BoundType.WrappingOf(BoundType.ISize),
            new("p", BoundTypeKind.Semantics, semantics: SemanticsKind.Ref, components: [BoundType.I32]),
            new("h", BoundTypeKind.Semantics, semantics: SemanticsKind.Rc, components: [BoundType.I32]),
            new("fn", BoundTypeKind.Function, components: [BoundType.I32]),
            new("s", BoundTypeKind.Dictionary, components: [BoundType.String, BoundType.I32]),
            .. KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!),
        ];
        var size = 0L;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            foreach (var type in types)
            {
                size += Layout.Of(type).Value?.Layout.Size ?? 0;
            }
        });
        Assert.Equal(0, allocated);
        Assert.True(size > 0);
    }

    private static (string Storage, int Size, int Alignment, int Stride, string Computation, string? Argument) Row(ValueLowering value)
        => (value.Layout.StorageType, value.Layout.Size, value.Layout.Alignment, value.Layout.Stride, value.ComputationType, value.ArgumentType);

    private static BoundType Variable(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == name).NameKoto.TypeOf()!;
}
