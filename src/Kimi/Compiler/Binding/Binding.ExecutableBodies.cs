// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Binding's formation context (SPEC 15.6, 7.2.3, 6.3.3, 11.8; DECISIONS R4-M101 E1).
// - Responsibility: the executable body Binding is binding, as an ExecutableBody, recorded on what Binding forms there.
// - Inputs: the entries that start binding a body or a declaration (BindHeader, BindFunction, BindConcreteClosure, BindAccessorBody, a
//   Property's initializer, ValidateOriginRelations), each through EnterBody.
// - Outputs: BindingObligation.Body (AddObligation), BoundOrigin.Body (OriginAtom, OpenOrigin; defined for non-declaration binders),
//   OriginDeclaration.Body (BeginOriginDeclaration), BoundClosure.EvaluatingBody (BindConcreteClosure), and ParameterVisible's answer.
// - Invariants: every entry restores the context it found; a declaration's own syntax (a signature, a declared Property Type, a
//   declaration clause) is bound with no body; a Closure's header and captures belong to its evaluating body.
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

    internal BindingSymbol? Definition { get; }

    /// <summary>Gets a default template's parameter index, or a module's ordinal for its top-level statements; -1 otherwise.</summary>
    internal int Index => this.index - 1;

    /// <summary>Gets a value indicating whether this names a body, not a declaration.</summary>
    internal bool IsBody => this.Definition is not null || this.index != 0;
}

public sealed partial class Binding
{
    private ExecutableBody formingBody;

    // A Closure's evaluating body (SPEC 15.3.4), recorded on its plan when it is bound; no body for any other.
    private static ExecutableBody EvaluatingBody(ExecutableBody body)
        => body.Definition?.Declaration is FunctionKoto { IsAnonymous: true, ClosureStorage: { } plan } ? plan.EvaluatingBody : default;

    // A function's own body; a module's top-level statements have no symbol and are named by the module's ordinal.
    private static ExecutableBody FunctionBody(FunctionKoto function, BindingSymbol? symbol)
        => symbol is null && function.IsGenerated ? new(null, function.SyntaxOwner!.Module) : new(symbol);

    // A Property's initializer (SPEC 11.8, 6.3.3): a static Property's own body, or for an instance Field the layer unit, the Field
    // initializers of its struct layer, which MIR builds into each constructor of that layer (DECISIONS R4-M101 E4).
    private static ExecutableBody InitializerBody(BindingSymbol property) => IsStatic(property) ? new(property) : LayerOf(property);

    // The layer unit of a struct's member (an instance Field or a constructor): its struct, the one body key of the layer's initializers.
    private static ExecutableBody LayerOf(BindingSymbol member) => new(member.Scope.Owner.BoundSymbol);

    // SPEC 11.8: whether a Property is static storage, a group's (TypeContext's storage test).
    private static bool IsStatic(BindingSymbol? property) => property is { Kind: BindingSymbolKind.Property, Scope.Owner: GroupKoto };

    // Enters a body, or with the default value a declaration, and returns the context it replaces, which the entry restores.
    private ExecutableBody EnterBody(ExecutableBody body)
    {
        var saved = this.formingBody;
        this.formingBody = body;
        return saved;
    }

    // SPEC 7.2.3: in default i of f, or in a Closure its evaluation reaches, a parameter of f is visible only when it precedes i (a
    // constructor's or destructor's receiver, whose slot is no parameter index, never is); elsewhere every parameter in scope is.
    private bool ParameterVisible(BindingSymbol candidate)
    {
        if (candidate.Kind != BindingSymbolKind.Parameter || candidate.Scope.Owner is not FunctionKoto function)
        {
            return true;
        }

        for (var body = this.formingBody; body.IsBody; body = EvaluatingBody(body))
        {
            if (body.Index >= 0 && ReferenceEquals(body.Definition?.Declaration, function))
            {
                return (uint)candidate.Slot < (uint)body.Index;
            }
        }

        return true;
    }
}
