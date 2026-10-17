// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// IMPL 21.1.3's rows and the sequence handles (SPEC 4.8.6, 4.9.5) in Layout.Of, which the old pool and gate read (R5 P7a).
public class LayoutTest
{
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
        var value = Layout.Of(BoundType.Primitives[type]);
        Assert.Equal((storage, size, size, size, computation, computation), Row(value));
        Assert.Same(value, WindowsLowering.GetValue(BoundType.Primitives[type]));
    }

    [Fact]
    public void UnitStringAndNeverUseTheRuntimeRecords()
    {
        Assert.Same(RuntimeAbi.Unit, Layout.Of(BoundType.Unit));
        Assert.Same(RuntimeAbi.String, Layout.Of(BoundType.String));

        // IMPL 21.1.3: Never has size 0, alignment 1 and stride 0, laid out as Unit; the old gate keeps it without a record (X31).
        var never = Layout.Of(BoundType.Never);
        Assert.Same(RuntimeAbi.Unit, never);
        Assert.Equal(("void", 0, 1, 0, "void", null), Row(never));
        Assert.Null(WindowsLowering.GetValue(BoundType.Never));
    }

    [Fact]
    public void OldPoolKeepsItsNullForANeverPayload()
    {
        // SPEC 3.1.3: Option<Never> is valid; the old pool keeps its null for the stored Never (STATUS), which Layout lays out as
        // zero-sized.
        var c = CompilationTestHelper.BindSuccess("func f(a: Option<Never>) -> i32 => 1");
        var f = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var option = f.Parameters[0].Type.TypeOf()!;
        Assert.Same(BoundType.Never, option.Components[0]);
        Assert.Null(new AggregateLayoutPool().Get(option));
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
        var value = Layout.Of(new BoundType("p", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.String]));
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
        var value = Layout.Of(handle);
        Assert.Equal(("[8 x i8]", 8, 8, 8, "[8 x i8]", "ptr"), Row(value));
        Assert.Same(value, Layout.Of(new BoundType("h", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.String])));
        Assert.Same(value, new AggregateLayoutPool().Get(handle)!.Value);
    }

    [Fact]
    public void FunctionValuesAreTwoWords()
    {
        // IMPL 21.1.3: a common function value is 16 bytes with alignment 8: code and environment words.
        var function = new BoundType("fn", BoundTypeKind.Function, components: [BoundType.I32, BoundType.I32]);
        var value = Layout.Of(function);
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
        var value = Layout.Of(sequence);
        var storage = "{ [0 x i64], [" + (words * 8) + " x i8] }";
        Assert.Equal((storage, words * 8, 8, words * 8, storage, "ptr"), Row(value));
        Assert.Equal(Enumerable.Range(0, words).Select(static x => x * 8), value.Layout.FieldOffsets.ToArray());
        Assert.Same(value, Layout.Of(new BoundType("s", kind, components: [.. components.Select(static _ => BoundType.Boolean)])));

        var layout = new AggregateLayoutPool().Get(sequence)!;
        Assert.Same(value, layout.Value);
        Assert.Equal(words, layout.Fields.Length);
    }

    [Fact]
    public void LoanStoresNothingLikeTheOldPoolsRecord()
    {
        // IMPL 21.1.3: Loan<T> has size 0, alignment 1 and stride 0: the record of a struct without stored Fields.
        var c = CompilationTestHelper.BindSuccess("()");
        var loan = c.Binding.SelfType(c.Library.GetSymbol(KimiDeclarationId.Loan)!);
        var value = Layout.Of(loan);
        var old = new AggregateLayoutPool().Get(loan)!.Value;
        Assert.Equal(("{ [0 x i8], [0 x i8] }", 0, 1, 0, "{ [0 x i8], [0 x i8] }", "ptr"), Row(value));
        Assert.Equal(Row(old), Row(value));
        Assert.Equal(0, old.Layout.FieldOffsets.Length + value.Layout.FieldOffsets.Length);
    }

    [Fact]
    public void ATypeWithoutARowIsAnInternalError()
        => Assert.Throws<InvalidOperationException>(() => Layout.Of(new BoundType("T", BoundTypeKind.Parameter)));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLayoutsAllocateNothing()
    {
        BoundType[] types =
        [
            BoundType.I32, BoundType.Never, BoundType.String, BoundType.WrappingOf(BoundType.ISize),
            new("p", BoundTypeKind.Semantics, semantics: SemanticsKind.Ref, components: [BoundType.I32]),
            new("h", BoundTypeKind.Semantics, semantics: SemanticsKind.Rc, components: [BoundType.I32]),
            new("fn", BoundTypeKind.Function, components: [BoundType.I32]),
            new("s", BoundTypeKind.Dictionary, components: [BoundType.String, BoundType.I32]),
        ];
        var size = 0L;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            foreach (var type in types)
            {
                size += Layout.Of(type).Layout.Size;
            }
        });
        Assert.Equal(0, allocated);
        Assert.True(size > 0);
    }

    private static (string Storage, int Size, int Alignment, int Stride, string Computation, string? Argument) Row(ValueLowering value)
        => (value.Layout.StorageType, value.Layout.Size, value.Layout.Alignment, value.Layout.Stride, value.ComputationType, value.ArgumentType);
}
