// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // The retained invocation plan accompanies its binder.

/// <summary>A positional callable invocation with retained signature and receiver acquisition.</summary>
public sealed class BoundValueCall
{
    private BoundArgumentOperation[] arguments = [];

    public Koto Receiver { get; private set; } = null!;

    public BoundType Signature { get; private set; } = null!;

    public BoundType ReturnType => this.Signature.Components[1];

    public SemanticsKind ReceiverKind { get; internal set; } = SemanticsKind.Ref;

    public BoundType ReceiverType => this.Receiver.BoundType!;

    public ReadOnlySpan<BoundArgumentOperation> Arguments => this.arguments;

    internal void Set(Koto receiver, BoundType signature, ReadOnlySpan<BoundArgumentOperation> arguments)
    {
        this.Receiver = receiver;
        this.Signature = signature;
        if (this.arguments.Length != arguments.Length)
        {
            this.arguments = new BoundArgumentOperation[arguments.Length];
        }

        arguments.CopyTo(this.arguments);
    }
}

public sealed partial class Binding
{
    private BoundConstraint BindCallableRequirement(Koto node, BoundType subject, BindingScope scope, BindingSymbol target)
    {
        var syntax = UnwrapTypeSyntax(node);
        if (syntax is not GenericsKoto { TypeArguments.Count: 1 or 2 } generic)
        {
            return this.InternConstraint(new(ConstraintKind.Error));
        }

        var receiver = SemanticsMask.Ref;
        if (generic.TypeArguments.Count == 2)
        {
            var access = UnwrapTypeSyntax(generic.TypeArguments[0]);
            var name = access is TypeSemanticsKoto type ? type.Identifier : (access as IdentifierNameKoto)?.IdentifierName;
            if (!SemanticsMaskHelper.TryParse(name, out receiver) || receiver is not (SemanticsMask.Ref or SemanticsMask.Uniq or SemanticsMask.Owner))
            {
                return this.InternConstraint(new(ConstraintKind.Error));
            }

            Complete(access, BoundType.Unit);
            Complete(generic.TypeArguments[0], BoundType.Unit);
        }

        var signature = this.BindType(generic.TypeArguments[^1], scope);
        if (signature?.Kind != BoundTypeKind.Function || !PerCallSignature(signature))
        {
            return this.InternConstraint(new(ConstraintKind.Error));
        }

        generic.Identifier!.BoundSymbol = target;
        Complete(generic.Identifier, BoundType.Unit);
        Complete(generic, BoundType.Boolean);
        return this.InternConstraint(new(ConstraintKind.Callable, subject, signature, mask: receiver));
    }

    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver)
    {
        var owner = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (owner.Kind == BoundTypeKind.Function)
        {
            signature = owner;
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (owner.Kind == BoundTypeKind.Closure && owner.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure })
        {
            signature = closure.Signature;
            receiver = closure.Receiver;
            return true;
        }

        signature = null!;
        receiver = SemanticsKind.Owner;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind != ConstraintKind.Callable || !ReferenceEquals(fact.Subject, owner) || !this.AvailableConstraintFact(environment, fact))
                {
                    continue;
                }

                if (signature is not null && !ReferenceEquals(signature, fact.RequiredType))
                {
                    return false; // Overloaded callable signatures need candidate selection.
                }

                signature = fact.RequiredType!;
                if (fact.Mask == SemanticsMask.Ref)
                {
                    receiver = SemanticsKind.Ref;
                }
                else if (fact.Mask == SemanticsMask.Uniq && receiver == SemanticsKind.Owner)
                {
                    receiver = SemanticsKind.Uniq;
                }
            }
        }

        return signature is not null;
    }

    private BoundType? BindValueCall(InvocationKoto call, BindingScope scope, BoundType signature, SemanticsKind receiver = SemanticsKind.Ref)
    {
        var receiverType = call.Method.BoundType!;
        if (receiver == SemanticsKind.Uniq && (receiverType.Semantics == SemanticsKind.Ref ||
            (receiverType.Semantics == SemanticsKind.Owner && KotoHelper.UnwrapParentheses(call.Method) is IdentifierNameKoto && !Writable(call.Method))))
        {
            return Fail(call, BindingFailure.InvalidAssignment);
        }

        if (receiver == SemanticsKind.Owner && receiverType.Kind == BoundTypeKind.Semantics &&
            this.ProveCopy(receiverType.Components[0], call) != ConstraintProof.Proven)
        {
            return Fail(call, BindingFailure.InvalidAssignment);
        }

        var parameters = signature.Components[0];
        var count = ReferenceEquals(parameters, BoundType.Unit) ? 0 : parameters.Components.Count;
        if (call.Method is GenericsKoto || count != call.ArgumentNodes.Count)
        {
            return Fail(call, BindingFailure.NoApplicableCandidate);
        }

        // This slice supports independent results and fresh direct input Origins.
        // Fixed external/result dependencies still need the full callable contract.
        if (!PerCallSignature(signature))
        {
            return Fail(call, BindingFailure.Unsupported);
        }

        var operations = this.argumentOperationScratch.Rent(count);
        var instantiated = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (call.GetArgumentLabel(i) is not null)
                {
                    return Fail(call, BindingFailure.NoApplicableCandidate);
                }

                var source = call.ArgumentNodes[i];
                var parameter = parameters.Components[i];
                var literal = IsUnfittedLiteral(source);
                var actual = this.BindNode(source, scope, parameter);
                if (actual is null || source.BindingState != BindingState.Resolved)
                {
                    return Complete(call, null);
                }

                if (!this.AdaptInput(source, parameter, actual, scope, null, null, out var adapted, out var quality, out var kind))
                {
                    return Fail(call, BindingFailure.NoApplicableCandidate);
                }

                if (parameter.Origin is { Kind: OriginKind.Input } && adapted.Origin is { } argumentOrigin)
                {
                    parameter = this.WithOrigins(parameter, argumentOrigin, (BoundOrigin[])parameter.OriginArguments);
                }

                if (!this.CheckTypeUse(adapted, parameter, source))
                {
                    return Fail(call, BindingFailure.NoApplicableCandidate);
                }

                operations[i] = new(source, actual, parameter, kind, literal ? ArgumentAdaptation.Literal : quality, ParameterIndex: i);
                instantiated[i] = parameter;
            }

            var inputs = count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, instantiated.AsSpan(0, count));
            signature = this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, signature.Components[1]]);
            call.ValueCallStorage ??= new();
            call.ValueCallStorage.Set(call.Method, signature, operations.AsSpan(0, count));
            call.ValueCallStorage.ReceiverKind = receiver;
            call.IsValueCall = true;
            return Complete(call, signature.Components[1]);
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.typeScratch.Return(instantiated, clearArray: true);
        }
    }
}
