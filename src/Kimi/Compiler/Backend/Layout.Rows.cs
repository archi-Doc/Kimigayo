// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// The static rows of Layout (IMPL 21.1.3; plan P7a): scalars, Unit, string, Never, borrows and raw pointers, object and function handles,
// each one shared record, which placement (Layout.Placement.cs) reads for a Type that is not placed and code generation reads through Row
// (DECISIONS R5-M148); an enum's tag (Tag), which placement places; and IMPL 21.1.3's Handles table, a sequence handle's one component
// (Component), whose placement is the handle's record (R8-R1b).
internal sealed partial class Layout
{
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
    /// <param name="type">A scalar, Unit, string, Never, borrow, raw pointer, object handle or function Type.</param>
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
        _ => throw Outside(type),
    };

    /// <summary>Gets whether a Type is laid out without its component (X34; DECISIONS R5-M149): a borrow, a raw pointer, an object or
    /// function handle or a sequence handle, whose referent, element or entries lie outside its own storage, the boundary of a root's
    /// storage that LayoutNeeds' readers apply.</summary>
    /// <param name="type">A Type.</param>
    /// <returns>Whether its row stores a pointer to its component rather than the component.</returns>
    internal static bool Indirect(BoundType type) => type.Kind is BoundTypeKind.Function or BoundTypeKind.Slice or BoundTypeKind.Array or
        BoundTypeKind.Dictionary || type is { Kind: BoundTypeKind.Semantics, Semantics: not SemanticsKind.Owner };

    /// <summary>Gets IMPL 21.1.3's one component of a sequence handle, the Kimi Kotonoha's C-layout struct at the value's address: an
    /// Array's <c>Storage.ArrayRepr</c>, a Slice's <c>Storage.SliceRepr</c>, a Dictionary's <c>DictionaryStorage.Handle</c>, the one
    /// statement of the handle's words (a Len reads its <c>length</c>, SPEC 4.7.3). It is the declaration's own Type: its Fields are a raw
    /// pointer and isize words, so no element Type is read (X34) and each handle kind has one record; a Field that read the parameter
    /// would place it, Rejected, an internal fault (Walk), never a silent layout. A string's component, <c>Storage.StringRepr</c>, is not
    /// returned: the string keeps its static row, O-43 (A)'s lasting pair with <c>RuntimeAbi.String</c> (DECISIONS R8-M1).</summary>
    /// <param name="type">A Type.</param>
    /// <returns>The component's own Type, or null for any other Type, a string included.</returns>
    internal BoundType? Component(BoundType type) => Representation(type.Kind) is { } component ? this.types.SelfType(component) : null;

    private static InvalidOperationException Outside(BoundType type) => new("Layout has no row for the Type " + type.Name + ".");

    // IMPL 21.1.3's Handles table: a sequence handle kind's component declaration, which Component and placement (Handle) read.
    private static KimiDeclarationId? Representation(BoundTypeKind kind) => kind switch
    {
        BoundTypeKind.Array => KimiDeclarationId.ArrayRepr,
        BoundTypeKind.Slice => KimiDeclarationId.SliceRepr,
        BoundTypeKind.Dictionary => KimiDeclarationId.DictionaryStorageHandle,
        _ => null,
    };

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
