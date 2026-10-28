// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The item signatures of ModuleCodegen (R5 package C 3.3, IMPL 21.3.1; DECISIONS R5-M65, R5-M89, R5-M102, R5-M106): a body item's from
// its own body, the one its definition and every call write; a DropGlue item's; a Builtin item's, its requirement's in its instance
// (DECISIONS R5-M126), and a PropertyWitness item's, its key's (InstanceCollector.StorageSignature; DECISIONS R5-M141); a StaticInit item's
// slot's ensure function; the per-index cache they share with the called imports' C signatures
// (ModuleCodegen.Imports.cs) and the common Function values' entries (ModuleCodegen.Erasures.cs); and the unnamed signatures of the
// calls that reach their callee through a pointer (Unnamed: a common Function value's entry, a virtual slot's, Slot): each computed once
// per Write, or kept, and the previous one reused while it still matches, so a warm module allocates nothing. A slot's and a Builtin
// item's signature reads TyCtxt.Signature, code generation's input for a virtual slot (package C 3.2), under the call's substitution, an
// Item's own (TyCtxt.SubstOfItem, a function of its Type) or none, each Type in the item's instance by InstanceCollector.TypeOf: no
// substitution is composed here (DECISIONS R5-M114).
internal sealed partial class ModuleCodegen
{
    /// <summary>The name of a body item's function before its index (<see cref="Signature"/>).</summary>
    internal const string FunctionPrefix = "__kimi_f";

    /// <summary>The name of a StaticInit item's slot before its index (SPEC 22.4.3); its state adds <see cref="StateSuffix"/>, its ensure
    /// function <c>_ensure</c>.</summary>
    internal const string StaticPrefix = "__kimi_static";

    /// <summary>The name of a static slot's state after its slot's.</summary>
    internal const string StateSuffix = "_state";

    /// <summary>The name of a Builtin item's function before its index (<see cref="Shim"/>).</summary>
    internal const string BuiltinPrefix = "__kimi_builtin";

    /// <summary>The name of a PropertyWitness item's function before its index (<see cref="Shim"/>).</summary>
    internal const string WitnessPrefix = "__kimi_witness";

    private readonly List<FunctionAbi> unnamed = []; // The unnamed signatures (Unnamed), kept across Writes.
    private FunctionAbi?[] helpers = []; // By adapter or StaticInit item, its helper function (Release, Ensure), kept across Writes.
    private FunctionAbi?[] signatures = []; // By item.
    private int[] signed = []; // By item, the Write whose signature signatures holds (0: none).
    private BoundType[] parameters = []; // A signature's parameter Types in slot order.

    /// <summary>Gets the signature of a body item from its own body (IMPL 21.3.1; DECISIONS R5-M89), the one its definition and every call of
    /// it write: under the internal convention, its result Type local 0's and its parameters its Arg locals' in slot order
    /// (FunctionCodegen.ArgumentPosition), in the item's instance, a caller-location library function's flag included (SPEC 22.5.1), a
    /// Closure body's environment Abi's [environment] (ConcreteClosure, SPEC 7.7.3; DECISIONS R5-M102), not a parameter slot; named
    /// <c>__kimi_f</c> with its index, computed once per Write and the previous one while it still matches.</summary>
    /// <param name="item">The item, a body item of a kind the frame writes.</param>
    /// <returns>The signature; null, recorded at the item's site, where the instance does not make a Type of its signature.</returns>
    internal FunctionAbi? Signature(int item)
    {
        var entry = this.set.Items[item];
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // Only a Generated item is requested (K11).
        }

        if (this.Stamped(ref this.signatures, ref this.signed, item) is { } current)
        {
            return current;
        }

        var body = this.program.Bodies[entry.BodyOrdinal];
        if (this.Inputs(item, body, out var count) is not { } result)
        {
            return null; // Recorded.
        }

