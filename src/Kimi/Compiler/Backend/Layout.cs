// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the storage layout of a closed Type in the windows-x64-v1 profile (IMPL 21.1; plan P7a-P7c; DECISIONS R5-M19, R5-M29,
// R5-M37): its size, alignment, stride, Field offsets and LLVM representation, for every generator (PD2: the old pool calls it,
// never a copy). One instance per compilation (Compilation.Layout) holds the placement records; IMPL 21.1.3's rows are static (Row).
// Rows: IMPL 21.1.3's scalars, Unit, string, Never, borrows and raw pointers, object and function handles, and the sequence handles
// (Slice, Array, Dictionary words). Placement (Layout.Placement.cs): structs (Loan<T> among them), Tuples, fixed arrays, enums,
// Closures and Function Items. P7c brings the Type memo, typed limits, the count bound and the depth and cycle guard (R5-M29), before
// any MIR caller; Weak<S> comes with its operations (P41).
// Input: a closed Type. A borrow, raw pointer, handle or sequence is laid out without reading its referent or element Type (X34).
// Output: a Result (S2-S4 X18): OK with the Type's ValueLowering; CLayout with the C-layout instance, at any depth, that stores a
// zero-sized Field; or Limit, when a size or offset exceeds 2^31 - 1. A Type without a row is an internal error.
// Invariants: deterministic; one record per representation, so a call that finds its records allocates nothing; the complete base
// is a prefix of a derived struct, its tail padding included (X1); Never is zero-sized, laid out as Unit (IMPL 21.1.3), while the
// old Emission keeps Never without a representation (WindowsLowering.GetValue's gate), so legacy .ll is unchanged. No Type memo
// until P7c: Binding keeps interned Types across passes and re-substitutes their stored Types, so the old pool memoizes per body.
// Must not read syntax (AdtDef.IsCLayout is the C-layout fact), Origins, HIR, MIR or ownership state, decide destruction (the old
// pool keeps its callbacks until G8), lay out IMPL 21.1.3's nonnull Option before PLAN U8 (STATUS), use LINQ, or be called
// concurrently on one instance.

using System.Globalization;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

/// <summary>The storage layout of one compilation's closed Types (IMPL 21.1).</summary>
internal sealed partial class Layout
{
    private const string Removal = "R8 declares the Slice, Array and Dictionary handles in the Kimigayo library, whose struct placement lays them out.";

    // SPEC 4.8.6, 4.9.5: a Slice is {buffer, length} and an Array handle {buffer, length, capacity}; a Dictionary handle holds seven
    // words. The buffer word is a pointer, the others are isize.
    [Transitional("R8", Reason = Removal)]
    private static readonly ValueLowering SliceHandle = Words(2);

    [Transitional("R8", Reason = Removal)]
    private static readonly ValueLowering ArrayHandle = Words(3);

    [Transitional("R8", Reason = Removal)]
    private static readonly ValueLowering DictionaryHandle = Words(7);

    // IMPL 21.1.3: an object handle is one pointer-sized word; a common function value is 16 bytes, its code and environment words.
    private static readonly ValueLowering ObjectHandle = new(new("[8 x i8]", 8, 8, 8, ReadOnlyMemory<int>.Empty), "[8 x i8]", "ptr");

    private static readonly ValueLowering FunctionHandle = new(new("[16 x i8]", 16, 8, 16, new[] { 0, 8 }), "[16 x i8]", "ptr");

    private static readonly Dictionary<BoundType, ValueLowering> Primitives = CreatePrimitives();

    /// <summary>Gets IMPL 21.1.3's static row of a Type, which the old scalar-value gate reads until G8.</summary>
    /// <param name="type">A scalar, Unit, string, Never, borrow, raw pointer, handle or sequence Type.</param>
    /// <returns>The row's shared record; a Type without a row is an internal error.</returns>
    internal static ValueLowering Row(BoundType type) => type.Kind switch
    {
        BoundTypeKind.Primitive => Primitives.GetValueOrDefault(type) ?? throw Outside(type),
        BoundTypeKind.Semantics => type.Semantics switch
        {
            // IMPL 21.1.3: a borrow or raw pointer is one pointer to its referent's storage, an object borrow one to the object.
            SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.Raw => RuntimeAbi.StringReference,
            SemanticsKind.ObjRef or SemanticsKind.ObjUniq => RuntimeAbi.StringReference,
            SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc => ObjectHandle,
            _ => throw Outside(type),
        },
        BoundTypeKind.Function => FunctionHandle,
        BoundTypeKind.Slice => SliceHandle,
        BoundTypeKind.Array => ArrayHandle,
        BoundTypeKind.Dictionary => DictionaryHandle,
        _ => throw Outside(type),
    };

