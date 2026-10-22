// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The item signatures of ModuleCodegen (R5 package C 3.3, IMPL 21.3.1, SPEC 22.6; DECISIONS R5-M65, R5-M89, R5-M102): a body item's from its
// own body, the one its definition and every call write; a DropGlue item's; a called #LibraryImport function's C signature and its
// declaration request; a common Function value's entry under a Callable signature; each computed once per Write, or kept, and the previous
// one reused while it still matches, so a warm module allocates nothing.
internal sealed partial class ModuleCodegen
{
    private readonly List<FunctionAbi> erased = []; // The erased entries' signatures (Erased), kept across Writes.
    private FunctionAbi?[] signatures = []; // By item.
    private int[] signed = []; // By item, the Write whose signature signatures holds (0: none).
    private FunctionAbi?[] importSignatures = []; // By import.
    private int[] importSigned = []; // By import, the Write whose signature importSignatures holds (0: none).
    private string?[] importSymbols = []; // By import, the external symbol its signature is named by.
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

        if (item >= this.signatures.Length)
        {
            Array.Resize(ref this.signatures, Math.Max(item + 1, 2 * this.signatures.Length));
        }

        if (item >= this.signed.Length)
        {
            Array.Resize(ref this.signed, this.signatures.Length);
        }

        if (this.signed[item] == this.writes && this.signatures[item] is { } current)
        {
            return current;
        }

        var body = this.program.Bodies[entry.BodyOrdinal];
        var closure = FunctionCodegen.OrderOf(body.Key.Kind) == FunctionCodegen.ArgumentOrder.Environment;
        var count = closure ? -1 : 0; // A Closure body's environment, its Arg local 1, is no parameter slot.
        foreach (var local in body.Locals)
        {
            count += local.Kind == MirLocalKind.Arg ? 1 : 0;
        }

        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            if (this.collector.TypeOf(item, body.Locals[1 + FunctionCodegen.ArgumentPosition(body.Key, slot)].Type) is not { } parameter)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // The collector laid out every local of an admitted item.
                return null;
            }

            this.parameters[slot] = parameter;
        }

        if (this.collector.TypeOf(item, body.Locals[0].Type) is not { } result)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site);
            return null;
        }

        // SPEC 22.5.1: a caller-location library function receives its caller's location; the old generator's constructor and destructor
        // result slot is its own ABI choice, which local 0's Type replaces here (IMPL 21.3.1).
        var flags = (KimiLibraryCatalog.RequiresCallerLocation(body.Key.Decl) ? Abi.Flags.CallerLocation : Abi.Flags.None) |
            (closure ? Abi.Flags.ConcreteClosure : Abi.Flags.None);
        var types = this.parameters.AsSpan(0, count);
        this.signed[item] = this.writes;
        if (this.signatures[item] is { } previous && Abi.Matches(previous, this.Layout, Abi.Convention.Internal, flags, result, types))
        {
            return previous;
        }

        var name = "__kimi_f" + item.ToString(CultureInfo.InvariantCulture);
        return this.signatures[item] = Abi.Of(this.Layout, Abi.Convention.Internal, flags, result, types).Named(name);
    }

    /// <summary>Gets the C signature of a validated <c>#LibraryImport</c> function a definition calls (SPEC 22.6; Abi.Of's C convention)
    /// and requests its declaration, one per symbol (DECISIONS R5-M89).</summary>
    /// <param name="function">The called function.</param>
    /// <returns>The signature, or null for a function without an import.</returns>
    internal FunctionAbi? Import(BindingSymbol function)
    {
        for (var index = 0; index < this.imports.Count; index++)
        {
            if (ReferenceEquals(this.imports[index].Decl, function))
            {
                var abi = this.ImportSignature(index);
                foreach (var external in this.externals)
                {
                    if (ReferenceEquals(external.Abi, abi))
                    {
                        return abi;
                    }
                }

                this.externals.Add(new(abi, this.imports[index].Kind == "import"));
                return abi;
            }
        }

        return null;
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

    /// <summary>Gets the signature of a common Function value's entry under a Callable signature (IMPL 21.1.3, 21.3.1; DECISIONS R5-M102):
    /// Abi.Of's internal convention with an i64 environment word first and the context last (ErasedClosure), taking the call's location
    /// (SPEC 22.5.1: a Kimi operation called through a function value reports that call); unnamed, since a call reaches it through the
    /// value's table, and kept across Writes, the previous one reused while it matches.</summary>
    /// <param name="signature">The closed Callable signature, a Function Type: its inputs' Tuple (or Unit) and its result.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Erased(BoundType signature)
    {
        const Abi.Flags Flags = Abi.Flags.ErasedClosure | Abi.Flags.CallerLocation;
        ReadOnlySpan<BoundType> inputs = (BoundType[])signature.Components[0].Components;
        var result = signature.Components[1];
        foreach (var candidate in this.erased)
        {
            if (Abi.Matches(candidate, this.Layout, Abi.Convention.Internal, Flags, result, inputs))
            {
                return candidate;
            }
        }

        var abi = Abi.Of(this.Layout, Abi.Convention.Internal, Flags, result, inputs);
        this.erased.Add(abi);
        return abi;
    }

    // The C signature of an import from its declared Types (TyCtxt.Signature), named by its external symbol: the first import's of that
    // symbol, so imports of one symbol share one declaration (the old generator's rule); computed once per Write and the previous one
    // while it still matches.
    private FunctionAbi ImportSignature(int index)
    {
        if (index >= this.importSignatures.Length)
        {
            Array.Resize(ref this.importSignatures, Math.Max(index + 1, 2 * this.importSignatures.Length));
            Array.Resize(ref this.importSymbols, this.importSignatures.Length);
            Array.Resize(ref this.importSigned, this.importSignatures.Length);
        }

        if (this.importSigned[index] == this.writes && this.importSignatures[index] is { } current)
        {
            return current;
        }

        var symbol = this.imports[index].Symbol;
        this.importSigned[index] = this.writes;
        for (var first = 0; first < index; first++)
        {
            if (string.Equals(this.imports[first].Symbol, symbol, StringComparison.Ordinal))
            {
                this.importSymbols[index] = symbol;
                return this.importSignatures[index] = this.ImportSignature(first);
            }
        }

        var function = this.imports[index].Decl;
        var result = TyCtxt.Signature(function, null, -1, out var count)!;
        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            this.parameters[slot] = TyCtxt.Signature(function, null, slot, out _)!;
        }

        var types = this.parameters.AsSpan(0, count);
        if (this.importSignatures[index] is { } previous && string.Equals(this.importSymbols[index], symbol, StringComparison.Ordinal) &&
            Abi.Matches(previous, this.Layout, Abi.Convention.C, Abi.Flags.None, result, types))
        {
            return previous;
        }

        this.importSymbols[index] = symbol;
        return this.importSignatures[index] = Abi.Of(this.Layout, Abi.Convention.C, Abi.Flags.None, result, types).Named(LlvmModuleText.ExternalName(symbol));
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
