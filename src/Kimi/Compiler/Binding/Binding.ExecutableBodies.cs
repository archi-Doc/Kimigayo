// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Binding's formation context (SPEC 15.6, 7.2.3, 6.3.3, 11.8; DECISIONS R4-M101 E1).
// - Responsibility: the executable body Binding is binding, as an ExecutableBody, recorded on what Binding forms there.
// - Inputs: the entries that start binding a body or a declaration (BindHeader, BindFunction, BindConcreteClosure, BindAccessorBody, a
//   Property's initializer, ValidateOriginRelations, and a local's declaration: BindVariable and BindType, PLAN G1369), each through
//   EnterBody, with the one value of each body (FunctionBody, a default template's, AccessorBody, InitializerBody).
// - Outputs: BindingObligation.Body (AddObligation), BoundOrigin.Body (OriginAtom, OpenOrigin; defined for non-declaration binders),
//   OriginDeclaration.Body (BeginOriginDeclaration), BindingSymbol.DeclaringBody (the index walk's: a Closure's, a named function's
//   and a local's, IndexNode and DeclareFunction).
// - The rule for a local (SPEC 15.3.4, 15.3.6; DECISIONS R8-M56): its declaration, its Type's Origin declaration and its initializer are
//   formed in its declaring body whoever demands them (FormingBodyOf: a use, a capture or a nested header binding them first), so every
//   atom, row and region they form is that body's. DeclaringBody is total for a local: the index walk enters every body Binding forms
//   locals in with the value Binding enters it with (a function's or a module's top-level statements', a default template, an accessor
//   body, a Property's initializer), and a local with none is an InternalInvariant.
// - Invariants: every entry restores the context it found; a declaration's own syntax (a signature, a declared Property Type, a
//   declaration clause) is bound with no body; a Closure's header and captures belong to its evaluating body. ExecutableBody.Declarations,
//   the declaration judge's frame (DECISIONS BU-89), is entered only by JudgeDeclarationRows; nothing is formed in it, only AddObligation
//   stamps it, on a relation formed in a body with a nested signature's per-call end (that signature's declaration row, R7-O3 U-O3), and
//   it is never a MIR key.
// - Prohibitions: no body derived from a scope chain, Use.Parent or syntax.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>An executable body in the form MIR keys it (<c>MirDefKey</c> without its kind): the definition's symbol, and the index a
/// default template (its parameter's) and a module's top-level statements (the module's ordinal; no symbol) need. The default value is no
/// body: Binding is binding a declaration (DECISIONS R4-M101 E1).</summary>
internal readonly record struct ExecutableBody
{
    // The index plus one, so that the default value is no body.
    private readonly int index;

    internal ExecutableBody(BindingSymbol? definition, int index = -1) => (this.Definition, this.index) = (definition, index + 1);

    /// <summary>Gets the declaration judge's frame (SPEC 15.3.4; DECISIONS BU-89): no body, distinct from the default value, in which
    /// every executable body's Origin a declaration's row names is a fixed Origin of that declaration.</summary>
    internal static ExecutableBody Declarations { get; } = new(null, -2);

    internal BindingSymbol? Definition { get; }

    /// <summary>Gets a default template's parameter index, or a module's ordinal for its top-level statements; -1 otherwise.</summary>
    internal int Index => this.index - 1;

    /// <summary>Gets a value indicating whether this names a body, not a declaration.</summary>
    internal bool IsBody => this.Definition is not null || this.index > 0;
}

public sealed partial class Binding
{
    private ExecutableBody formingBody;

    // SPEC 11.1, 11.8: whether a Property is static storage, a Property of a container whose members are static (the container's
    // published HasStaticMembersOnly, which Parsing reads for the static initializer; TypeContext's storage test).
    internal static bool IsStatic(BindingSymbol? property)
        => property is { Kind: BindingSymbolKind.Property, Scope.Owner: DeclarationContainerKoto { HasStaticMembersOnly: true } };

    // The body that declares `body`'s definition (SPEC 15.3.4), the one recorded fact BindingSymbol.DeclaringBody: a Closure's evaluating
    // body and a named function's declaring body, the body the index walk reaches it in (a root function's is its module's top-level
    // statements, SPEC 6.1.1); no body for any other.
    private static ExecutableBody EvaluatingBody(ExecutableBody body) => body.Definition is { } definition ? definition.DeclaringBody : default;

    // A function's own body; a module's top-level statements have no symbol and are named by the module's ordinal.
    private static ExecutableBody FunctionBody(FunctionKoto function, BindingSymbol? symbol)
        => symbol is null && function.IsGenerated ? new(null, function.SyntaxOwner!.Module) : new(symbol);

    // An accessor's own body (SPEC 11.3; DECISIONS R4-M101 E1).
    private static ExecutableBody AccessorBody(PropertyAccessorKoto accessor) => new(Accessor(accessor).SignatureSymbol);

    // A Property's initializer (SPEC 11.8, 6.3.3): a static Property's own body, or for an instance Field the layer unit, the Field
    // initializers of its struct layer, which MIR builds into each constructor of that layer (DECISIONS R4-M101 E4).
    private static ExecutableBody InitializerBody(BindingSymbol property) => IsStatic(property) ? new(property) : LayerOf(property);

    // The layer unit of a struct's member (an instance Field or a constructor): its struct, the one body key of the layer's initializers.
    private static ExecutableBody LayerOf(BindingSymbol member) => new(member.Scope.Owner.BoundSymbol);

    // Enters a body, or with the default value a declaration, and returns the context it replaces, which the entry restores.
    private ExecutableBody EnterBody(ExecutableBody body)
    {
        var saved = this.formingBody;
        this.formingBody = body;
        return saved;
    }

    // The body a declaration `owner` owns is formed in (the local rule above): a local's declaring body whoever demands it; the demander's
    // context for any other owner (a Property's own syntax with no body, BindHeader; a Type's, an alias's, a function's). Contract: the
    // index walk declares every local with its body (IndexNode); a local it did not declare, or declared with none, is a broken contract,
    // an InternalInvariant without recovery (one fault for both).
    private ExecutableBody FormingBodyOf(Koto? owner)
    {
        if (owner is not FieldKoto local)
        {
            return this.formingBody;
        }

        return this.symbols.TryGetValue(local, out var symbol) && symbol.DeclaringBody is { IsBody: true } body
            ? body : throw new InvalidOperationException("A local has no declaring body (InternalInvariant; PLAN G1369).");
    }
}
