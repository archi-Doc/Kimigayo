// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The old generator's signatures: its representation gate over the old layout pool, and the signatures of source functions
/// and imports, which <see cref="Abi"/> builds (DECISIONS R5-M58) and this pool reuses by physical shape, keeping no syntax.</summary>
internal sealed class FunctionAbiPool
{
    private readonly List<FunctionAbi> signatures = new();
    private readonly List<BoundType> parameters = new();

    // Passing mode and attributes are fixed by the implemented representation in this profile.
    // The pool caches physical shapes; current call plans separately validate complete Types and Origins.
    internal static bool Supports(BoundType? type, AggregateLayoutPool? layouts = null) => type is not null && GetValue(type, layouts) is not null;

    internal static ValueLowering? GetValue(BoundType type, AggregateLayoutPool? layouts) => type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Closure or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice || AdtDef.IsStruct(type) || AdtDef.IsEnum(type) || type.HandleMode is not null
        ? layouts?.Get(type)?.Value : WindowsLowering.GetValue(type);

    internal static bool HasResultSlot(BoundType type, AggregateLayoutPool? layouts) => GetValue(type, layouts) is { } value && Abi.HasResultSlot(type, value);

    internal static string? ResultType(BoundType type, AggregateLayoutPool? layouts = null) => ReferenceEquals(type, BoundType.Never)
        ? "void" : GetValue(type, layouts) is { } value ? Abi.ResultType(type, value) : null;

    /// <summary>Gets the signature of a selected source function, the previous one at its ordinal while that is still the same.</summary>
    /// <param name="ordinal">The function's ordinal, which names it.</param>
    /// <param name="function">A function whose signature Types the old gate admitted.</param>
    /// <param name="layout">The compilation's layout.</param>
    /// <returns>The signature named <c>__kimi_f</c> with the ordinal.</returns>
    internal FunctionAbi Get(int ordinal, FunctionKoto function, Layout layout)
    {
        // A closure body is the entry of a common Function value: its environment word comes first and the result slot second, as
        // every common call and erasure adapter passes them (LlvmModuleWriter.WriteValueCall).
        var environment = function.ClosureAsCreated?.EnvironmentType is null ? Abi.Flags.ErasedClosure : Abi.Flags.ConcreteClosure;
        var flags = (function.IsAnonymous ? environment : Abi.Flags.None) |
            (function.IsConstructor || function.IsDestructor ? Abi.Flags.ResultSlot : Abi.Flags.None) |
            (KimiLibraryCatalog.RequiresCallerLocation(function.BoundSymbol) ? Abi.Flags.CallerLocation : Abi.Flags.None);
        var result = function.BoundSymbol!.Type!;
        var parameters = this.Parameters(function);
        var abi = ordinal < this.signatures.Count && Abi.Matches(this.signatures[ordinal], layout, Abi.Convention.Internal, flags, result, parameters)
            ? this.signatures[ordinal]
            : Abi.Of(layout, Abi.Convention.Internal, flags, result, parameters).Named("__kimi_f" + ordinal.ToString(CultureInfo.InvariantCulture));
        this.parameters.Clear();
        if (ordinal == this.signatures.Count)
        {
            this.signatures.Add(abi);
        }
        else
        {
            this.signatures[ordinal] = abi;
        }

        return abi;
    }

    /// <summary>Gets the C signature of a validated import (SPEC 22.6), the previous one while that is still the same.</summary>
    /// <param name="previous">The import's previous signature, if any.</param>
    /// <param name="function">The import's declaration.</param>
    /// <param name="layout">The compilation's layout.</param>
    /// <param name="name">The declaration's external name.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Import(FunctionAbi? previous, FunctionKoto function, Layout layout, string name)
    {
        var result = function.BoundSymbol!.Type!;
        var parameters = this.Parameters(function);
        var abi = previous is not null && Abi.Matches(previous, layout, Abi.Convention.C, Abi.Flags.None, result, parameters)
            ? previous : Abi.Of(layout, Abi.Convention.C, Abi.Flags.None, result, parameters).Named(name);
        this.parameters.Clear();
        return abi;
    }

    // The declared parameter Types in a scratch list that every call reuses and clears after use, so a warm call allocates nothing and
    // the pool keeps no Type (nor the syntax its declarations hold).
    private ReadOnlySpan<BoundType> Parameters(FunctionKoto function)
    {
        this.parameters.Clear();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.parameters.Add(function.Parameters[i].Type.BoundType!);
        }

        return CollectionsMarshal.AsSpan(this.parameters);
    }
}