    /// <summary>Gets the layout and representation of a closed Type of this compilation.</summary>
    /// <param name="type">A Type of one of the laid-out rows.</param>
    /// <returns>The shared representation record, or the Limit or CLayout result of a placement.</returns>
    internal Result Of(BoundType type) => type.Kind switch
    {
        BoundTypeKind.Tuple or BoundTypeKind.Closure or BoundTypeKind.FunctionItem => this.Place(type),
        BoundTypeKind.FixedArray => this.PlaceArray(type),
        _ when AdtDef.IsStruct(type) => this.Place(type),
        _ when AdtDef.IsEnum(type) => this.PlaceEnum(type),
        _ => new(Row(type), null),
    };

    private static InvalidOperationException Outside(BoundType type) => new("Layout has no row for the Type " + type.Name + ".");

    // Pointer-aligned words at consecutive offsets, the first being the buffer pointer.
    private static ValueLowering Words(int count)
    {
        var offsets = new int[count];
        for (var i = 0; i < count; i++)
        {
            offsets[i] = i * 8;
        }

        var storage = "{ [0 x i64], [" + (count * 8).ToString(CultureInfo.InvariantCulture) + " x i8] }";
        return new(new(storage, count * 8, 8, count * 8, offsets), storage, "ptr");
    }

    private static Dictionary<BoundType, ValueLowering> CreatePrimitives()
    {
        // IMPL 21.1.3: size, alignment and stride are equal; a bool is stored as i8 and computed as i1. Never is zero-sized like Unit.
        var values = new Dictionary<BoundType, ValueLowering>
        {
            [BoundType.Unit] = RuntimeAbi.Unit,
            [BoundType.Never] = RuntimeAbi.Unit,
            [BoundType.String] = RuntimeAbi.String,
            [BoundType.Boolean] = new(Scalar("i8", 1), "i1", "i1"),
        };

        AddScalar(values, "i8", "i8", 1);
        AddScalar(values, "u8", "i8", 1);
        AddScalar(values, "i16", "i16", 2);
        AddScalar(values, "u16", "i16", 2);
        AddScalar(values, "i32", "i32", 4);
        AddScalar(values, "u32", "i32", 4);
        AddScalar(values, "char", "i32", 4);
        AddScalar(values, "i64", "i64", 8);
        AddScalar(values, "u64", "i64", 8);
        AddScalar(values, "isize", "i64", 8);
        AddScalar(values, "usize", "i64", 8);
        AddScalar(values, "i128", "i128", 16);
        AddScalar(values, "u128", "i128", 16);
        AddScalar(values, "f32", "float", 4);
        AddScalar(values, "f64", "double", 8);

        // SPEC 3.1.1, 21.1.3: a wrapping integer Type has exactly the representation and ABI of its integer argument.
        foreach (var (integer, wrapping) in BoundType.WrappingScalars)
        {
            values.Add(wrapping, values[integer]);
        }

        return values;

        static void AddScalar(Dictionary<BoundType, ValueLowering> values, string name, string llvm, int size)
            => values.Add(BoundType.Primitives[name], new(Scalar(llvm, size), llvm, llvm));

        static TypeLayout Scalar(string llvm, int size)
            => new(llvm, size, size, size, ReadOnlyMemory<int>.Empty);
    }

    /// <summary>A Type's layout (S2-S4 X18): OK with its <see cref="Value"/>; CLayout with the C-layout <see cref="CLayout"/>
    /// instance, at any depth, that stores a zero-sized Field (SPEC 6.7.1, 8.15.3); or Limit, with neither, when a size or offset
    /// exceeds the generation limit (IMPL 21.3.2).</summary>
    /// <param name="Value">The shared representation record, when the Type is laid out.</param>
    /// <param name="CLayout">The C-layout instance without a layout.</param>
    internal readonly record struct Result(ValueLowering? Value, BoundType? CLayout);
}
