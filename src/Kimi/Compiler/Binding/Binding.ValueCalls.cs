// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Value calls (SPEC 7.7, 8.10, 15.6.4, 15.8.2; DECISIONS R4-M148, R4-M152): a call of a callable value, a Function value or a Closure.
// - Responsibility: the callee's acquisition by its receiver mode (AcquireValueCallee), each argument bound and adapted
//   (AcquireValueCallArguments), the per-call inputs solved and substituted, and the result (ValueCallResult). P9: a Closure's result
//   naming its call receiver ρ takes the receiver acquisition's Origin (ReceiverImage) and its call result atom the call's Unbound atom,
//   images of the Closure binder's reserved atoms built only where the result names them (CallAtomsOf, PerCallImages); a result naming,
//   in a non-raw Origin position, an atom of a Closure that does not enclose the call is F1's located Unsupported_Kd (a raw layer's is
//   PLAN G273's open issue); R1's condition is the old analysis's gate fact.
// - Inputs: the call, the callee's call signature (TryCallable; a Closure Type's definition, BoundClosure.Of) and its receiver mode.
//   Outputs: the call's plan (CallPlan.SetValue) and its result Type, or a failure at the call.
// - Invariants: a bound call's result names no input or reserved atom of its callee's binder (otherwise the call is Unsupported_Kd);
//   the images are this call's alone.
// - Prohibitions: no judgment of the callee's body; no second copy of the substitution (Binding.Storage's SubstituteStoredOrigins).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly BorrowAnnotationVisitor borrowAnnotationVisitor = new();

    // The atoms of Closures a value call's result names (CallAtomsOf).
    [Flags]
    private enum CallAtoms : byte
    {
        None = 0,
        Receiver = 1,
        Result = 2,
        Foreign = 4,
    }

    // The unimplemented acquisition of an object callee's payload, as its reason (SPEC 7.3): a temporary handle, a handle reached through
    // a reference or an element, or an Exclusive call of a payload not proven Sealed.

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

    // An object callee Place the payload lowering reaches only through a reference, an object layer or an element; a binding, an owned
    // Field path below one, an object view value and a Temporary Value are reached directly.
    private static bool IndirectObjectCallee(Koto handle)
    {
        var node = KotoHelper.UnwrapParentheses(handle);
        if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.ObjRef or SemanticsKind.ObjUniq } ||
            (node is not (IdentifierNameKoto or MemberAccessKoto or IndexKoto or ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow }) &&
                !(node is BinaryKoto element && ElementAccess.IsSyntax(element))))
        {
            return false;
        }

        while (node is MemberAccessKoto member)
        {
            node = KotoHelper.UnwrapParentheses(member.Left);
            if (node.BoundType is not { } layer || (layer.Kind == BoundTypeKind.Semantics && layer.Semantics != SemanticsKind.Owner))
            {
                return true;
            }
        }

        return node is not IdentifierNameKoto;
    }

    private static bool CallableReceiverFits(SemanticsKind actual, SemanticsMask required)
        => actual == SemanticsKind.Ref || required == SemanticsMask.Owner || (actual == SemanticsKind.Uniq && required == SemanticsMask.Uniq);

    private static SemanticsMask CallableReceiverMask(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => SemanticsMask.Ref, SemanticsKind.Uniq => SemanticsMask.Uniq, _ => SemanticsMask.Owner };

    // SPEC 15.6.4, 8.10: the binder of a callee's own per-call input Origins: its Item or closure declaration, otherwise the
    // Function Type of the signature, whose own inputs are the only Input atoms it binds at an outer layer.
    private static Koto? CalleeBinder(BoundType? callee, BoundType signature)
    {
        return callee is not null && SignatureOwner(callee) is { } declaration ? declaration : FunctionTypeBinder(signature);
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

    // Whether a result names an environment binding of the closure `function` or its call receiver Origin ρ, so it depends on the
    // receiver (SPEC 15.8.2, 7.7.4; DECISIONS R4-M126 P12).
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

            return ReferenceEquals(origin.Binder, function) && ReceiverAtom(origin);
        }
    }

    // The atoms of Closures a value call's result names in its non-raw Origin positions (SPEC 15.8.2; DECISIONS R4-M152): its callee
    // Closure's call receiver ρ and call result, each of which the call substitutes, and F1's located limit, an atom of another Closure
    // that does not enclose the call (its receiver's, an environment binding's or an input's): a Closure value returned from that
    // Closure's call, whose signature names that Closure's atoms, which no representation maps to the receiver and arguments of the call
    // that returned the value (the owner's 「2c-5 F1: (c')」). A raw layer's Origin is not read here (PLAN G273's open issue, with the
    // raw role's sites, DECISIONS BU-24).
    private static CallAtoms CallAtomsOf(BoundType type, FunctionKoto closure, Koto call)
    {
        if (!type.CarriesAnyOrigin)
        {
            return CallAtoms.None;
        }

        var atoms = CallAtomsOf(type.Origin, closure, call);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            atoms |= CallAtomsOf(type.OriginArguments[i], closure, call);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            atoms |= CallAtomsOf(type.Components[i], closure, call);
        }

        return atoms;
    }

    private static CallAtoms CallAtomsOf(BoundOrigin? origin, FunctionKoto closure, Koto call)
    {
        if (origin is null)
        {
            return CallAtoms.None;
        }

        var atoms = CallAtoms.None;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            atoms |= CallAtomsOf(origin.Operands[i], closure, call);
        }

        if (origin.Kind is not (OriginKind.Projection or OriginKind.Input or OriginKind.Parameter) ||
            origin.Binder is not FunctionKoto { IsAnonymous: true } binder)
        {
            return atoms;
        }

        if (!ReferenceEquals(binder, closure))
        {
            return IsWithin(call, binder) ? atoms : atoms | CallAtoms.Foreign;
        }

        return origin.Kind != OriginKind.Projection ? atoms : atoms | origin.Slot switch
        {
            ClosureValueSlot => CallAtoms.Receiver,
            CallResultSlot => CallAtoms.Result,
            _ => CallAtoms.None,
        };
    }

    // SPEC 7.7.3, 15.8.2 (DECISIONS R4-M152): the Origin of a Closure call's receiver acquisition, which a result naming ρ takes: the
    // acquisition AcquireValueCallee publishes (a payload's borrow, TryPayloadReceiver; a stored exclusive callee's Reborrow, BU-27);
    // through any other reference callee its referent's (ReferentOrigin: `ref/F`, and a Shared Closure read through `uniq/F`); any
    // owned callee's Borrow (PlaceOrigin), for the Closure literal itself its own temporary, which is ρ's atom in its evaluating body.
    private BoundOrigin ReceiverImage(InvocationKoto call, FunctionKoto closure, in BoundArgumentOperation acquisition)
    {
        if (acquisition.AdaptedType?.Origin is { } adapted)
        {
            return adapted;
        }

        var callee = call.Method.BoundType!;
        if (callee.Kind == BoundTypeKind.Semantics && this.ReferentOrigin(call.Method, callee) is { } referent)
        {
            return referent;
        }

        return this.PlaceOrigin(call.Method);
    }

    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver) => this.TryCallable(type, scope, out signature, out receiver, out _);

    // SPEC 8.10, 10.5: the one call signature of a callable value. Distinct Callable signatures on one F are call candidates
    // (`several`); with them there is no single signature S. A Closure Type's signature and receiver are its definition's
    // (BoundClosure.Of), whatever its literal's creation outcome (DECISIONS R5-M63, R4-M152).
    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver, out bool several)
    {
        several = false;
        var owner = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (this.FunctionItemSignature(owner) is { } itemSignature)
        {
            signature = this.ContractType(itemSignature, scope);
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (owner.Kind == BoundTypeKind.Function)
        {
            signature = owner;
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (BoundClosure.Of(owner) is { } closure)
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

    // SPEC 7.3.3 (object-kind column and check 1), 7.7.3, 13.5.5: a callee reached through an obj/rc/arc handle or an objref/objuniq view,
    // and a written payload follow, is acquired as its complete payload, `h@follow@ref` for a Shared call and `h@follow@uniq` for an
    // Exclusive one, through the same checked projection as the explicit spelling; the payload offers no Take.
    // The checks follow SPEC 7.3's order: the operation's legality and authority (a payload offers no Take and rc/arc/objref only Read),
    // then the acquisition, whose unimplemented forms are located limits; a Shared call needs no Sealed proof, as an open View's
    // shared receiver does not (SPEC 13.5.5). A handle Place is lent as SPEC lends it (LendsHandle), its static root failing the handle
    // alone (FailStaticBorrow), the call bound on (SPEC 23.1.2).
    private bool TryPayloadReceiver(InvocationKoto call, Koto handle, SemanticsKind receiver, BindingScope scope, out BoundArgumentOperation operation)
    {
        operation = default;
        var handleType = this.ContractType(handle.BoundType!, scope);
        var shared = handleType.HandleMode is { PayloadAuthority: LoanRequirement.Ref } || handleType.Semantics == SemanticsKind.ObjRef || ReachedThroughShared(handle);
        var view = handleType.Semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq;
        if (receiver != SemanticsKind.Ref && shared)
        {
            this.FailObjectAuthority(call, call.Method);
            return false;
        }

        if (receiver == SemanticsKind.Owner || (receiver == SemanticsKind.Uniq && !view && IsBarePlace(handle) && !this.LendsPlace(handle, scope, true, out _)))
        {
            this.FailWrite(call, call.Method);
            return false;
        }

        var unsupported = IndirectObjectCallee(handle) || (!view && !this.LendsHandle(handle, scope, receiver == SemanticsKind.Uniq, out _));
        var payload = handleType.Components[0];
        var adapted = (BoundType?)null;
        if (!unsupported && receiver == SemanticsKind.Ref)
        {
            adapted = this.types.Reference(SemanticsKind.Ref, payload, this.PlaceOrigin(handle));
        }
        else if (!unsupported && !this.TryPayloadProjection(handle, this.types.Reference(receiver, payload), handleType, scope, out adapted))
        {
            unsupported = true;
        }

        if (unsupported)
        {
            this.FailExplained(ref this.unsupportedSpans, call, BindingFailure.Unsupported, call.Method.Span);
            return false;
        }

        operation = new(handle, handleType, adapted, ArgumentOperationKind.PayloadProjection, ArgumentAdaptation.CrossSemanticsBorrow, AdaptedType: adapted);
        _ = !view && this.FailStaticBorrow(handle, ArgumentOperationKind.PayloadProjection, receiver);
        return true;
    }

    // SPEC 7.3, 7.7.3: the callee of a value call is a Receiver Expression, acquired in the call's mode: through a handle or view as its
    // payload (TryPayloadReceiver), exclusively only where its lending point is exclusively writable, and owned only by the value read
    // of a Copy closure; a Shared or Exclusive call borrows an owned callee Place as SPEC lends it (LendsPlace), whose static root is the
    // located limit until P42, final here as a value call selects no other callee: it fails the callee alone, the call bound on (SPEC
    // 23.1.2; FailStaticBorrow). False when the acquisition fails, its record made.
    private bool AcquireValueCallee(InvocationKoto call, BindingScope scope, SemanticsKind receiver, out BoundArgumentOperation receiverOperation)
    {
        var receiverType = call.Method.BoundType!;
        receiverOperation = default;
        var handle = receiverType.Kind == BoundTypeKind.Semantics && IsObjectSemantics(receiverType.Semantics) ? call.Method
            : receiver != SemanticsKind.Owner && KotoHelper.UnwrapParentheses(call.Method) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } follow ? follow.Left : null;
        if (handle is not null)
        {
            return this.TryPayloadReceiver(call, handle, receiver, scope, out receiverOperation);
        }

        if (receiver == SemanticsKind.Uniq)
        {
            if (receiverType.Semantics == SemanticsKind.Ref)
            {
                this.FailWrite(call, call.Method);
                return false;
            }

            // SPEC 7.3, 7.7.3: the callee is a Receiver Expression, acquired exclusively without a spelling when
            // its lending point is exclusively writable; a let-bound closure Place is never Copied instead.
            if (receiverType.Semantics == SemanticsKind.Owner && IsBarePlace(call.Method) && !this.LendsPlace(call.Method, scope, true, out _))
            {
                this.FailWrite(call, call.Method);
                return false;
            }
        }
        else if (receiver == SemanticsKind.Owner)
        {
            // SPEC 7.3 (owning row), 3.5.3: a reference supplies an owning receiver only by the value read of a read Type, which a
            // callable value never is; `r@follow()` Copies a Copy referent.
            if (receiverType.Kind == BoundTypeKind.Semantics)
            {
                this.FailWrite(call, call.Method);
                return false;
            }

            // SPEC 7.7.3: a Consuming call Copies a Copy closure; a Non-Copy closure Place needs c@move().
            if (receiverType.Semantics == SemanticsKind.Owner && IsBarePlace(call.Method) && this.ProveCopy(receiverType, call) != ConstraintProof.Proven)
            {
                this.FailAcquisition(call, BindingFailure.TransferRequired, call.Method);
                return false;
            }
        }

        _ = receiver != SemanticsKind.Owner && receiverType.Semantics == SemanticsKind.Owner &&
            this.FailStaticBorrow(call.Method, ArgumentOperationKind.Borrow, receiver);

        // SPEC 7.7.3, 15.6.3 (DECISIONS BU-27): a stored exclusive callee taken in its own mode is Reborrowed, named as every Reborrow is;
        // the MIR builder types the Reborrow by the operation's AdaptedType.
        if (receiver == SemanticsKind.Uniq && receiverType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [var target] })
        {
            var reborrow = this.types.Rebuild(receiverType, [target], this.ReferentOrigin(call.Method, receiverType), []);
            receiverOperation = new(null, receiverType, null, ArgumentOperationKind.Reborrow, ArgumentAdaptation.SameSemanticsReborrow, AdaptedType: reborrow);
        }

        return true;
    }

    private BoundType? BindValueCall(InvocationKoto call, BindingScope scope, BoundType signature, SemanticsKind receiver = SemanticsKind.Ref)
    {
        // A function value has no Type parameters of its own, so explicit Type arguments select nothing (SPEC 7.7, 12.5.2);
        // the receiver checks below read the bound callee, which a generic application never is.
        if (call.Method is GenericsKoto)
        {
            return this.Fail(call, BindingFailure.NoApplicableCandidate);
        }

        if (!this.AcquireValueCallee(call, scope, receiver, out var receiverOperation))
        {
            return null;
        }

        var parameters = signature.Components[0];
        var count = ReferenceEquals(parameters, BoundType.Unit) ? 0 : parameters.Components.Count;
        if (count != call.ArgumentNodes.Count)
        {
            return this.Fail(call, BindingFailure.NoApplicableCandidate);
        }

        // A per-call input is one bound by the callee's own binder; an input written over a fixed Origin of the calling body is fitted
        // as written.
        var ownBinder = CalleeBinder(call.Method.BoundType, signature);
        var operations = this.argumentOperationScratch.Rent(count + 1);
        operations[count] = default; // Common call-relation helpers reserve the last slot for a receiver.
        var instantiated = this.RentTypes(count);
        var inputCount = ownBinder is null ? 0 : InputOriginCount(ownBinder);
        var originCount = ownBinder is FunctionKoto function ? function.Origins.Count : 0;
        var argumentOrigins = this.originScratch.Rent(inputCount);
        var origins = this.originScratch.Rent(originCount);
        Array.Clear(argumentOrigins, 0, inputCount);
        Array.Clear(origins, 0, originCount);
        try
        {
            if (!this.AcquireValueCallArguments(call, scope, parameters, operations.AsSpan(0, count), instantiated.AsSpan(0, count)))
            {
                return null;
            }

            // Collect the complete parameter structure, including nested slots, through the ordinary call solver.
            // Scratch inputs hold acquired Types until substitution commits the one selected call's contract.
            var conditioned = ownBinder is FunctionKoto { IsAnonymous: false } named && this.HasOriginConditions(named) ? named : null;
            if (ownBinder is not null && HasUnsubstitutedInput(signature, ownBinder) && !this.SolveValueCallOrigins(ownBinder, parameters, instantiated.AsSpan(0, count), origins, argumentOrigins, call))
            {
                if (conditioned is not null)
                {
                    (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = [new(conditioned, null, null)];
                }

                return this.Fail(call, BindingFailure.NoApplicableCandidate);
            }

            for (var i = 0; i < count; i++)
            {
                var parameter = ownBinder is null ? parameters.Components[i] :
                    this.SubstituteStoredOrigins(parameters.Components[i], ownBinder, origins.AsSpan(0, originCount), argumentOrigins.AsSpan(0, inputCount));
                // An input's Borrow of a static root is the located limit, its relations waiting for P42 (FailStaticBorrow).
                if (!this.FailStaticBorrow(operations[i].Source!, operations[i].Kind, parameter.Semantics) &&
                    !this.CheckTypeUse(instantiated[i], parameter, operations[i].Source!))
                {
                    return this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                instantiated[i] = parameter;
                operations[i] = operations[i] with { ParameterType = parameter };
            }

            // SPEC 15.6.1, 15.6.4 step 3: as at a selected call, each input's instantiated Type is well formed and each clause holds, also
            // where no input is per-call: a written input's well-formedness is its signature's premise, proven at each call (R4-M137).
            this.callRelationScratch.Clear();
            for (var i = 0; i < count; i++)
            {
                if (operations[i] is { Source: { } at, AdaptedType: { } adapted, ParameterType: { } parameter })
                {
                    this.RequireInputPremises(at, parameter, adapted);
                }
            }

            if (conditioned is not null)
            {
                this.JudgeCallClauses(call, conditioned, operations.AsSpan(0, count + 1), count, origins, argumentOrigins, null);
            }

            this.PublishCallRelations();
            var result = this.ValueCallResult(
                call, signature, ownBinder, origins.AsSpan(0, originCount), argumentOrigins.AsSpan(0, inputCount), receiverOperation);
            if (result is null)
            {
                return null;
            }

            // SPEC 15.3.7, 15.6.1: the instantiated result, under a named callee's premises, else a nested signature's whole (the owner's (A)).
            this.RequireResultPremises(ownBinder is FunctionKoto { IsAnonymous: false } namedCallee ? namedCallee : null, result, call);

            var inputs = count == 0 ? BoundType.Unit : this.types.Tuple(instantiated.AsSpan(0, count));
            var declaredSignature = signature;
            signature = this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, result]);
            (call.CallStorage ??= new()).SetValue(call.Method, signature, operations.AsSpan(0, count), declaredSignature, receiverOperation, receiver);
            return Complete(call, result);
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.typeScratch.Return(instantiated, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(argumentOrigins, clearArray: true);
        }
    }

    // SPEC 15.6.4, 15.1.5: each argument of a value call, bound against the Type the call hands it (OpenCallPosition) and adapted to its
    // parameter: its operation and the Type it acquires, which the call's substitution then instantiates; false when one fails, which
    // fails or leaves unresolved the call.
    private bool AcquireValueCallArguments(
        InvocationKoto call, BindingScope scope, BoundType parameters, Span<BoundArgumentOperation> operations, Span<BoundType> instantiated)
    {
        for (var i = 0; i < instantiated.Length; i++)
        {
            if (call.GetArgumentLabel(i) is not null)
            {
                this.Fail(call, BindingFailure.NoApplicableCandidate);
                return false;
            }

            var source = call.ArgumentNodes[i];
            var parameter = parameters.Components[i];
            var literal = IsUnfittedLiteral(source);
            var actual = this.BindNode(source, scope, literal ? LiteralInputType(parameter) : this.OpenCallPosition(parameter, call, i));
            if (actual is null || source.BindingState != BindingState.Resolved)
            {
                Complete(call, null);
                return false;
            }

            actual = this.ArgumentType(source, actual);
            this.acquisitionFailure = null;
            if (!this.AdaptInput(source, parameter, actual, scope, null, null, out var adapted, out var quality, out var kind))
            {
                // SPEC 15.1.5: name the missing spelling when a bare Place was the obstacle, as an ordinary call does.
                if (this.acquisitionFailure is { } acquisition)
                {
                    this.FailAcquisition(call, acquisition.Kind, acquisition.Place, acquisition.Object, unresolved: true);
                }
                else
                {
                    this.Fail(call, BindingFailure.NoApplicableCandidate);
                }

                return false;
            }

            // ArgumentType includes the already selected expected adaptation. Retain the original syntax
            // Type as the source identity, as ordinary calls do; ownership applies that adaptation once.
            operations[i] = new(
                source, source.BoundType, parameter, kind, literal ? ArgumentAdaptation.Literal : quality, ParameterIndex: i, AdaptedType: adapted);
            instantiated[i] = adapted;
        }

        return true;
    }

    // SPEC 15.6.4, 15.8.2 (DECISIONS R4-M152): a value call's result. Fresh input Origins and fixed shared capture results retain their
    // complete call contracts; a result over the per-call inputs takes the arguments' Origins, as an ordinary call's does; one written
    // over a fixed Origin of the calling body stays as written. A Closure's result naming its call receiver ρ takes the receiver
    // acquisition's Origin and its call result atom the call's Unbound atom, each image built only where the result names its atom;
    // one naming another Closure's atom is F1's located limit. R1's condition (`dependent`, or a per-call result naming the receiver)
    // is the old analysis's gate fact (Binding.ValueCallGate), written on the call's plan.
    private BoundType? ValueCallResult(
        InvocationKoto call,
        BoundType signature,
        Koto? ownBinder,
        ReadOnlySpan<BoundOrigin> origins,
        ReadOnlySpan<BoundOrigin> inputs,
        in BoundArgumentOperation acquisition)
    {
        var result = signature.Components[1];
        var inputBinder = ownBinder is not null && HasUnsubstitutedOrigin(result, ownBinder) ? ownBinder : null;
        var dependent = inputBinder is null
            ? !PerCallSignature(signature, ownBinder) && !this.ResultFixedForCall(result, call.Method, call)
            : HasEnvironmentOrigin(result, inputBinder);
        var closure = ownBinder is FunctionKoto { IsAnonymous: true, ClosureDefinition: not null } binder ? binder : null;
        var atoms = closure is null ? CallAtoms.None : CallAtomsOf(result, closure, call);
        if ((atoms & CallAtoms.Foreign) != 0)
        {
            return this.Fail(call, BindingFailure.Unsupported); // F1's located limit (DECISIONS R4-M152): the owner's (c').
        }

        if (inputBinder is not null || atoms != CallAtoms.None)
        {
            var image = (atoms & CallAtoms.Receiver) != 0 ? this.ReceiverImage(call, closure!, acquisition) : null;
            var unbound = (atoms & CallAtoms.Result) != 0 ? this.OriginAtom(call, OriginKind.Unbound, 0) : null;
            result = this.SubstituteStoredOrigins(result, ownBinder!, origins, new(inputs, image, unbound));
            if (inputBinder is not null && HasUnsubstitutedInput(result, inputBinder))
            {
                return this.Fail(call, BindingFailure.Unsupported);
            }
        }

        this.RecordReceiverDependence(call, dependent);
        return result;
    }
}
