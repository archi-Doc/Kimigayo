// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

// The called #LibraryImport functions of ModuleCodegen (SPEC 22.6, IMPL 21.3.1; DECISIONS R5-M89): a called import's C signature, Abi.Of's
// C convention of its declared Types named by its external symbol, and its declaration request, one per symbol; computed once per Write
// through the per-index cache of ModuleCodegen.Signatures.cs and the previous one reused while it still matches.
internal sealed partial class ModuleCodegen
{
    private FunctionAbi?[] importSignatures = []; // By import.
    private int[] importSigned = []; // By import, the Write whose signature importSignatures holds (0: none).
    private string?[] importSymbols = []; // By import, the external symbol its signature is named by.

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

    // The C signature of an import from its declared Types (TyCtxt.Signature), named by its external symbol: the first import's of that
    // symbol, so imports of one symbol share one declaration (the old generator's rule); computed once per Write and the previous one
    // while it still matches.
    private FunctionAbi ImportSignature(int index)
    {
        if (this.Stamped(ref this.importSignatures, ref this.importSigned, index) is { } current)
        {
            return current;
        }

        if (this.importSymbols.Length < this.importSignatures.Length)
        {
            Array.Resize(ref this.importSymbols, this.importSignatures.Length);
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
}
