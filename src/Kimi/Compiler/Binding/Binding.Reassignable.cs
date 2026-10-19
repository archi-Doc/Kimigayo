// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns reassignability: whether a binding may be written again (SPEC 6.6, 14.6.1, 14.8.2, 7.7.2).
// Input: a declaration or a symbol. Output: the one answer Binding's write checks and the MIR builder's local and static mutability read.
// Computed from the declaration and the capture entry, no state (Q8 package §3.5, DECISIONS R5-M79); no copy of the rule elsewhere.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 6.6, 14.6.1, 14.8.2: var locals, var Pattern bindings and var iteration slots are reassignable.
    internal static bool IsMutableDeclaration(Koto? declaration)
        => declaration is VariableKoto { VariableKind: VariableKind.Var } or SyntaxFormKoto { IsMutablePattern: true } ||
            (declaration is IdentifierNameKoto { Parent: ForKoto loop } slot && loop.IsMutableSlot(slot));

    // A binding is reassignable where its declaration is (a static var Property is a VariableKoto), an environment binding also where
    // its capture entry made it mutable (SPEC 7.7.2).
    internal static bool Reassignable(BindingSymbol symbol) => symbol.MutableCapture || IsMutableDeclaration(symbol.Declaration);
}
