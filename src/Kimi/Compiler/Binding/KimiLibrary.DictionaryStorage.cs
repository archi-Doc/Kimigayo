// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The view's key belongs to the view.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The DictionaryStorage library functions (DictionaryStorage.kimi) the Backend names by symbol, the key of
/// <see cref="KimiLibrary.DictionaryStorage"/> (F2c, DECISIONS R5-M28); a legacy Dictionary helper row declares its callees in it
/// (X4), None where it has none. Reserve is the last member.</summary>
[Transitional("R8", Reason = KimiLibrary.DictionaryStorageRemoval)]
internal enum DictionaryCallee : byte
{
    None,

    // Generic over the entry Types, instantiated at them: a dictionary literal's find call (Binding.DictionaryStorageCall) and the
    // Dictionary drop helper's entry callee (InstanceCollector).
    FindEntry,
    ClearEntries,

    // Not generic: the placement helper's callee (append), a Dictionary's construction (initialize, LegacyRuntimeHelpers.Construction)
    // and requireAbsent, whose body the old analysis collects.
    RequireAbsent,
    Append,
    Initialize,

    // Called by no legacy text: tests read its symbol.
    Reserve,
}

public sealed partial class KimiLibrary
{
    internal const string DictionaryStorageRemoval = "R8 removes the legacy runtime text that calls these functions by name.";

    // The declared names, in DictionaryCallee order after None.
    private static readonly string[] DictionaryCalleeNames = ["findEntry", "clearEntries", "requireAbsent", "append", "initialize", "reserve"];

    private readonly BindingSymbol[] dictionaryCallees;

    /// <summary>Gets the symbol of a DictionaryStorage function, which names its definition the way <c>MirDefKey</c> does (DECISIONS
    /// R5-M25); None names no function, a broken contract.</summary>
    /// <param name="callee">The function.</param>
    /// <returns>The one symbol Binding declares for it.</returns>
    internal BindingSymbol DictionaryStorage(DictionaryCallee callee)
        => callee != DictionaryCallee.None && (int)callee <= this.dictionaryCallees.Length
            ? this.dictionaryCallees[(int)callee - 1]
            : throw new InvalidOperationException("A DictionaryStorage callee names no function.");

    // SPEC 22.1: each function is declared once in the DictionaryStorage group (KimiLibraryShapeTest). A cataloged one keeps its
    // catalog symbol; each other one gets its symbol here, registered so that Binding's indexer declares this same symbol.
    private BindingSymbol[] CreateDictionaryCallees(BindingScope scope)
    {
        var callees = new BindingSymbol[DictionaryCalleeNames.Length];
        for (var i = 0; i < callees.Length; i++)
        {
            var name = DictionaryCalleeNames[i];
            var declaration = FindDeclaration((DeclarationContainerKoto)scope.Owner, name, true)
                ?? throw new InvalidOperationException($"The embedded Kimi library has no unique declaration of DictionaryStorage.{name}.");
            BindingSymbol? symbol = null;
            foreach (var cataloged in this.symbols)
            {
                if (cataloged is not null && ReferenceEquals(cataloged.Declaration, declaration))
                {
                    symbol = cataloged;
                }
            }

            if (symbol is null)
            {
                symbol = new(name, BindingSymbolKind.Function, declaration, scope);
                this.registeredSymbols.Add(symbol);
            }

            callees[i] = symbol;
        }

        return callees;
    }
}
