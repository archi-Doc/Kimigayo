// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// The static rows of Layout (IMPL 21.1.3; plan P7a): scalars, Unit, string, Never, borrows and raw pointers, object and function handles,
// and until R8 the Slice, Array and Dictionary handle words, each one shared record, which placement (Layout.Placement.cs) reads for a
// Type that is not placed and the old scalar-value gate reads through Row until G8; and an enum's tag (Tag), which placement places.
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

    /// <summary>Gets an enum's tag (IMPL 21.1.3, implementation-defined): the Case ordinal as an i32 at offset 0 of the enum's record,
    /// before its payload at Field 1, which placement places and code generation reads and writes.</summary>
    internal static ValueLowering Tag => Primitives[BoundType.I32];

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
