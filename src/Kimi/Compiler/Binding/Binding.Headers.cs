// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binds named declaration headers from their scope and retained associated-Type state.
// Publishes parameter/result Types and header flags in the existing order; a pending associated
// input or result keeps the header incomplete. Anonymous headers belong to their expression context.
public sealed partial class Binding
{
    private void BindHeader(BindingSymbol symbol)
    {
        if (symbol.HeaderBound || symbol.Resolving || symbol.Declaration is FunctionKoto { IsAnonymous: true })
        {
            // An anonymous header belongs to its fixed expression context, including on a later binding pass.
            return;
        }

        symbol.Resolving = true;
        if (symbol.Declaration is FunctionKoto function)
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

                symbol.Resolving = false;
                return;
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
        }
        else if (symbol.Property is { } property)
        {
            this.BindPropertyHeader(property);
        }
        else if (symbol.Declaration is VariableKoto variable && variable.TypeKoto is { } type)
        {
            symbol.Type = this.BindType(type, symbol.Scope);
        }

        symbol.HeaderBound = true;
        symbol.Resolving = false;
    }
}
