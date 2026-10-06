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

    public BoundType DeclaredSignature { get; private set; } = null!;

    public BoundType ReturnType => this.Signature.Components[1];

    public SemanticsKind ReceiverKind { get; internal set; } = SemanticsKind.Ref;

    public BoundType ReceiverType => this.Receiver.BoundType!;

    public ReadOnlySpan<BoundArgumentOperation> Arguments => this.arguments;

    internal void Set(Koto receiver, BoundType signature, ReadOnlySpan<BoundArgumentOperation> arguments, BoundType? declaredSignature = null)
    {
        this.Receiver = receiver;
        this.Signature = signature;
        this.DeclaredSignature = declaredSignature ?? signature;
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

    private static SemanticsMask CallableReceiverMask(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => SemanticsMask.Ref, SemanticsKind.Uniq => SemanticsMask.Uniq, _ => SemanticsMask.Owner };

    // SPEC 15.6.4, 8.6: the binder of a callee's own per-call input Origins: its Item or closure declaration, otherwise the
    // Function Type of the signature, whose own inputs are the only Input atoms it binds at an outer layer.
    private static Koto? CalleeBinder(BoundType? callee, BoundType signature)
    {
        return callee is not null && SignatureOwner(callee) is { } declaration ? declaration : FunctionTypeBinder(signature);
    }

    // Every input Origin the callee binds is the outer Origin of a direct borrowed input at its own position (SPEC 15.6.4); other
    // inputs mention none of them.
    private static bool OwnInputsAreDirect(BoundType signature, Koto binder)
    {
        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            var input = inputs.Components[i];
            if (input is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, OriginArguments.Count: 0, Origin: { Kind: OriginKind.Input, Occurrence: null } origin } &&
                ReferenceEquals(origin.Binder, binder))
            {
                if (origin.Slot != i || HasUnsubstitutedOrigin(input.Components[0], binder))
                {
                    return false;
                }
            }
            else if (HasUnsubstitutedOrigin(input, binder))
            {
                return false;
            }
        }

        return true;
    }

    // A result whose Origins are static or fixed Origins of a function that encloses the call: the signature's own written contract,
    // which the callee's body was checked against.
    private static bool FixedInBody(BoundType type, Koto use)
    {
        if (!FixedOrigin(type.Origin, use))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!FixedOrigin(type.OriginArguments[i], use))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!FixedInBody(type.Components[i], use))
            {
                return false;
            }
        }

        return true;

        static bool FixedOrigin(BoundOrigin? origin, Koto use)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!FixedOrigin(origin.Operands[i], use))
                    {
                        return false;
                    }
                }

                return true;
            }

            return FixedInBodyOrigin(origin, use);
        }
    }

    // Whether a result still names a per-call input or a universal Origin of `binder`.
    private static bool HasUnsubstitutedInput(BoundType type, Koto binder)
    {
        if (InputAtom(type.Origin, binder))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (InputAtom(type.OriginArguments[i], binder))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasUnsubstitutedInput(type.Components[i], binder))
            {
                return true;
            }
        }

        return false;

        static bool InputAtom(BoundOrigin? origin, Koto binder)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (InputAtom(origin.Operands[i], binder))
                {
                    return true;
                }
            }

            return origin.Kind is OriginKind.Input or OriginKind.Parameter && ReferenceEquals(origin.Binder, binder);
        }
    }

    // Whether a result names an environment binding of the closure `function` (SPEC 15.8.2).
    private static bool HasEnvironmentOrigin(BoundType type, Koto function)
    {
        if (EnvironmentAtom(type.Origin, function))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (EnvironmentAtom(type.OriginArguments[i], function))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasEnvironmentOrigin(type.Components[i], function))
            {
                return true;
            }
        }

        return false;

        static bool EnvironmentAtom(BoundOrigin? origin, Koto function)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (EnvironmentAtom(origin.Operands[i], function))
                {
                    return true;
                }
            }

            return origin.Kind == OriginKind.Projection && ReferenceEquals(origin.Binder, function) && origin.Slot <= EnvironmentSlot(0) && origin.Slot != ClosureValueSlot;
        }
    }

    // Whether every Origin of an argument is the parameter's own at that position or static, so the argument needs no relation.
    private static bool OriginsAsWritten(BoundType actual, BoundType expected)
    {
        if (!SameOrStatic(actual.Origin, expected.Origin) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return false;
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            if (!SameOrStatic(actual.OriginArguments[i], expected.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (!ReferenceEquals(actual.Components[i], expected.Components[i]) && !OriginsAsWritten(actual.Components[i], expected.Components[i]))
            {
                return false;
            }
        }

        return true;

        static bool SameOrStatic(BoundOrigin? actual, BoundOrigin? expected) => ReferenceEquals(actual, expected) || actual?.Kind == OriginKind.Static;
    }

    // SPEC 15.6.5: whether every position at which a parameter names a fixed Origin of a function enclosing the call, rather than an
    // Origin of the callee itself, receives that Origin as written or static, so the argument's Loans need no extension to that region.
    private static bool EnclosingOriginsAsWritten(BoundType actual, BoundType expected, Koto callee, Koto use)
    {
        if (!Fits(actual.Origin, expected.Origin, callee, use))
        {
            return false;
        }

        if (actual.Kind != expected.Kind || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            // An implicit borrow adds the parameter's borrow layer, whose Origin was compared above; any other adapted argument has no
            // position to compare, so only a parameter without such an Origin needs none.
            return expected is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && actual.Kind != BoundTypeKind.Semantics
                ? EnclosingOriginsAsWritten(actual, expected.Components[0], callee, use)
                : !Names(expected, callee, use);
        }

        for (var i = 0; i < expected.OriginArguments.Count; i++)
        {
            if (!Fits(actual.OriginArguments[i], expected.OriginArguments[i], callee, use))
            {
                return false;
            }
        }

        for (var i = 0; i < expected.Components.Count; i++)
        {
            if (expected.Kind != BoundTypeKind.Function && !ReferenceEquals(actual.Components[i], expected.Components[i]) &&
                !EnclosingOriginsAsWritten(actual.Components[i], expected.Components[i], callee, use))
            {
                return false;
            }
        }

        return true;

        static bool Fits(BoundOrigin? actual, BoundOrigin? expected, Koto callee, Koto use)
            => expected is null || ReferenceEquals(actual, expected) || actual?.Kind == OriginKind.Static || !Atom(expected, callee, use);

        static bool Names(BoundType type, Koto callee, Koto use)
        {
            if ((type.Origin is { } origin && Atom(origin, callee, use)) || (type.Kind != BoundTypeKind.Function && AnyComponent(type, callee, use)))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (Atom(type.OriginArguments[i], callee, use))
                {
                    return true;
                }
            }

            return false;
        }

        static bool AnyComponent(BoundType type, Koto callee, Koto use)
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (Names(type.Components[i], callee, use))
                {
                    return true;
                }
            }

            return false;
        }

        static bool Atom(BoundOrigin origin, Koto callee, Koto use)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (Atom(origin.Operands[i], callee, use))
                {
                    return true;
                }
            }

            return !ReferenceEquals(origin.Binder, callee) && FixedInBodyOrigin(origin, use);
        }
    }

    // A fixed Origin of a named function that encloses the use: its input or a parameter Origin of its signature.
    private static bool FixedInBodyOrigin(BoundOrigin origin, Koto use)
        => origin.Kind is OriginKind.Input or OriginKind.Parameter && origin.Binder is FunctionKoto { IsAnonymous: false } owner && IsWithin(use, owner);

    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver) => this.TryCallable(type, scope, out signature, out receiver, out _);

    // SPEC 8.6, 10.5: the one call signature of a callable value. Distinct Callable signatures on one F are call candidates
    // (`several`); with them there is no single signature S.
    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver, out bool several)
    {
        several = false;
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

        var normalizedOwner = this.CallableContractType(owner, scope);
        if (!ReferenceEquals(normalizedOwner, owner))
        {
            return this.TryCallable(normalizedOwner, scope, out signature, out receiver, out several);
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
                if (fact.Kind != ConstraintKind.Callable || !ReferenceEquals(this.CallableContractType(fact.Subject!, scope), owner) || !this.AvailableConstraintFact(environment, fact))
                {
                    continue;
                }

                var currentSignature = this.CallableContractType(fact.RequiredType!, scope);
                if (signature is not null && !SameCallableSignature(signature, currentSignature))
                {
                    several = true;
                    return false;
                }

                signature ??= currentSignature;
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

        // Fresh direct input Origins and fixed shared capture results retain their complete call contracts. A result over the
        // per-call inputs alone takes the arguments' Origins, as an ordinary call's does (SPEC 15.6.4). A per-call input is one
        // bound by the callee's own binder; an input or result written over a fixed Origin of the calling body is fitted as written.
        var ownBinder = CalleeBinder(call.Method.BoundType, signature);
        var inputBinder = (Koto?)null;
        var dependent = false;
        if (ownBinder is not null && !OwnInputsAreDirect(signature, ownBinder))
        {
            return this.Fail(call, BindingFailure.Unsupported);
        }

        if (ownBinder is not null && HasUnsubstitutedOrigin(signature.Components[1], ownBinder))
        {
            inputBinder = ownBinder;
        }
        else if (!PerCallSignature(signature, ownBinder) && !this.ResultFixedForCall(signature.Components[1], call.Method, call))
        {
            dependent = true; // SPEC 15.8.2: a result over the closure's environment depends on the call receiver.
        }

        var operations = this.argumentOperationScratch.Rent(count);
        var instantiated = this.RentTypes(count);
        var argumentOrigins = this.originScratch.Rent(count);
        Array.Clear(argumentOrigins, 0, count);
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
                this.acquisitionFailure = null;
                if (!this.AdaptInput(source, parameter, actual, scope, null, null, out var adapted, out var quality, out var kind))
                {
                    // SPEC 15.1.5: name the missing spelling when a bare Place was the obstacle, as an ordinary call does.
                    return this.acquisitionFailure is { } acquisition
                        ? this.FailAcquisition(call, acquisition.Kind, acquisition.Place, acquisition.Object, true)
                        : this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                if (parameter.Origin is { Kind: OriginKind.Input } own && ReferenceEquals(own.Binder, ownBinder) && adapted.Origin is { } argumentOrigin)
                {
                    argumentOrigins[i] = argumentOrigin;
                    parameter = this.WithOrigins(parameter, argumentOrigin, (BoundOrigin[])parameter.OriginArguments);
                }
                else if (!OriginsAsWritten(adapted, parameter) && this.FitsTypeAt(adapted, parameter, source))
                {
                    // SPEC 15.6.5: a fixed Origin of the calling body contains every later point of it, so an argument that fits it only
                    // through a premise keeps its Loans for that whole region; ownership does not extend them yet (PLAN G65).
                    this.Fail(source, BindingFailure.Unsupported);
                    return Complete(call, null);
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

            // SPEC 15.6.4 step 3, 15.3.7: a named callee's declared relations and result premises are proven for this call, as for a
            // direct call; its per-call inputs are then the solution, which each argument outlives.
            var conditioned = ownBinder is FunctionKoto { IsAnonymous: false } named && this.HasOriginConditions(named) ? named : null;
            if (conditioned is not null)
            {
                if (!this.SolveItemCallOrigins(conditioned, parameters, argumentOrigins.AsSpan(0, count), call))
                {
                    if (conditioned.Origins.Count != 0)
                    {
                        return this.Fail(call, BindingFailure.Unsupported); // A signature Origin no value call binds (see below).
                    }

                    // As at a direct call, an unsatisfiable declared relation leaves its one candidate inapplicable (PLAN G56 U3).
                    (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = [new(conditioned, null, null)];
                    return this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                for (var i = 0; i < count; i++)
                {
                    if (argumentOrigins[i] is { } solved && !ReferenceEquals(instantiated[i].Origin, solved))
                    {
                        instantiated[i] = this.WithOrigins(instantiated[i], solved, (BoundOrigin[])instantiated[i].OriginArguments);
                        operations[i] = operations[i] with { ParameterType = instantiated[i] };
                    }
                }
            }

            var result = signature.Components[1];
            if (dependent || (inputBinder is not null && HasEnvironmentOrigin(result, inputBinder)))
            {
                // The receiver binds the environment's Origins; the callee's own inputs are substituted below.
                if (this.ReceiverDependentResult(result, call.Method, receiver, call) is not { } bound)
                {
                    return this.Fail(call, BindingFailure.Unsupported);
                }

                result = bound;
            }

            if (inputBinder is not null)
            {
                result = this.SubstituteStoredOrigins(result, inputBinder, default, argumentOrigins.AsSpan(0, count));
            }

            // The receiver Origin of a temporary closure is bound to the closure literal itself, which is no per-call input; only an
            // unsubstituted input or a remaining environment binding is unsupported.
            if ((inputBinder is not null && HasUnsubstitutedInput(result, inputBinder)) || (ownBinder is not null && HasEnvironmentOrigin(result, ownBinder)))
            {
                return this.Fail(call, BindingFailure.Unsupported);
            }

            if (conditioned is not null)
            {
                this.RequireResultPremises(conditioned, result, call); // SPEC 15.3.7: the callee's result premises.
            }

            var inputs = count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, instantiated.AsSpan(0, count));
            var declaredSignature = signature;
            signature = this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, result]);
            call.ValueCallStorage ??= new();
            call.ValueCallStorage.Set(call.Method, signature, operations.AsSpan(0, count), declaredSignature);
            call.ValueCallStorage.ReceiverKind = receiver;
            call.IsValueCall = true;
            return Complete(call, signature.Components[1]);
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.typeScratch.Return(instantiated, clearArray: true);
            this.originScratch.Return(argumentOrigins, clearArray: true);
        }
    }
}
