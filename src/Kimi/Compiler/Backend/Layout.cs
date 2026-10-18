// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Layout, one instance per compilation (Compilation.Layout; IMPL 21.1; plan P7a-P7c; DECISIONS R5-M19, R5-M29, R5-M37).
// Responsibility: the size, alignment, stride, Field offsets and LLVM representation of a closed Type for every generator (PD2: the
// old pool calls it, never a copy), and its verdict: OK, a typed generation limit, the C-layout zero-sized rule anywhere in the
// 64-level window (S2-S4 X18), or a failure derived from Binding's rejected storage. Rows (static, Row): IMPL 21.1.3's scalars, Unit,
// string, Never, borrows and raw pointers, object and function handles, and the sequence handles (Slice, Array, Dictionary words).
// Placement (Layout.Placement.cs): structs (Loan<T> among them), Tuples, fixed arrays, enums, Closures and Function Items, memoized
// per program version; Weak<S> comes with its operations (P41).
// Inputs: a closed, interned Type of the owning compilation; AdtDef (stored Types, IsCLayout, InlineCycle); IMPL 21.1.3's rows. A
// borrow, raw pointer, handle or sequence is laid out without reading its referent or element Type (X34).
// Outputs: a Result (Outcome, Value, Instance); Row(type) for the old scalar-value gate until G8. A Type without a row is an internal
// error. The collector (InstanceCollector) alone maps the outcomes: a limit to a Resource record (K10a), CLayout to
// InvalidCLayout_Kd at the use (K10b), Rejected to a derived failure without a record; codegen after acceptance treats any outcome
// but Ok as a broken contract (InternalInvariant_Kd).
// After a failed Binding: Rejected for a declaration with InlineCycle, a null stored Field Type, null CaseTypes or a fixed array with
// Length < 0; an unresolved base, which Binding publishes as no base, is laid out without it (Ok; no record follows a failed Binding).
// Invariants: an answer depends only on the Type and the program version, never on query order; one record per representation
// within a program version; warm calls allocate 0 B; at most 65 placement levels per call and 65 memo entries per Type; the complete
// base is a prefix of a derived struct, its tail padding included (X1); Never is zero-sized, laid out as Unit (IMPL 21.1.3), while
// the old Emission keeps Never without a representation (WindowsLowering.GetValue's gate), so legacy .ll is unchanged.
// Must not read syntax (AdtDef.IsCLayout is the C-layout fact), Origins, HIR, MIR or ownership state, decide destruction (the old
// pool keeps its callbacks until G8), keep static mutable state, detect cycles itself (Binding's InlineCycle), decide derived
// failures (the collector's), lay out IMPL 21.1.3's nonnull Option before PLAN U8 (STATUS), use LINQ, be called concurrently on one
// instance, or be given another compilation's Types (their stored Types follow that compilation's edits).

using System.Globalization;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

/// <summary>The storage layout of one compilation's closed Types (IMPL 21.1).</summary>
internal sealed partial class Layout
{
    /// <summary>IMPL 21.3.2's bound on a Type's placement height: a row is 0 high; a struct, Tuple, Closure, Function Item, fixed
    /// array or enum 1 more than its highest base, Field, component, element or Case payload Tuple.</summary>
    internal const int MaximumHeight = 64;

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

    /// <summary>A Type's verdict (S2-S4 X18, refined by DECISIONS R5-M37).</summary>
    internal enum Outcome : byte
    {
        /// <summary>Laid out: the result's Value is its record.</summary>
        Ok,

        /// <summary>A size or offset exceeds 2^31 - 1 (IMPL 21.3.2).</summary>
        SizeLimit,

        /// <summary>A fixed array holds more than 2^31 - 1 elements, zero-sized ones included (IMPL 21.3.2).</summary>
        CountLimit,

        /// <summary>The placement height exceeds <see cref="MaximumHeight"/> and no CLayout lies in the window (IMPL 21.3.2).</summary>
        DepthLimit,

        /// <summary>The result's Instance, a C-layout instance placed in the window, stores a zero-sized Field (SPEC 6.7.1, 8.15.3).</summary>
        CLayout,

        /// <summary>The result's Instance is a struct, enum or fixed array whose storage Binding rejected: derived, never reported.</summary>
        Rejected,
    }

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

    /// <summary>Gets the layout and verdict of a closed Type of this compilation, memoized for the program version.</summary>
    /// <param name="type">A Type of one of the laid-out rows.</param>
    /// <returns>Ok with the representation's one record, or the Type's other outcome (precedence: CLayout in the window,
    /// DepthLimit, a limit, Rejected, the Type's own size).</returns>
    internal Result Of(BoundType type) => this.Walk(type, MaximumHeight).Result;

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

    /// <summary>A Type's layout and verdict: <see cref="Value"/> exactly for Ok, <see cref="Instance"/> exactly for CLayout and
    /// Rejected.</summary>
    /// <param name="Outcome">The verdict.</param>
    /// <param name="Value">The representation's one record, when the Type is laid out.</param>
    /// <param name="Instance">The C-layout instance storing a zero-sized Field, or the Type whose storage Binding rejected.</param>
    internal readonly record struct Result(Outcome Outcome, ValueLowering? Value, BoundType? Instance)
    {
        /// <summary>Gets a value indicating whether the outcome is a generation limit (IMPL 21.3.2), a Resource record's cause.</summary>
        internal bool IsLimit => this.Outcome is Outcome.SizeLimit or Outcome.CountLimit or Outcome.DepthLimit;
    }
}
