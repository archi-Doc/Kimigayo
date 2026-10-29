// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// The static rows of Layout (IMPL 21.1.3; plan P7a): scalars, Unit, string, Never, borrows and raw pointers, object and function handles,
// and until R8 the Slice, Array and Dictionary handle words, each one shared record, which placement (Layout.Placement.cs) reads for a
// Type that is not placed and code generation reads through Row (the old scalar-value gate too, until the old Emission goes at R6-gen;
// DECISIONS R5-M148); and an enum's tag (Tag), which placement places.
internal sealed partial class Layout
{
    private const string Removal = "R8 declares the Slice, Array and Dictionary handles in the Kimigayo library, whose struct placement lays them out.";

    // SPEC 4.7.3, 4.8.6, 4.9.5: the sequence handles, each its words and the word holding its length: a Slice is {buffer, length} and an
    // Array handle {buffer, length, capacity}, their length Field 1; a Dictionary handle holds seven words, DictionaryStorage.Handle's
    // {buffer, used, capacity, length, head, tail, free}, its live count Field 3 (DECISIONS R5-M124). The buffer word is a pointer, the others
    // are isize.
    [Transitional("R8", Reason = Removal)]
    private static readonly Handle[] Handles =
    [
        new(BoundTypeKind.Slice, Words(2), 1), new(BoundTypeKind.Array, Words(3), 1), new(BoundTypeKind.Dictionary, Words(7), 3),
    ];

    // IMPL 21.1.3: an object handle is one pointer-sized word; a common function value is 16 bytes, its code and environment words.
    private static readonly ValueLowering ObjectHandle = new(new("[8 x i8]", 8, 8, 8, ReadOnlyMemory<int>.Empty), "[8 x i8]", "ptr");

    private static readonly ValueLowering FunctionHandle = new(new("[16 x i8]", 16, 8, 16, new[] { 0, 8 }), "[16 x i8]", "ptr");

    private static readonly Dictionary<BoundType, ValueLowering> Primitives = CreatePrimitives();

    /// <summary>Gets an enum's tag (IMPL 21.1.3, implementation-defined): the Case ordinal as an i32 at offset 0 of the enum's record,
    /// before its payload at Field 1, which placement places and code generation reads and writes.</summary>
    internal static ValueLowering Tag => Primitives[BoundType.I32];

    /// <summary>Gets IMPL 21.1.3's static row of a Type, which code generation reads (and the old scalar-value gate until R6-gen). Of never
    /// asks it for an abstract Type (Layout.cs' Inputs; DECISIONS R5-M155); any other kind without a row lies outside Layout's input
    /// contract in every compilation, so the default arms stay a throw, the closed-kind fault.</summary>
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
        BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary => HandleOf(type).Words,
        _ => throw Outside(type),
    };

    /// <summary>Gets whether a Type is laid out without its component (X34; DECISIONS R5-M149): a borrow, a raw pointer, an object or
    /// function handle or a sequence handle, whose referent, element or entries lie outside its own storage, the boundary of a root's
    /// storage that LayoutNeeds' readers apply.</summary>
    /// <param name="type">A Type.</param>
    /// <returns>Whether its row stores a pointer to its component rather than the component.</returns>
    internal static bool Indirect(BoundType type) => type.Kind is BoundTypeKind.Function or BoundTypeKind.Slice or BoundTypeKind.Array or
        BoundTypeKind.Dictionary || type is { Kind: BoundTypeKind.Semantics, Semantics: not SemanticsKind.Owner };

    /// <summary>Gets the Field of a sequence handle's record that holds its length (SPEC 4.7.3, IMPL 21.1.3): its handle row's length word.</summary>
    /// <param name="sequence">A Slice, Array or Dictionary Type.</param>
    /// <returns>The Field's index in the handle's record; a Type without a handle is an internal error.</returns>
    internal static int LengthWord(BoundType sequence) => HandleOf(sequence).Length;

    private static InvalidOperationException Outside(BoundType type) => new("Layout has no row for the Type " + type.Name + ".");

    // The handle row of a sequence Type.
    private static Handle HandleOf(BoundType type)
    {
        foreach (var handle in Handles)
        {
            if (handle.Kind == type.Kind)
            {
                return handle;
            }
        }

        throw Outside(type);
    }

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

    // A sequence handle's row: its Type kind, its words and the Field holding its length.
    private readonly record struct Handle(BoundTypeKind Kind, ValueLowering Words, int Length);
}
