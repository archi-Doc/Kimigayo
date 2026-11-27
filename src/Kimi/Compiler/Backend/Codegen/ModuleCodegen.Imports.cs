// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

// The #LibraryImport functions of ModuleCodegen (SPEC 22.6, IMPL 21.2, 21.3.1; DECISIONS R5-M89): a called import's C signature, Abi.Of's
// C convention of the row (ImportConvention: Windows x64's C, or System V x86-64's with its argument extensions) over the import's own
// declared Types, named by its external symbol, so each call writes its own import's extensions (the Tatara plan's B7b2b, the owner's
// O-504 Q1 (a)), and its declaration request, one per symbol, whose Types the first called import's signature gives (Binding admits
// imports of one symbol only with an equal physical code); computed once per Write through the per-index cache of
// ModuleCodegen.Signatures.cs and the previous one reused while it still matches. What the written module states for its link (the
// generation result, Compilation.Generated; R9-B1): the external functions it declares (Externals), each with its providing Library
// (Binding's), the native libraries it requires (Libraries), which decide its start (IMPL 21.5.2: on the C path when they hold the row's
// C runtime), and the imports its runtime declares that no external names (RuntimeImports; LinkAmbiguity, T's ruling 32), each with its
// providing Library (the frame's; NamedProvider), each published here and nowhere re-derived (the toolchain reads no IR text).
internal sealed partial class ModuleCodegen
{
    private readonly List<(string Library, string Kind)> libraries = []; // The latest Write's required libraries (Libraries).
    private readonly List<(string Symbol, string Library)> runtimeImports = []; // The latest Write's runtime imports no external names (RuntimeImports).
    private readonly Abi.Convention importConvention; // The row's C convention of its imports (ImportConvention).
    private FunctionAbi?[] importSignatures = []; // By import.
    private int[] importSigned = []; // By import, the Write whose signature importSignatures holds (0: none).
    private string?[] importSymbols = []; // By import, the external symbol its signature is named by.

    /// <summary>Gets the external functions the latest written module declares, one per symbol in declaration order, each with its
    /// external symbol, dllimport form and providing library (SPEC 22.6, IMPL 21.2): the imports its definitions call, a kernel32 one the
    /// runtime declares included.</summary>
    internal IReadOnlyList<ExternalFunction> Externals => this.externals;

    /// <summary>Gets the native libraries the latest written module requires (SPEC 22.6, IMPL 20.4): the library and Kind of every
    /// validated import, called or not, one per library in import order, the reserved ones included; then, on a row with a C runtime
    /// (TargetProfile.CRuntime), that supply with its row Kind when any library is required and no import names it (IMPL 21.5.2).</summary>
    internal IReadOnlyList<(string Library, string Kind)> Libraries => this.libraries;

    /// <summary>Gets the imports the latest written module's runtime declares and no external of <see cref="Externals"/> names, each
    /// with its providing supply, in the row's order: the frame's runtime's, a test build's the test runtime's
    /// (LlvmModuleText.ModuleFrame), so the module's external declarations, these after <see cref="Externals"/>, name each symbol once
    /// (LinkAmbiguity, T's ruling 32; NamedProvider).</summary>
    internal IReadOnlyList<(string Symbol, string Library)> RuntimeImports => this.runtimeImports;

    /// <summary>Gets the C signature of a validated <c>#LibraryImport</c> function a definition calls (SPEC 22.6; Abi.Of's C convention
    /// of the row), its own import's, and requests its declaration, one per external symbol (DECISIONS R5-M89).</summary>
    /// <param name="function">The called function.</param>
    /// <returns>The signature, or null for a function without an import.</returns>
    internal FunctionAbi? Import(BindingSymbol function)
    {
        for (var index = 0; index < this.imports.Count; index++)
        {
            if (ReferenceEquals(this.imports[index].Decl, function))
            {
                var (abi, symbol) = (this.ImportSignature(index), this.imports[index].Symbol);
                if (!this.DeclaresExternal(symbol))
                {
                    this.externals.Add(new(abi, symbol, this.imports[index].Kind == "import", this.imports[index].Library));
                }

                return abi;
            }
        }

        return null;
    }

    // The C convention of a row's imports (IMPL 21.2), from its ArgumentExtension: none, Windows x64's; "SystemV", System V x86-64's.
    private static Abi.Convention ImportConvention(TargetProfile row) => row.ArgumentExtension switch
    {
        null => Abi.Convention.C,
        "SystemV" => Abi.Convention.SystemV,
        _ => throw new InvalidDataException("The target row's argument extension must be none or SystemV."),
    };

    // The libraries of every validated import, one per library (Libraries): an import's library is resolved when the program is built
    // whether or not a generated body calls it (SPEC 22.6), so the set is the declarations', not the calls'. On a row with a C runtime a
    // module that requires any library starts through it (IMPL 21.5.2; the Tatara plan's decision 21), so the runtime's supply joins
    // with its row Kind, once.
    private void Require()
    {
        this.libraries.Clear();
        for (var index = 0; index < this.imports.Count; index++)
        {
            if (!this.Requires(this.imports[index].Library))
            {
                this.libraries.Add((this.imports[index].Library, this.imports[index].Kind));
            }
        }

        if (this.libraries.Count != 0 && this.row.CRuntime is { } runtime && !this.Requires(runtime))
        {
            var supply = this.row.Supply(runtime) ?? throw new InvalidDataException("The target row's C runtime must be one of its supplies.");
            this.libraries.Add((runtime, supply.Kind));
        }
    }

    // The runtime's imports that no external names (RuntimeImports), after the module is assembled: a test build's frame then has a
    // test runtime (LlvmModuleText.WriteRuntime refuses one without).
    private void DeclareRuntime()
    {
        this.runtimeImports.Clear();
        var frame = LlvmModuleText.Frame(this.row);
        foreach (var import in (this.IsTestBuild ? frame.TestRuntimeImports : frame.RuntimeImports)!)
        {
            if (!this.DeclaresExternal(import.Symbol))
            {
                this.runtimeImports.Add(import);
            }
        }
    }

    // Whether the latest Write's externals name a symbol.
    private bool DeclaresExternal(string symbol)
    {
        foreach (var external in this.externals)
        {
            if (string.Equals(external.Symbol, symbol, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the latest Write's libraries hold a library.
    private bool Requires(string library)
    {
        foreach (var (name, _) in this.libraries)
        {
            if (string.Equals(name, library, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // The C signature of an import from its own declared Types (TyCtxt.Signature) under the row's C convention, named by its external
    // symbol; computed once per Write and the previous one while it still matches.
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
        var function = this.imports[index].Decl;
        var result = this.types.Signature(function, null, -1, out var count)!;
        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            this.parameters[slot] = this.types.Signature(function, null, slot, out _)!;
        }

        var types = this.parameters.AsSpan(0, count);
        if (this.importSignatures[index] is { } previous && string.Equals(this.importSymbols[index], symbol, StringComparison.Ordinal) &&
            Abi.Matches(previous, this.Layout, this.importConvention, Abi.Flags.None, result, types))
        {
            return previous;
        }

        this.importSymbols[index] = symbol;
        return this.importSignatures[index] = Abi.Of(this.Layout, this.importConvention, Abi.Flags.None, result, types).Named(LlvmModuleText.ExternalName(symbol));
    }
}
