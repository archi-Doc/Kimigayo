// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the identity of a MIR body (R3 decision 6): MirDefKey, by which MirProgram, the analyses and code generation name a body.
// Input: a definition's symbol, which body of it, and a default template's index or a module's ordinal. Output: the key.
// Invariants: one key names one body; a callee's symbol and its declaration name its body; R7's declaration table replaces the symbol.
// Must not name a body by syntax, HIR or a consumer's own map (MirProgram keeps the one map from keys to bodies).

namespace Kimi.Compiler.Mir;

/// <summary>The key of a MIR body (R3 decision 6): its definition's symbol (a Closure's own, an accessor's the function its calls
/// target), which body of it and the index of a default template (0 otherwise), so that a callee's symbol and its declaration name its
/// body. A module's top-level statements (SPEC 22.4.1) have no symbol; their index is the module's ordinal. R7's declaration table
/// replaces the symbol.</summary>
/// <param name="Decl">The definition, or null for a module's top-level statements.</param>
/// <param name="Kind">The body of the definition.</param>
/// <param name="Index">The index of a default template, or the module of top-level statements.</param>
internal readonly record struct MirDefKey(BindingSymbol? Decl, MirBodyKind Kind, int Index = 0);
