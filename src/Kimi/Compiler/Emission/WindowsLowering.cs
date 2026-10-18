// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

/// <summary>The old Emission's scalar-value gate and implementations of compiler-provided functions in windows-x64-v1. Types
/// outside the gate have no fallback representation. The records, layouts and runtime ABI it returns are Backend's (DECISIONS
/// R5-M15, R5-M19).</summary>
internal static partial class WindowsLowering
{
    /// <summary>Gets the representation of a Type the old generator passes as a scalar value (a primitive other than Never, a
    /// borrow it implements or a raw pointer), or null for any other Type. Layout decides the representation (DECISIONS R5-M19); Never stays
    /// without one here although Layout lays it out as zero-sized, so the old pool keeps its null for Never.</summary>
    /// <param name="type">The complete Type.</param>
    /// <returns>The value lowering.</returns>
    internal static ValueLowering? GetValue(BoundType type)
        => ReferenceTypes.IsValue(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) ? Layout.Of(type).Value : null;

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
}
