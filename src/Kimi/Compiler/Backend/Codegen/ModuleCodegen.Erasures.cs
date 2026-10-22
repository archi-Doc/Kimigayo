// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The common Function values' entries of ModuleCodegen (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102, R5-M104), whose definitions
// FunctionCodegen.Erasures writes: an entry under a Callable signature (Erased), an adapter item's entry (Adapter) and its heap
// environment's release (Release); each kept, or computed once per Write through the per-index cache of ModuleCodegen.Signatures.cs,
// and the previous one reused while it still matches.
internal sealed partial class ModuleCodegen
{
    /// <summary>The name of an adapter item's entry before its index; its table adds <see cref="TableSuffix"/>, its release <c>_drop</c>.</summary>
    internal const string AdapterPrefix = "__kimi_erase";

    /// <summary>The name of an adapter item's table after its entry's.</summary>
    internal const string TableSuffix = "_table";

    private const Abi.Flags ErasedFlags = Abi.Flags.ErasedClosure | Abi.Flags.CallerLocation; // A common Function value's entry (Erased).

    private readonly List<FunctionAbi> erased = []; // The erased entries' signatures (Erased), kept across Writes.

    /// <summary>Gets the signature of a common Function value's entry under a Callable signature (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102):
    /// Abi.Of's internal convention with an i64 environment word first and the context last (ErasedClosure), taking the call's location
    /// (SPEC 22.5.1: a Kimi operation called through a function value reports that call); unnamed, since a call reaches it through the
    /// value's table, and kept across Writes, the previous one reused while it matches.</summary>
    /// <param name="signature">The closed Callable signature, a Function Type: its inputs' Tuple (or Unit) and its result.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Erased(BoundType signature)
    {
        ReadOnlySpan<BoundType> inputs = (BoundType[])signature.Components[0].Components;
        var result = signature.Components[1];
        foreach (var candidate in this.erased)
        {
            if (Abi.Matches(candidate, this.Layout, Abi.Convention.Internal, ErasedFlags, result, inputs))
            {
                return candidate;
            }
        }

        var abi = Abi.Of(this.Layout, Abi.Convention.Internal, ErasedFlags, result, inputs);
        this.erased.Add(abi);
        return abi;
    }

    /// <summary>Gets the entry signature of a ClosureErasure or ItemAdapter item (IMPL 21.1.3, 21.3.1; DECISIONS R5-M104): the erased
    /// signature (<see cref="Erased"/>'s form) of its source's own Callable signature, its body target's inputs and result (a Closure body's
    /// environment not among them), named <see cref="AdapterPrefix"/> with its index, computed once per Write and the previous one while it
    /// still matches. Temporary until C5c keys an adapter by its target Function Type: an adapter without a body target has none.</summary>
    /// <param name="item">The adapter item.</param>
    /// <returns>The signature; null for an adapter without a body target, or, recorded, where the instance does not make its Types.</returns>
    internal FunctionAbi? Adapter(int item)
    {
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // Only a Generated item is requested (K11).
        }

        if (this.Stamped(ref this.signatures, ref this.signed, item) is { } current)
        {
            return current;
        }

        var targets = this.set.Targets(item);
        if (targets.Length == 0 || this.set.Items[targets[0].Item] is not { Key.Kind: MonoKind.Body, BodyOrdinal: var ordinal } ||
            this.Inputs(targets[0].Item, this.program.Bodies[ordinal], out var count) is not { } result)
        {
            return null;
        }

        return this.Define(item, AdapterPrefix, ErasedFlags, result, this.parameters.AsSpan(0, count));
    }

    /// <summary>Gets whether an adapter's entry is the erased signature of a Function Type, the one every erased call of that Type uses
    /// (<see cref="Erased"/>): a Never-result source erased into another result is not (SPEC 10.7; until C5c, DECISIONS R5-M104).</summary>
    /// <param name="entry">The adapter's entry (<see cref="Adapter"/>).</param>
    /// <param name="function">The closed Function Type the source is erased into.</param>
    /// <returns>Whether the signatures match.</returns>
    internal bool Fits(FunctionAbi entry, BoundType function)
    {
        ReadOnlySpan<BoundType> inputs = (BoundType[])function.Components[0].Components;
        return Abi.Matches(entry, this.Layout, Abi.Convention.Internal, ErasedFlags, function.Components[1], inputs);
    }

    /// <summary>Gets the release of an adapter item's heap environment, its table's drop entry (IMPL 21.1.3; DECISIONS R5-M104): a glue
    /// function's signature (<see cref="DropGlue.Parameters"/>) named after its entry with <c>_drop</c>, kept across Writes.</summary>
    /// <param name="item">The adapter item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Release(int item)
        => this.Helper(item) ??= new(AdapterPrefix + item.ToString(CultureInfo.InvariantCulture) + "_drop", "void", DropGlue.Parameters);
}
