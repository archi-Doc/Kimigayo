// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The concrete layout/value/ABI vocabulary used by Windows lowering.

/// <summary>Storage layout of one concrete Type (SPEC 21.1): LLVM storage Type, size, alignment, stride and Field offsets.</summary>
internal sealed record TypeLayout(string StorageType, int Size, int Alignment, int Stride, ReadOnlyMemory<int> FieldOffsets);

/// <summary>Computation representation and internal-ABI argument form (SPEC 21.4.1); a null ArgumentType omits the slot.</summary>
internal sealed record ValueLowering(TypeLayout Layout, string ComputationType, string? ArgumentType);

internal readonly record struct AbiParameter(string Type, string Name);

/// <summary>The implemented windows-x64-v1 representations. Types without an entry have no fallback representation.</summary>
internal static class WindowsLowering
{
    /// <summary>The Static string releaseKind: compiler constant backing that is never freed (SPEC 22.5.5).</summary>
    internal const int StaticReleaseKind = 0;

    internal static readonly ValueLowering Unit = new(new("void", 0, 1, 0, ReadOnlyMemory<int>.Empty), "void", null);

    // { data, byteLength, releaseKind }: size/stride 24, alignment 8, offsets 0/8/16 (SPEC 22.5.5).
    internal static readonly ValueLowering String = new(new("%kimi.string", 24, 8, 24, new[] { 0, 8, 16 }), "%kimi.string", "ptr");

    internal static readonly FunctionAbi Entry = new("__kimi_entry_body", Unit.ComputationType, []);
    internal static readonly FunctionAbi Start = new(WindowsProfile.EntrySymbol, Unit.ComputationType, [], noReturn: true);
    internal static readonly FunctionAbi Exit = new("__kimi_exit", Unit.ComputationType, [new("i32", "code")], noReturn: true);

    // Hidden diagnostic context follows the ordinary parameters (SPEC 21.4.2, 22.5.1).
    internal static readonly FunctionAbi WriteLine = new("__kimi_write_line", Unit.ComputationType, [new(String.ArgumentType!, "text"), new("ptr", "location"), new("i64", "location_length")]);
    internal static readonly FunctionAbi DestroyString = new("__kimi_destroy_string", Unit.ComputationType, WriteLine.Parameters);

    /// <summary>Gets the compiler-facing runtime definitions expanded into WindowsRuntime.ll.in.</summary>
    internal static readonly FunctionAbi[] RuntimeDefinitions = [Exit, DestroyString, WriteLine];

    private static readonly Dictionary<BoundType, ValueLowering> Values = CreateValues();

    /// <summary>Gets a concrete representation, or null when the Type has no implemented representation.</summary>
    /// <param name="type">The complete Type.</param>
    /// <returns>The value lowering.</returns>
    internal static ValueLowering? GetValue(BoundType type)
        => Values.GetValueOrDefault(type);

    /// <summary>Gets the physical implementation of a compiler-provided Core function.</summary>
    /// <param name="kind">The compiler function identity.</param>
    /// <returns>The implementation ABI, or null when no body is generated.</returns>
    internal static FunctionAbi? GetCompilerFunction(CompilerFunctionKind kind)
        => kind == CompilerFunctionKind.WriteLine ? WriteLine : null;

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
