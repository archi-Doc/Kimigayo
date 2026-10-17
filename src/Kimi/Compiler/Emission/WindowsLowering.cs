// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

/// <summary>The old Emission's windows-x64-v1 representations of complete Types and implementations of compiler-provided functions.
/// Types without an entry have no fallback representation. The records and runtime ABI it returns are Backend's (DECISIONS R5-M15).</summary>
internal static partial class WindowsLowering
{
    private static readonly Dictionary<BoundType, ValueLowering> Values = CreateValues();

    /// <summary>Gets a concrete representation, or null when the Type has no implemented representation.</summary>
    /// <param name="type">The complete Type.</param>
    /// <returns>The value lowering.</returns>
    internal static ValueLowering? GetValue(BoundType type) => ReferenceTypes.IsString(type) || ReferenceTypes.IsBorrow(type) || ReferenceTypes.IsPointer(type)
        ? RuntimeAbi.StringReference : Values.GetValueOrDefault(type);

    /// <summary>Gets the physical implementation of a compiler-provided Kimi function.</summary>
    /// <param name="kind">The compiler function identity.</param>
    /// <returns>The implementation ABI, or null when no body is generated.</returns>
    internal static FunctionAbi? GetCompilerFunction(CompilerFunctionKind kind) => kind switch
    {
        CompilerFunctionKind.WriteLine => RuntimeAbi.WriteLine,
        CompilerFunctionKind.Abort => RuntimeAbi.AbortMessage,
        CompilerFunctionKind.TestTempDirectory => RuntimeAbi.TestTempDirectory,
        _ => GetFormattingFunction(kind),
    };

    private static Dictionary<BoundType, ValueLowering> CreateValues()
    {
        // SPEC 21.1.3 scalar representations. Borrows, object handles and aggregates need their own records.
        var values = new Dictionary<BoundType, ValueLowering>
        {
            [BoundType.Unit] = RuntimeAbi.Unit,
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
