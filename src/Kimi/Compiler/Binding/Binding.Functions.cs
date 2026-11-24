// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The binding of a function definition: its header (BindHeader's function part, also bound lazily from a use), its signature, then its
// parts (defaults, base initializer, body); and SPEC 7.2.3's parameter positions, by the default of its function a node lies in
// (DefaultIndex, the one statement of it, also read by the index walk to enter a default template body and by the HIR's position of a
// default, HirPublisher.FormPosition, which records it in Hir.Defaults) and the parameters in a default's scope (ParameterInScope,
// also a default template body's roots, RootOf): which parameters a name there may select (ParameterVisible, the rule of every name
// lookup, a value's and a written Origin's), and that a parameter read in the default of a later parameter names the slot its call
// prepared (InLaterDefault, read by the capture diagnostics and the HIR tables, HirRes.PreparedParam).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Gets whether a node lies in the default of a later parameter of the function that declares a parameter, so that
    /// the parameter names the slot its call prepared, which the default can neither move nor keep a borrow of (SPEC 7.2.3).</summary>
    /// <param name="node">The node, such as a closure.</param>
    /// <param name="symbol">The parameter it reads.</param>
    /// <returns>Whether the node is in a later default of the parameter's function; never for a constructor's or destructor's receiver,
    /// whose slot is no parameter index (Binding.ReceiverSlot).</returns>
    internal static bool InLaterDefault(Koto node, BindingSymbol symbol) => InLaterDefault(node, symbol, out _);

    /// <summary>Gets the same, with the parents walked, a scaling check's work measure.</summary>
    /// <param name="node">The node.</param>
    /// <param name="symbol">The parameter it reads.</param>
    /// <param name="walked">The parents the answer read: the node's depth below the parameter's function, bounded by IMPL A.4's L.</param>
    /// <returns>Whether the node is in a later default of the parameter's function.</returns>
    internal static bool InLaterDefault(Koto node, BindingSymbol symbol, out int walked)
    {
        walked = 0;
        return symbol.Kind == BindingSymbolKind.Parameter && symbol.Scope.Owner is FunctionKoto declaration &&
            (uint)symbol.Slot < (uint)declaration.Parameters.Count && DefaultIndex(node, declaration, out walked) > symbol.Slot;
    }

    // SPEC 7.2.3 with 15.3.4 (the owner's D-1236 (A)): whether a name at `use` may select a parameter: in default i of its function,
    // whatever the default holds (a Closure, a named function's signature or body, a local's annotation), only one before i
    // (ParameterInScope); elsewhere every parameter in scope.
    private static bool ParameterVisible(BindingSymbol candidate, Koto use)
        => candidate.Kind != BindingSymbolKind.Parameter || candidate.Scope.Owner is not FunctionKoto function ||
            ParameterInScope(candidate.Slot, DefaultIndex(use, function, out _));

    // SPEC 7.2.3: whether parameter `slot` (j) of a function is in scope at `index` (i), the default of that function a position lies in:
    // in a default when j < i (a constructor's or destructor's receiver and a helper borrow, whose slots are no parameter index, never),
    // and every parameter where `index` < 0 (the function's signature or body). Read by a name's position (ParameterVisible) and by a
    // default template body's roots (RootOf, the body (f, i)).
    private static bool ParameterInScope(int slot, int index) => index < 0 || (uint)slot < (uint)index;

    // SPEC 7.2.3: the parameter of `function` whose default holds `node`, else -1 (its signature, its body, or outside it); `walked`
    // counts the parents read, the node's depth below the function, bounded by IMPL A.4's L.
    private static int DefaultIndex(Koto node, FunctionKoto function, out int walked)
    {
        walked = 0;
        Koto child = node;
        for (var parent = node.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            walked++;
            if (ReferenceEquals(parent, function))
            {
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (ReferenceEquals(function.Parameters[i].DefaultValue, child))
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        return -1;
    }

    private static bool HasParameterDefaults(FunctionKoto function)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].DefaultValue is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static Koto? IncompleteSignature(FunctionKoto function)
    {
        if (function.ReturnType is { BindingState: not BindingState.Resolved } result)
        {
            return result;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type is { BindingState: not BindingState.Resolved } type)
            {
                return type;
            }
        }

        return null;
    }

    // A function definition: its signature (header, receiver, generic arguments, Constraints, effect bounds, parameter and result
    // Types), bound as its declaration's (SPEC 15.6.1; DECISIONS R4-M101 E3), then its parts (BindFunctionParts).
    private BoundType? BindFunction(FunctionKoto function, BindingScope scope)
    {
        var symbol = this.symbols.GetValueOrDefault(function);
        var saved = this.EnterBody(default);
        try
        {
            if (symbol is not null)
            {
                this.BindHeader(symbol);
            }

            // The indexing pass only had the nominal shell. A constructor/destructor body sees the complete Self,
            // including its Type parameters and Origins, just as an explicit receiver does.
            if (this.SpecialReceiver(function) is { } receiver && symbol?.Scope.Owner.BoundSymbol is { Schema: not null } owner)
            {
                receiver.Type = this.DeclarationSelf(owner);
            }

            for (var i = 0; i < function.GenericArguments.Count; i++)
            {
                this.BindType(function.GenericArguments[i], scope);
            }

            for (var i = 0; i < function.TypeConstraints.Count; i++)
            {
                this.BindNode(function.TypeConstraints[i], scope);
            }

            for (var i = 0; i < function.EffectBounds.Count; i++)
            {
                this.BindNode(function.EffectBounds[i], scope);
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                this.ParameterSymbol(function, i).Type = this.BindType(function.Parameters[i].Type, scope);
            }

            // SPEC 15.3.4 (PLAN G230 (A)): the header may be bound before the body types a local its written Origins name; bound
            // again here, with that local bound, each parameter takes its Type and a result the header could not bind takes its own,
            // its `during r` naming r's region, which a named nested function's body reads as a fixed Origin through its declaring body.
            if (function.ReturnType is { } resultSyntax && symbol is { Type: null })
            {
                symbol.Type = this.BindType(resultSyntax, scope);
            }

            this.CheckCaseBound(function);
            this.BindFunctionParts(function, symbol, scope);
        }
        finally
        {
            this.formingBody = saved;
        }

        if (function.IsAnonymous || (function.IsSpecialization && !this.specializations.ContainsKey(function)))
        {
            return this.Fail(function, BindingFailure.Unsupported, true);
        }

        return Complete(function, function.IsGenerated ? BoundType.Unit : symbol?.Type);
    }

    // A function's parts (SPEC 7.2.3, 15.6): each default as its own template (f, i), then the base initializer and the body as f's body.
    private void BindFunctionParts(FunctionKoto function, BindingSymbol? symbol, BindingScope scope)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            if (parameter.DefaultValue is { } value)
            {
                this.formingBody = new(symbol, i);
                this.RequireType(value, scope, parameter.Type.BoundType);
            }
        }

        this.formingBody = FunctionBody(function, symbol);

        if (function.BaseInitializer is { } initializer)
        {
            this.BindNode(initializer, scope);
        }

        if (function.Body is { } body)
        {
            this.BindNode(body, scope);
        }

        if (function.ExpressionBody is { } expression)
        {
            var discards = DiscardsFunctionBody(function);
            var result = discards ? this.BindNode(expression, scope) : this.BindExpected(expression, scope, symbol?.Type, function.ReturnType);
            if (symbol is not null)
            {
                var structural = this.ResultStructure();
                // SPEC 15.3.4, 15.6.5 (R7-O3 O3-A): the result fit is at the value, inside the body, as a `return`'s is at its node: every
                // reading at the use (RegionValueAtUse, DeclaredAtUse) resolves in this function's own body, never in the declaring one.
                if (!discards && (KotoHelper.IsBodyExpression(expression) || structural.CanComplete(expression)) &&
                    symbol.Type is { } expected && result is not null && !this.FitsValueAt(result, expected, expression, expression, out var failure))
                {
                    this.FailMismatch(expression, expression, result, expected, failure);
                }
            }
        }

        this.JudgeHelperCaptures(function);
    }

    // A function's header (BindHeader's function part): its parameter and result Types under its Origin declaration, then its receiver's
    // shape. False while a pending associated Type keeps the header open, so that a later BindHeader binds it again.
    private bool BindFunctionHeader(BindingSymbol symbol, FunctionKoto function)
    {
        var scope = this.scopes[function];
        var header = this.PrepareVirtualHeaderPremises(function, scope); // An override's inputs and result bind under its header layer.
        var originDeclaration = this.BeginOriginDeclaration(function, scope);
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.ParameterSymbol(function, i).Type = this.BindType(function.Parameters[i].Type, header);
        }

        // Named signatures never infer a result from their body or callers (SPEC 10.5).
        symbol.Type = function.ReturnType is { } result ? this.BindType(result, header) : BoundType.Unit;
        var pendingInputs = false;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            // BindType retains a partial value on the parameter symbol without publishing it on
            // syntax. A failed Type (null) is already an error, not an inference dependency.
            pendingInputs |= this.ParameterSymbol(function, i).Type is { } parameterType && this.PendingAssociatedType(parameterType);
        }

        var pendingHeader = pendingInputs || (symbol.Type is { } resultType && this.PendingAssociatedType(resultType));
        if (this.associatedInferenceReady && pendingHeader)
        {
            // Result portions that do not depend on missing Type values use the ordinary Origin
            // completion. Unrelated result holes do not hide a determined input/elision contract.
            if (!pendingInputs && this.PartialOriginContractReady(function))
            {
                this.CompleteOriginDeclaration(originDeclaration ?? this.originDeclarations.GetValueOrDefault(function));
            }

            return false;
        }

        this.CompleteOriginDeclaration(originDeclaration ?? this.originDeclarations.GetValueOrDefault(function));
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.ParameterSymbol(function, i).Type = function.Parameters[i].Type.BoundType;
        }

        if (symbol.ReceiverIndex >= 0)
        {
            var receiver = function.Parameters[symbol.ReceiverIndex];
            if (!this.IsReceiverType(receiver.Type.BoundType, symbol.Scope.Owner.BoundSymbol!) || receiver.ExternalName != "self" || receiver.DefaultValue is not null)
            {
                this.Record(function, BindingFailure.InvalidTypeFormation, []);
            }
        }

        return true;
    }

    // The first failed node inside a parameter Type whose outer layer resolved.
    private Koto? FailedSignaturePart(FunctionKoto function)
    {
        var visitor = this.failedSignaturePartVisitor ??= new();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (visitor.Find(function.Parameters[i].Type, invalidState: true) is { } part)
            {
                return part;
            }
        }

        return null;
    }
}
