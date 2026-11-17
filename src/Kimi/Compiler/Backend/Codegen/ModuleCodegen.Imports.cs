// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

// The #LibraryImport functions of ModuleCodegen (SPEC 22.6, IMPL 21.3.1; DECISIONS R5-M89): a called import's C signature, Abi.Of's
// C convention of its declared Types named by its external symbol, and its declaration request, one per symbol; computed once per Write
// through the per-index cache of ModuleCodegen.Signatures.cs and the previous one reused while it still matches. What the written module
// states for its link (the generation result, Compilation.Generated; R9-B1): the external functions it declares (Externals) and the
// native libraries it requires (Libraries), each published here and nowhere re-derived (the toolchain reads no IR text).
internal sealed partial class ModuleCodegen
{
    private readonly List<(string Library, string Kind)> libraries = []; // The latest Write's required libraries (Libraries).
    private FunctionAbi?[] importSignatures = []; // By import.
    private int[] importSigned = []; // By import, the Write whose signature importSignatures holds (0: none).
    private string?[] importSymbols = []; // By import, the external symbol its signature is named by.

    /// <summary>Gets the external functions the latest written module declares, one per symbol in declaration order, each with its
    /// external symbol and dllimport form (SPEC 22.6): the imports its definitions call, a kernel32 one the runtime declares included.</summary>
    internal IReadOnlyList<ExternalFunction> Externals => this.externals;

    /// <summary>Gets the native libraries the latest written module requires (SPEC 22.6, IMPL 20.4): the library and Kind of every
    /// validated import, called or not, one per library in import order; the reserved ones included.</summary>
    internal IReadOnlyList<(string Library, string Kind)> Libraries => this.libraries;

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

                this.externals.Add(new(abi, this.imports[index].Symbol, this.imports[index].Kind == "import"));
                return abi;
            }
        }

        return null;
    }

    // The libraries of every validated import, one per library (Libraries): an import's library is resolved when the program is built
    // whether or not a generated body calls it (SPEC 22.6), so the set is the declarations', not the calls'.
    private void Require()
    {
        this.libraries.Clear();
        for (var index = 0; index < this.imports.Count; index++)
        {
            var (library, kind) = (this.imports[index].Library, this.imports[index].Kind);
            var known = false;
            for (var j = 0; j < this.libraries.Count && !known; j++)
            {
                known = string.Equals(this.libraries[j].Library, library, StringComparison.Ordinal);
            }

            if (!known)
            {
                this.libraries.Add((library, kind));
            }
        }
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
