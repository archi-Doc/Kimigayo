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
    private readonly BorrowAnnotationVisitor borrowAnnotationVisitor = new();

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

        if (scope.Owner is FunctionKoto { BoundSymbol: { } function } && !function.HeaderBound)
        {
            this.BindHeader(function);
        }

        var signature = this.BindType(generic.TypeArguments[^1], scope);
        this.borrowAnnotationVisitor.Found = false;
        this.borrowAnnotationVisitor.Visit(generic.TypeArguments[^1]);
        if (signature?.Kind != BoundTypeKind.Function || this.borrowAnnotationVisitor.Found)
        {
            return this.InternConstraint(new(ConstraintKind.Error));
        }

        generic.Identifier!.BoundSymbol = target;
        Complete(generic.Identifier, BoundType.Unit);
        Complete(generic, BoundType.Boolean);
        return this.InternConstraint(new(ConstraintKind.Callable, subject, signature, mask: receiver));
    }

    private sealed class BorrowAnnotationVisitor : KotoVisitor
    {
        internal bool Found { get; set; }

        public override void Visit(Koto node)
        {
            this.Found |= node is TypeSemanticsKoto { HasOrigin: true, BindingSetName: null };
            if (!this.Found)
            {
                node.VisitChildren(this);
            }
        }
    }

    private static bool CallableReceiverFits(SemanticsKind actual, SemanticsMask required)
        => actual == SemanticsKind.Ref || required == SemanticsMask.Owner || (actual == SemanticsKind.Uniq && required == SemanticsMask.Uniq);

    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver)
    {
        var owner = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (this.FunctionItemSignature(owner) is { } itemSignature)
        {
            signature = itemSignature;
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (owner.Kind == BoundTypeKind.Function)
        {
            signature = owner;
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (owner.Kind == BoundTypeKind.Closure && owner.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure })
        {
            signature = this.ClosureSignature(owner)!;
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
        // A function value has no Type parameters of its own, so explicit Type arguments select nothing (SPEC 7.6, 12.4.2);
        // the receiver checks below read the bound callee, which a generic application never is.
        if (call.Method is GenericsKoto)
        {
            return this.Fail(call, BindingFailure.NoApplicableCandidate);
        }

        var receiverType = call.Method.BoundType!;
        if (receiver == SemanticsKind.Uniq)
        {
            if (receiverType.Semantics == SemanticsKind.Ref)
            {
                return this.FailWrite(call, call.Method);
            }

            // SPEC 7.3, 7.6.3: the callee is a Receiver Expression, acquired exclusively without a spelling when
            // its lending point is exclusively writable; a let-bound closure Place is never Copied instead.
            if (receiverType.Semantics == SemanticsKind.Owner && IsBarePlace(call.Method) && !this.BorrowablePlace(call.Method, scope, true))
            {
                return this.FailWrite(call, call.Method);
            }
        }

        if (receiver == SemanticsKind.Owner)
        {
            if (receiverType.Kind == BoundTypeKind.Semantics && this.ProveCopy(receiverType.Components[0], call) != ConstraintProof.Proven)
            {
                return this.Fail(call, BindingFailure.InvalidAssignment);
            }

            // SPEC 7.6.3: a Consuming call Copies a Copy closure; a Non-Copy closure Place needs c@move().
            if (receiverType.Semantics == SemanticsKind.Owner && IsBarePlace(call.Method) && this.ProveCopy(receiverType, call) != ConstraintProof.Proven)
            {
                return this.FailAcquisition(call, BindingFailure.TransferRequired, call.Method);
            }
        }

        var parameters = signature.Components[0];
        var count = ReferenceEquals(parameters, BoundType.Unit) ? 0 : parameters.Components.Count;
        if (count != call.ArgumentNodes.Count)
        {
            return this.Fail(call, BindingFailure.NoApplicableCandidate);
        }

        // Fresh direct input Origins and fixed shared capture results retain their complete call contracts.
        if (!PerCallSignature(signature) && !this.FixedCaptureSignature(signature, call.Method))
        {
            return this.Fail(call, BindingFailure.Unsupported);
        }

        var operations = this.argumentOperationScratch.Rent(count);
        var instantiated = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (call.GetArgumentLabel(i) is not null)
                {
                    return this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                var source = call.ArgumentNodes[i];
                var parameter = parameters.Components[i];
                var literal = IsUnfittedLiteral(source);
                var actual = this.BindNode(source, scope, parameter);
                if (actual is null || source.BindingState != BindingState.Resolved)
                {
                    return Complete(call, null);
                }

                actual = this.ArgumentType(source, actual);
                this.transferRequired = this.lendingRequired = false;
                if (!this.AdaptInput(source, parameter, actual, scope, null, null, out var adapted, out var quality, out var kind))
                {
                    // SPEC 15.1.5: name the missing spelling when a bare Place was the obstacle, as an ordinary call does.
                    var failure = this.lendingRequired ? BindingFailure.ExclusiveBorrowRequired : this.transferRequired ? BindingFailure.TransferRequired : BindingFailure.NoApplicableCandidate;
                    return failure != BindingFailure.NoApplicableCandidate && this.acquisitionPlace is { } place
                        ? this.FailAcquisition(call, failure, place, this.acquisitionObject, true)
                        : this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                if (parameter.Origin is { Kind: OriginKind.Input } && adapted.Origin is { } argumentOrigin)
                {
                    parameter = this.WithOrigins(parameter, argumentOrigin, (BoundOrigin[])parameter.OriginArguments);
                }

                if (!this.CheckTypeUse(adapted, parameter, source))
                {
                    return this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                // ArgumentType includes the already selected expected adaptation. Retain the original syntax
                // Type as the source identity, as ordinary calls do; ownership applies that adaptation once.
                operations[i] = new(source, source.BoundType, parameter, kind, literal ? ArgumentAdaptation.Literal : quality, ParameterIndex: i);
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
