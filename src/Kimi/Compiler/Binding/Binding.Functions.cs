// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The binding of a function definition: its header (BindHeader's function part, also bound lazily from a use), its signature, then its
// parts (defaults, base initializer, body).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private int defaultBindingDepth;

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

    // A function definition: its signature (header, receiver, generic arguments, Constraints, effect bounds, parameter and result
    // Types), then its parts (parameter defaults, base initializer and body).
    private BoundType? BindFunction(FunctionKoto function, BindingScope scope)
    {
        var symbol = this.symbols.GetValueOrDefault(function);
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
            var parameter = function.Parameters[i];
            this.symbols[parameter].Type = this.BindType(parameter.Type, scope);
        }

        if (function.ReturnType is { } resultSyntax)
        {
            this.BindType(resultSyntax, scope);
        }

        this.CheckCaseBound(function);
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            if (parameter.DefaultValue is { } value)
            {
                this.defaultBindingDepth++;
                try
                {
                    this.RequireType(value, scope, parameter.Type.BoundType);
                }
                finally
                {
                    this.defaultBindingDepth--;
                }
            }
        }

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
            var discards = KotoHelper.DiscardsFunctionBody(function);
            var result = discards ? this.BindNode(expression, scope) : this.BindExpected(expression, scope, symbol?.Type, function.ReturnType);
            if (symbol is not null)
            {
                var structural = this.ResultStructure();
                if (!discards && (KotoHelper.IsBodyExpression(expression) || structural.CanComplete(expression)) &&
                    symbol.Type is { } expected && result is not null && !this.FitsTypeAt(result, expected, function))
                {
                    this.FailMismatch(expression, expression, result, expected);
                }
            }
        }

        this.JudgeHelperCaptures(function);
        if (function.IsAnonymous || (function.IsSpecialization && !this.specializations.ContainsKey(function)))
        {
            return this.Fail(function, BindingFailure.Unsupported, true);
        }

        return Complete(function, function.IsGenerated ? BoundType.Unit : symbol?.Type);
    }

    // A function's header (BindHeader's function part): its parameter and result Types under its Origin declaration, then its receiver's
    // shape. False while a pending associated Type keeps the header open, so that a later BindHeader binds it again.
    private bool BindFunctionHeader(BindingSymbol symbol, FunctionKoto function)
    {
        var scope = this.scopes[function];
        this.PrepareVirtualHeaderPremises(function, scope);
        var originDeclaration = this.BeginOriginDeclaration(function, scope);
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            this.symbols[parameter].Type = this.BindType(parameter.Type, scope);
        }

        // Named signatures never infer a result from their body or callers (SPEC 10.5).
        symbol.Type = function.ReturnType is { } result ? this.BindType(result, scope) : BoundType.Unit;
        var pendingInputs = false;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            // BindType retains a partial value on the parameter symbol without publishing it on
            // syntax. A failed Type (null) is already an error, not an inference dependency.
            pendingInputs |= this.symbols[function.Parameters[i]].Type is { } parameterType && this.PendingAssociatedType(parameterType);
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
            this.symbols[function.Parameters[i]].Type = function.Parameters[i].Type.BoundType;
        }

        if (symbol.ReceiverIndex >= 0)
        {
            var receiver = function.Parameters[symbol.ReceiverIndex];
            if (!this.IsReceiverType(receiver.Type.BoundType, symbol.Scope.Owner.BoundSymbol!) || receiver.ExternalName != "self" || receiver.DefaultValue is not null)
            {
                this.Fail(function, BindingFailure.InvalidTypeFormation);
            }
        }

        return true;
    }
}
