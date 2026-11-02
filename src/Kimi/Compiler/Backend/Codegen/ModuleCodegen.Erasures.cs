// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The common Function values' entries of ModuleCodegen (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102, R5-M104, R5-M108), whose definitions
// FunctionCodegen.Erasures writes: a Function Type's erased signature (Erased, one of ModuleCodegen.Signatures.cs' unnamed signatures), an
// adapter item's entry, its target's named (Adapter), and its heap environment's release (Release); each kept, or computed once per Write
// through the per-index cache of ModuleCodegen.Signatures.cs, and the previous one reused while it still matches.
internal sealed partial class ModuleCodegen
{
    /// <summary>The name of an adapter item's entry before its index; its table adds <see cref="TableSuffix"/>, its release <c>_drop</c>.</summary>
    internal const string AdapterPrefix = "__kimi_erase";

    /// <summary>The name of an adapter item's table after its entry's.</summary>
    internal const string TableSuffix = "_table";

    private const Abi.Flags ErasedFlags = Abi.Flags.ErasedClosure | Abi.Flags.CallerLocation; // A common Function value's entry (Erased).

    /// <summary>Gets the erased signature of a Function Type, the one computation of it (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102, R5-M108
    /// as G157 (25) amends it): Abi.Of's internal convention with an i64 environment word first and the context last (ErasedClosure),
    /// taking the call's location (SPEC 22.5.1: a Kimi operation called through a function value reports that call); unnamed, kept across
    /// Writes (Unnamed). A call of a common Function value writes it for the value's Type, and an adapter's entry is it named
    /// (<see cref="Adapter"/>), so a call and the entry it reaches share one parameter list.</summary>
    /// <param name="function">The closed Function Type: its inputs' Tuple (or Unit) and its result.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Erased(BoundType function)
        => this.Unnamed(ErasedFlags, function.Components[1], (BoundType[])function.Components[0].Components);

    /// <summary>Gets the entry signature of a ClosureErasure or ItemAdapter item (IMPL 21.1.3, 21.3.1; DECISIONS R5-M108): the erased
    /// signature of its target Function Type (its key's, <see cref="Erased"/>), named <see cref="AdapterPrefix"/> with its index, so a
    /// Never-result source has one entry per target signature (SPEC 10.7); Abi reads no Origin, so the erased Type serves. Named once per
    /// Write, the previous name kept while it names the same parameters.</summary>
    /// <param name="item">The adapter item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Adapter(int item)
    {
        this.Requested(item);
        if (this.Stamped(ref this.signatures, ref this.signed, item) is { } current)
        {
            return current;
        }

        var erased = this.Erased(this.set.Items[item].Key.Function!);
        this.signed[item] = this.writes;
        return this.signatures[item] is { } previous && ReferenceEquals(previous.Parameters, erased.Parameters)
            ? previous : this.signatures[item] = erased.Named(AdapterPrefix + item.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Gets the release of an adapter item's heap environment, its table's drop entry (IMPL 21.1.3; DECISIONS R5-M104): a glue
    /// function's signature (<see cref="DropGlue.Parameters"/>), called at the address of a copy of the environment word (G182), named after
    /// its entry with <c>_drop</c>, kept across Writes.</summary>
    /// <param name="item">The adapter item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Release(int item)
        => this.Helper(item) ??= new(AdapterPrefix + item.ToString(CultureInfo.InvariantCulture) + "_drop", "void", DropGlue.Parameters);
}
