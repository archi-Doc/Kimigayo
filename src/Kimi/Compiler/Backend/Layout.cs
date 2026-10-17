// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the storage layout of a closed Type in the windows-x64-v1 profile (IMPL 21.1; plan P7a-P7c; DECISIONS R5-M19): its size,
// alignment, stride, Field offsets and LLVM representation, for every generator (PD2: the old pool calls it, never a copy).
// Rows: IMPL 21.1.3's scalars, Unit, string, Never, borrows and raw pointers, object and function handles and Loan<T>, and the
// sequence handles (Slice, Array, Dictionary words). Struct, Tuple, fixed-array, enum, Closure and Function Item placement, with
// the Limit and CLayout results, come with P7b and P7c; Weak<S> comes with its operations (P41).
// Input: a closed Type. A borrow, raw pointer, handle or sequence is laid out without reading its referent or element Type.
// Output: the Type's ValueLowering, one shared record per representation (P7b makes it the OK/Limit/CLayout result); a Type
// without a row is an internal error.
// Invariants: deterministic; no call allocates (every row is a static record); Never is zero-sized, laid out as Unit (IMPL
// 21.1.3), while the old Emission keeps Never without a representation (WindowsLowering.GetValue's gate), so legacy .ll is
// unchanged.
// Must not read syntax, Origins, HIR, MIR or ownership state, decide destruction (the old pool keeps its callbacks until G8),
// lay out IMPL 21.1.3's nonnull Option before PLAN U8 (STATUS), or use LINQ.

using System.Globalization;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

/// <summary>The storage layout of closed Types (IMPL 21.1).</summary>
internal static class Layout
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

    // IMPL 21.1.3: Loan<T> stores nothing; it is the record of a struct without stored Fields.
    private static readonly ValueLowering Empty = new(new("{ [0 x i8], [0 x i8] }", 0, 1, 0, ReadOnlyMemory<int>.Empty), "{ [0 x i8], [0 x i8] }", "ptr");

    private static readonly Dictionary<BoundType, ValueLowering> Primitives = CreatePrimitives();

    /// <summary>Gets the layout and representation of a closed Type.</summary>
    /// <param name="type">A Type of one of the laid-out rows.</param>
    /// <returns>The shared representation record.</returns>
    internal static ValueLowering Of(BoundType type) => type.Kind switch
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
        BoundTypeKind.Constructed when type.Symbol?.LibraryDeclaration == KimiDeclarationId.Loan => Empty,
        _ => throw Outside(type),
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
}
