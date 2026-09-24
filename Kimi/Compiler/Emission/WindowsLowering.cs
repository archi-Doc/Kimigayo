// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The concrete layout/value/ABI vocabulary used by Windows lowering.

/// <summary>Storage layout of one concrete Type (SPEC 21.1): LLVM storage Type, size, alignment, stride and Field offsets.</summary>
internal sealed record TypeLayout(string StorageType, int Size, int Alignment, int Stride, ReadOnlyMemory<int> FieldOffsets);

/// <summary>Computation representation and internal-ABI argument form (SPEC 21.4.1); a null ArgumentType omits the slot.</summary>
internal sealed record ValueLowering(TypeLayout Layout, string ComputationType, string? ArgumentType);

internal enum AbiParameterKind : byte
{
    Value,
    OwnedSlot,
    SharedReference,
    ResultSlot,
    Location,
    LocationLength,
    Environment,
    Context,
}

internal readonly record struct AbiParameter(string Type, string Name, AbiParameterKind Kind = AbiParameterKind.Value, int LogicalIndex = -1)
{
    internal string Attributes => this.Kind == AbiParameterKind.SharedReference ? " noundef nonnull align 8 dereferenceable(24)" : string.Empty;
}

/// <summary>The implemented windows-x64-v1 representations. Types without an entry have no fallback representation.</summary>
internal static partial class WindowsLowering
{
    /// <summary>The Static string releaseKind: compiler constant backing that is never freed (SPEC 22.5.5).</summary>
    internal const int StaticReleaseKind = 0;

    internal static readonly ValueLowering Unit = new(new("void", 0, 1, 0, ReadOnlyMemory<int>.Empty), "void", null);

    // { data, byteLength, releaseKind }: size/stride 24, alignment 8, offsets 0/8/16 (SPEC 22.5.5).
    internal static readonly ValueLowering String = new(new("%kimi.string", 24, 8, 24, new[] { 0, 8, 16 }), "%kimi.string", "ptr");

    internal static readonly ValueLowering StringReference = new(new("ptr", 8, 8, 8, ReadOnlyMemory<int>.Empty), "ptr", "ptr");

    internal static readonly FunctionAbi Entry = new("__kimi_entry_body", Unit.ComputationType, []);
    internal static readonly FunctionAbi Start = new(WindowsProfile.EntrySymbol, Unit.ComputationType, [], noReturn: true);
    internal static readonly FunctionAbi Exit = new("__kimi_exit", Unit.ComputationType, [new("i32", "code")], noReturn: true);
    internal static readonly FunctionAbi Abort = new("__kimi_abort", Unit.ComputationType, [new("i32", "reason"), new("ptr", "location"), new("i64", "location_length"), new("i64", "os_error")], noReturn: true);

    // Hidden diagnostic context follows the ordinary parameters (SPEC 21.4.2, 22.5.1).
    internal static readonly AbiParameter[] OwnedStringParameters = [new(String.ArgumentType!, "text", AbiParameterKind.OwnedSlot, 0), new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)];

    // SPEC 22.4-22.5.5: writeLine borrows its string handle and releases nothing.
    internal static readonly FunctionAbi WriteLine = new("__kimi_write_line", Unit.ComputationType, [new(StringReference.ArgumentType!, "text", AbiParameterKind.SharedReference, 0), new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)]);
    internal static readonly FunctionAbi DestroyString = new("__kimi_destroy_string", Unit.ComputationType, OwnedStringParameters);

    // SPEC 4.7.4: an Array handle is {buffer, length, capacity}; construction zeroes it and destruction releases its buffer.
    internal static readonly AbiParameter[] ArrayHandleParameters = [new("ptr", "handle", AbiParameterKind.OwnedSlot, 0), new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)];
    internal static readonly FunctionAbi ArrayInit = new("__kimi_array_init", Unit.ComputationType, ArrayHandleParameters);
    internal static readonly FunctionAbi DictionaryInit = new("__kimi_dictionary_init", Unit.ComputationType, ArrayHandleParameters);
    internal static readonly FunctionAbi ArrayFree = new("__kimi_array_free", Unit.ComputationType, ArrayHandleParameters);

    // SPEC 4.7.4, 4.7.7: capacity routines move element bytes by stride and run no user code; the writer emits them only for modules that use Arrays.
    internal static readonly FunctionAbi ArrayGrow = new("__kimi_array_grow", Unit.ComputationType, [new("ptr", "handle"), new("i64", "stride"), new("i64", "minimum"), new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)]);
    internal static readonly FunctionAbi ArrayReserve = new("__kimi_array_reserve", Unit.ComputationType, [new("ptr", "handle"), new("i64", "stride"), new("i64", "additional"), new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)]);
    internal static readonly FunctionAbi DictionaryReserve = new("__kimi_dictionary_reserve", Unit.ComputationType, ArrayReserve.Parameters);
    internal static readonly FunctionAbi ArrayShrink = new("__kimi_array_shrink", Unit.ComputationType, [new("ptr", "handle"), new("i64", "stride"), new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)]);
    internal static readonly FunctionAbi DictionaryShrink = new("__kimi_dictionary_shrink", Unit.ComputationType, ArrayShrink.Parameters);
    internal static readonly FunctionAbi AbortMessage = new("__kimi_abort_message", Unit.ComputationType, OwnedStringParameters, noReturn: true);
    internal static readonly FunctionAbi TestTempDirectory = new("__kimi_test_temp", "void", [new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true);

    /// <summary>Gets the compiler-facing runtime definitions expanded into WindowsRuntime.ll.in.</summary>
    internal static readonly FunctionAbi[] RuntimeDefinitions = [Exit, DestroyString, WriteLine, Abort, AbortMessage, ArrayInit, ArrayFree];

    private static readonly Dictionary<BoundType, ValueLowering> Values = CreateValues();

    /// <summary>Gets a concrete representation, or null when the Type has no implemented representation.</summary>
    /// <param name="type">The complete Type.</param>
    /// <returns>The value lowering.</returns>
    internal static ValueLowering? GetValue(BoundType type)
        => ReferenceTypes.IsString(type) || ReferenceTypes.IsBorrow(type) || ReferenceTypes.IsPointer(type) ? StringReference : Values.GetValueOrDefault(type);

    /// <summary>Gets the physical implementation of a compiler-provided Kimi function.</summary>
    /// <param name="kind">The compiler function identity.</param>
    /// <returns>The implementation ABI, or null when no body is generated.</returns>
    internal static FunctionAbi? GetCompilerFunction(CompilerFunctionKind kind)
        => kind switch { CompilerFunctionKind.WriteLine => WriteLine, CompilerFunctionKind.Abort => AbortMessage, CompilerFunctionKind.TestTempDirectory => TestTempDirectory, _ => GetFormattingFunction(kind) };

    private static Dictionary<BoundType, ValueLowering> CreateValues()
    {
        // SPEC 21.1.4 scalar representations. Borrows, object handles and aggregates need their own records.
        var values = new Dictionary<BoundType, ValueLowering>
        {
            [BoundType.Unit] = Unit,
            [BoundType.String] = String,
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
        return values;

        static void AddScalar(Dictionary<BoundType, ValueLowering> values, string name, string llvm, int size)
            => values.Add(BoundType.Primitives[name], new(Scalar(llvm, size), llvm, llvm));

        static TypeLayout Scalar(string llvm, int size)
            => new(llvm, size, size, size, ReadOnlyMemory<int>.Empty);
    }
}
