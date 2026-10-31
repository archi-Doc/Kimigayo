// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the physical representation records of the windows-x64-v1 profile (SPEC 21.1, 21.3.1; DECISIONS R5-M15): the storage
// layout of a concrete Type (TypeLayout), its computation and argument representation (ValueLowering) and a physical signature
// (FunctionAbi of AbiParameters) with its definition header. Code generation builds, compares and writes these records.
// Input: LLVM type text, sizes, alignments, offsets, parameter names and kinds, signature flags.
// Output: immutable records; a FunctionAbi's `define ... {` line, created once per linkage and cached.
// Invariants: a FunctionAbi is identified by reference, and its definition and every call to it use the same parameters.
// Must not take BoundType, syntax or MIR types: deciding a Type's record belongs to Layout and a signature's to Abi (DECISIONS R5-M19,
// R5-M58).

#pragma warning disable SA1402 // The physical representation records form one vocabulary.

using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>Storage layout of one concrete Type (SPEC 21.1): LLVM storage Type, size, alignment, stride and Field offsets.</summary>
internal sealed record TypeLayout(string StorageType, int Size, int Alignment, int Stride, ReadOnlyMemory<int> FieldOffsets);

/// <summary>Computation representation and internal-ABI argument form (SPEC 21.3.1); a null ArgumentType omits the slot.</summary>
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

/// <summary>A parameter of a physical signature: its LLVM type, its name, its kind and the logical parameter it passes (-1: none). A Context
/// with a logical index is a measured capacity: the length of the fixed array that index's parameter receives, which a call writer measures
/// from that parameter's Type rather than being given it (DECISIONS R5-M128).</summary>
internal readonly record struct AbiParameter(string Type, string Name, AbiParameterKind Kind = AbiParameterKind.Value, int LogicalIndex = -1)
{
    internal string Attributes => this.Kind == AbiParameterKind.SharedReference ? " noundef nonnull align 8 dereferenceable(24)" : string.Empty;
}

/// <summary>A physical signature shared by definitions and calls (SPEC 21.3.1). ccc is LLVM's default; identity is by reference.</summary>
internal sealed class FunctionAbi(string name, string result, AbiParameter[] parameters)
{
    private string? internalDefinition;
    private string? exportedDefinition;

    internal string Name { get; } = name;

    internal string Result { get; } = result;

    internal AbiParameter[] Parameters { get; } = parameters;

    internal bool NoReturn { get; init; }

    internal bool ResultSlot { get; init; }

    internal bool CallerLocation { get; init; }

    /// <summary>Gets this signature under a name, for its definition and calls (<see cref="Abi.Of"/> gives none).</summary>
    /// <param name="name">The physical name.</param>
    /// <returns>A signature with the same parameters, result and flags.</returns>
    internal FunctionAbi Named(string name) => new(name, this.Result, this.Parameters)
    {
        NoReturn = this.NoReturn, ResultSlot = this.ResultSlot, CallerLocation = this.CallerLocation,
    };

    /// <summary>Gets the cached <c>define ... {</c> line, including the profile attribute group.</summary>
    /// <param name="exported">Whether the definition has external linkage instead of internal.</param>
    /// <returns>The definition header text.</returns>
    internal string GetDefinition(bool exported) => exported
        ? this.exportedDefinition ??= this.CreateDefinition(exported: true)
        : this.internalDefinition ??= this.CreateDefinition(exported: false);

    private string CreateDefinition(bool exported)
    {
        var text = new StringBuilder();
        text.Append(exported ? "define " : "define internal ").Append(this.Result).Append(" @").Append(this.Name).Append('(');
        for (var i = 0; i < this.Parameters.Length; i++)
        {
            if (i != 0)
            {
                text.Append(", ");
            }

            text.Append(this.Parameters[i].Type).Append(this.Parameters[i].Attributes).Append(" %").Append(this.Parameters[i].Name);
        }

        return text.Append(this.NoReturn ? ") noreturn #0 {\n" : ") #0 {\n").ToString();
    }
}
