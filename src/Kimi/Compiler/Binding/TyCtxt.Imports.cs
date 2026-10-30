// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The import view for the Backend (SPEC 22.6, 8.15.3; DECISIONS R5-M150): the import declarations Binding checks (Binding.ImportDeclarations,
// forwarded unchanged) and the site of a slot of a signature, the written Type a fact about the slot names. Its one reader is the instance
// collector, which checks an import's parameters and result as by-value uses.
internal sealed partial class TyCtxt
{
    /// <summary>Gets the import declarations of the latest pass: each function whose signature Binding checks as an import's (SPEC 22.6),
    /// supported or not (<see cref="Binding.ImportDeclarations"/>).</summary>
    internal IReadOnlyList<BindingSymbol> Imports => binding.ImportDeclarations;

    /// <summary>Gets the site of a slot of a function's signature: a parameter's written Type, the written result Type at -1, else the
    /// declaration, the nodes the MIR builder locates a body's parameter and result locals at (DECISIONS R4-M90 D6, BU-48). It indexes the
    /// written parameters by the slot directly, so it holds only for a receiverless signature without special parameters, such as an
    /// import's (SPEC 22.6).</summary>
    /// <param name="function">The function.</param>
    /// <param name="slot">A parameter slot, or -1 for the result.</param>
    /// <returns>The site.</returns>
    internal static SourceRef SignatureSite(BindingSymbol function, int slot)
    {
        var declaration = (FunctionKoto)function.Declaration;
        return SourceRef.Of(slot >= 0 ? declaration.Parameters[slot].Type : (Koto?)declaration.ReturnType ?? declaration);
    }
}
