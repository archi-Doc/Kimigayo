// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The common Function values' entries of ModuleCodegen (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102, R5-M104, R5-M108), whose definitions
// FunctionCodegen.Erasures writes: an entry under a Callable signature (Erased, one of ModuleCodegen.Signatures.cs' unnamed signatures), an
// adapter item's entry under its target's (Adapter) and its heap environment's release (Release); each kept, or computed once per Write
// through the per-index cache of ModuleCodegen.Signatures.cs, and the previous one reused while it still matches.
internal sealed partial class ModuleCodegen
{
    /// <summary>The name of an adapter item's entry before its index; its table adds <see cref="TableSuffix"/>, its release <c>_drop</c>.</summary>
    internal const string AdapterPrefix = "__kimi_erase";

    /// <summary>The name of an adapter item's table after its entry's.</summary>
    internal const string TableSuffix = "_table";

    private const Abi.Flags ErasedFlags = Abi.Flags.ErasedClosure | Abi.Flags.CallerLocation; // A common Function value's entry (Erased).

    /// <summary>Gets the signature of a common Function value's entry under a Callable signature (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102):
    /// Abi.Of's internal convention with an i64 environment word first and the context last (ErasedClosure), taking the call's location
    /// (SPEC 22.5.1: a Kimi operation called through a function value reports that call); unnamed, since a call reaches it through the
    /// value's table, and kept across Writes (Unnamed).</summary>
    /// <param name="signature">The closed Callable signature, a Function Type: its inputs' Tuple (or Unit) and its result.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Erased(BoundType signature)
        => this.Unnamed(ErasedFlags, signature.Components[1], (BoundType[])signature.Components[0].Components);

    /// <summary>Gets the entry signature of a ClosureErasure or ItemAdapter item (IMPL 21.1.3, 21.3.1; DECISIONS R5-M108): the erased
    /// signature (<see cref="Erased"/>'s form) of its target Function Type (its key's), the one every erased call of that Type writes, so a
    /// Never-result source has one entry per target signature (SPEC 10.7); Abi reads no Origin, so the erased Type serves. Named
    /// <see cref="AdapterPrefix"/> with its index, computed once per Write and the previous one while it still matches.</summary>
    /// <param name="item">The adapter item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Adapter(int item)
    {
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // Only a Generated item is requested (K11).
        }

        if (this.Stamped(ref this.signatures, ref this.signed, item) is { } current)
        {
            return current;
        }

        var function = this.set.Items[item].Key.Function!;
        return this.Define(item, AdapterPrefix, ErasedFlags, function.Components[1], (BoundType[])function.Components[0].Components);
    }

    /// <summary>Gets the release of an adapter item's heap environment, its table's drop entry (IMPL 21.1.3; DECISIONS R5-M104): a glue
    /// function's signature (<see cref="DropGlue.Parameters"/>), called at the address of a copy of the environment word (G182), named after
    /// its entry with <c>_drop</c>, kept across Writes.</summary>
    /// <param name="item">The adapter item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Release(int item)
        => this.Helper(item) ??= new(AdapterPrefix + item.ToString(CultureInfo.InvariantCulture) + "_drop", "void", DropGlue.Parameters);
}