        // SPEC 22.5.1: a caller-location library function receives its caller's location; the old generator's constructor and destructor
        // result slot is its own ABI choice, which local 0's Type replaces here (IMPL 21.3.1).
        var closure = FunctionCodegen.OrderOf(body.Key.Kind) == FunctionCodegen.ArgumentOrder.Environment;
        var flags = (KimiLibraryCatalog.RequiresCallerLocation(body.Key.Decl) ? Abi.Flags.CallerLocation : Abi.Flags.None) |
            (closure ? Abi.Flags.ConcreteClosure : Abi.Flags.None);
        return this.Define(item, FunctionPrefix, flags, result, this.parameters.AsSpan(0, count));
    }

    /// <summary>Gets the signature of a DropGlue item (package C 3.6), named <c>__kimi_drop</c> with its index: the previous one while it is
    /// a glue signature (its parameters <see cref="DropGlue.Parameters"/>).</summary>
    /// <param name="item">The item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Glue(int item)
    {
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // Only a Generated item is requested (K11).
        }

        if (item >= this.signatures.Length)
        {
            Array.Resize(ref this.signatures, Math.Max(item + 1, 2 * this.signatures.Length));
        }

        if (this.signatures[item] is { } previous && ReferenceEquals(previous.Parameters, DropGlue.Parameters))
        {
            return previous;
        }

        return this.signatures[item] = new("__kimi_drop" + item.ToString(CultureInfo.InvariantCulture), "void", DropGlue.Parameters);
    }

    /// <summary>Gets the signature a call through a virtual slot takes and every entry of the slot has (SPEC 6.4.1: an override keeps its
    /// original's inputs and result; IMPL 21.3.1; DECISIONS R5-M114): the internal convention, without flags (no virtual function is a
    /// caller-location library function), over the original's parameter and result Types (TyCtxt.Signature, package C 3.2's input) under the
    /// dispatching substitution, each in the item's instance (InstanceCollector.TypeOf); unnamed, since the call reaches an entry through its
    /// receiver's table (Unnamed).</summary>
    /// <param name="original">The slot's original.</param>
    /// <param name="subst">The dispatching substitution: a Virtual callee's.</param>
    /// <param name="item">The item whose instance the call is in.</param>
    /// <returns>The signature; null, recorded at the item's site, where the instance does not make a Type of it.</returns>
    internal FunctionAbi? Slot(BindingSymbol original, Subst? subst, int item)
        => this.Declared(original, subst, item, out var count) is { } result
            ? this.Unnamed(Abi.Flags.None, result, this.parameters.AsSpan(0, count)) : null;

    /// <summary>Gets the signature a virtual function's Item's call takes (<see cref="Slot(BindingSymbol, Subst, int)"/>), under the Item's own
    /// substitution, the pure one its Type makes (TyCtxt.SubstOfItem; DECISIONS R5-M43).</summary>
    /// <param name="function">The Item's Type in the item's instance.</param>
    /// <param name="item">The item whose instance the call is in.</param>
    /// <returns>The signature, or null after a record.</returns>
    internal FunctionAbi? Slot(BoundType function, int item) => this.Slot(function.Symbol!, this.types.SubstOfItem(function), item);

    /// <summary>Gets the ensure function of a StaticInit item's slot (SPEC 22.4.3; DECISIONS R5-M106): void, taking the access's location
    /// (SPEC 22.5.1), named after its slot with <c>_ensure</c>, kept across Writes.</summary>
    /// <param name="item">The StaticInit item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Ensure(int item)
        => this.Helper(item) ??=
            new(StaticPrefix + item.ToString(CultureInfo.InvariantCulture) + "_ensure", "void", [RuntimeAbi.Location, RuntimeAbi.LocationLength]);

    /// <summary>Gets the signature of a shim item (PD3), by its kind's row: a Builtin item's, its function's requirement's in the item's
    /// instance, the substitution of the call that admitted it (InstanceCollector.TypeOf; DECISIONS R5-M126), named
    /// <see cref="BuiltinPrefix"/>; a PropertyWitness item's, the one its key fixes (InstanceCollector.StorageSignature; DECISIONS R5-M141),
    /// named <see cref="WitnessPrefix"/>. Each under the internal convention and caller-location (one ABI rule for every shim; SPEC 22.5.1:
    /// a failure inside it reports its invoking expression), the one every call of the item writes: a call under another substitution of
    /// the same key has the same Abi, since the key's Self erases to one Type, a reference is a pointer whatever its Origin and SPEC 13.3.3
    /// fixes a built-in numeric Type's counterpart and Output to itself. Computed once per Write and the previous one while it still
    /// matches.</summary>
    /// <param name="item">The Builtin or PropertyWitness item.</param>
    /// <returns>The signature; null, recorded at the item's site, where the instance does not make a Type of it.</returns>
    internal FunctionAbi? Shim(int item)
    {
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // Only a Generated item is requested (K11).
        }

        if (this.Stamped(ref this.signatures, ref this.signed, item) is { } current)
        {
            return current;
        }

        var key = this.set.Items[item].Key;
        var count = 0;
        var (prefix, result) = key.Kind == MonoKind.PropertyWitness
            ? (WitnessPrefix, this.Stored(item, out count)) : (BuiltinPrefix, this.Declared(key.Symbol!, null, item, out count));
        return result is null ? null : this.Define(item, prefix, Abi.Flags.CallerLocation, result, this.parameters.AsSpan(0, count));
    }

    // A function's result Type and its parameter Types in slot order into the scratch, each under a substitution (null: its declared Type) in
    // an item's instance (InstanceOf); null, recorded at the item's site, where the instance does not make one.
    private BoundType? Declared(BindingSymbol function, Subst? subst, int item, out int count)
    {
        _ = TyCtxt.Signature(function, null, int.MaxValue, out count);
        this.Reserve(count);
        var result = this.InstanceOf(function, subst, -1, item);
        for (var slot = 0; result is not null && slot < count; slot++)
        {
            if (this.InstanceOf(function, subst, slot, item) is not { } parameter)
            {
                return null; // Recorded.
            }

            this.parameters[slot] = parameter;
        }

        return result;
    }

    // A PropertyWitness item's result Type and its parameter Types in slot order into the scratch, the ones its key fixes
    // (InstanceCollector.StorageSignature); null, recorded at the item's site, where its representative Self stores no such Field, a broken
    // contract (the collector admitted the item on that Field, InstanceCollector.Agrees).
    private BoundType? Stored(int item, out int count)
    {
        if (this.collector.StorageSignature(item, -1, out count) is not { } result)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site);
            return null;
        }

        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            this.parameters[slot] = this.collector.StorageSignature(item, slot, out _)!; // The key's Field was found for the result.
        }

        return result;
    }

    // A body item's Callable inputs, its parameter Types in slot order into the scratch (its Arg locals' in its instance, by
    // FunctionCodegen.ArgumentPosition; a Closure body's environment, its Arg local 1, is no parameter slot), and its result Type, local 0's;
    // null, recorded at the item's site, where the instance does not make one (the collector laid out every local of an admitted item).
    private BoundType? Inputs(int item, MirBody body, out int count)
    {
        var site = this.set.Items[item].Site;
        count = FunctionCodegen.OrderOf(body.Key.Kind) == FunctionCodegen.ArgumentOrder.Environment ? -1 : 0;
        foreach (var local in body.Locals)
        {
            count += local.Kind == MirLocalKind.Arg ? 1 : 0;
        }

        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            if (this.collector.TypeOf(item, body.Locals[1 + FunctionCodegen.ArgumentPosition(body.Key, slot)].Type) is not { } parameter)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, site);
                return null;
            }

            this.parameters[slot] = parameter;
        }

        var result = this.collector.TypeOf(item, body.Locals[0].Type);
        if (result is null)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
        }

        return result;
    }

    // The head of the by-index caches of Signature, Adapter, ImportSignature and a descriptor's row (ModuleCodegen.Objects): the arrays
    // grown to hold the index, and the entry stamped in this Write (null: the caller computes it, then stamps it: Define for an item's
    // signature).
    private T? Stamped<T>(ref T?[] kept, ref int[] stamps, int index)
        where T : class
    {
        if (index >= kept.Length)
        {
            Array.Resize(ref kept, Math.Max(index + 1, 2 * kept.Length));
        }

        if (index >= stamps.Length)
        {
            Array.Resize(ref stamps, kept.Length);
        }

        return stamps[index] == this.writes ? kept[index] : null;
    }

    // The tail of an item's signature (Signature, Adapter): the item stamped for this Write, the previous Write's signature while it still
    // matches, else a new one named before the item's index (only then, so a warm module allocates nothing).
    private FunctionAbi Define(int item, string prefix, Abi.Flags flags, BoundType result, ReadOnlySpan<BoundType> types)
    {
        this.signed[item] = this.writes;
        if (this.signatures[item] is { } previous && Abi.Matches(previous, this.Layout, Abi.Convention.Internal, flags, result, types))
        {
            return previous;
        }

        var name = prefix + item.ToString(CultureInfo.InvariantCulture);
        return this.signatures[item] = Abi.Of(this.Layout, Abi.Convention.Internal, flags, result, types).Named(name);
    }

    // A function's parameter Type at a slot (at -1 its result) under a substitution, in an item's instance (Slot); null, recorded at the
    // item's site, where the instance does not make it.
    private BoundType? InstanceOf(BindingSymbol function, Subst? subst, int slot, int item)
    {
        if (TyCtxt.Signature(function, subst, slot, out _) is { } declared && this.collector.TypeOf(item, declared) is { } type)
        {
            return type;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // The collector laid out the call's Types (K 4.8).
        return null;
    }

    // An unnamed signature under the internal convention, which its calls reach through a pointer (a common Function value's entry,
    // ModuleCodegen.Erasures.cs; a virtual slot's, Slot): a kept one while Abi.Matches holds, else a new one, kept across Writes.
    private FunctionAbi Unnamed(Abi.Flags flags, BoundType result, ReadOnlySpan<BoundType> inputs)
    {
        foreach (var candidate in this.unnamed)
        {
            if (Abi.Matches(candidate, this.Layout, Abi.Convention.Internal, flags, result, inputs))
            {
                return candidate;
            }
        }

        var abi = Abi.Of(this.Layout, Abi.Convention.Internal, flags, result, inputs);
        this.unnamed.Add(abi);
        return abi;
    }

    // The kept helper function of an item (Release, Ensure: an item is an adapter or a StaticInit, never both), its array grown to hold it.
    private ref FunctionAbi? Helper(int item)
    {
        if (item >= this.helpers.Length)
        {
            Array.Resize(ref this.helpers, Math.Max(item + 1, 2 * this.helpers.Length));
        }

        return ref this.helpers[item];
    }

    // The scratch for a signature's parameter Types, grown to at least the count given.
    private void Reserve(int count)
    {
        if (this.parameters.Length < count)
        {
            this.parameters = new BoundType[Math.Max(count, 2 * this.parameters.Length)];
        }
    }
}
