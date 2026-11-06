// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The import view for the Backend (SPEC 22.6, 8.15.3; DECISIONS R5-M150): the import declarations Binding checks (Binding.ImportDeclarations,
// forwarded unchanged) and the site of a slot of a signature, the written Type a fact about the slot names. Its two readers: the instance
// collector, which checks an import's parameters and result as by-value uses there, and the MIR builder, which locates a body's parameter
// and result locals there.
internal sealed partial class TyCtxt
{
    /// <summary>Gets the import declarations of the latest pass: each function whose signature Binding checks as an import's (SPEC 22.6),
    /// supported or not (<see cref="Binding.ImportDeclarations"/>).</summary>
    internal IReadOnlyList<BindingSymbol> Imports => binding.ImportDeclarations;

    /// <summary>Gets the site of a slot of a declaration's signature: a written parameter's Type, the written result Type at -1, else the
    /// declaration (an accessor, a result without a written Type), the nodes the MIR builder locates a body's parameter and result locals
    /// at and a fact about the slot names (DECISIONS R4-M90 D6, BU-48).</summary>
    /// <param name="declaration">The declaration.</param>
    /// <param name="slot">The index of a written parameter, or -1 for the result.</param>
    /// <returns>The site.</returns>
    internal static Koto SignatureSite(Koto declaration, int slot)
        => (slot >= 0 ? (declaration as FunctionKoto)?.Parameters[slot].Type : (declaration as FunctionKoto)?.ReturnType) ?? declaration;
}
