// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Value calls (SPEC 7.7, 8.10, 15.6.4, 15.8.2; DECISIONS R4-M148, R4-M152): a call of a callable value, a Function value or a Closure.
// - Responsibility: the callee's acquisition by its receiver mode (AcquireValueCallee: its formation, FormValueCallee, then its records),
//   by case where the callee's Closure is Exclusive only in the admitted cases where a capture Reborrows (its decider and Shared
//   acquisition, CallPlan.CaseDecider and SharedReceiver; SPEC 7.7.3, PLAN G13), each argument bound and adapted
//   (AcquireValueCallPart): the evidence parts first (AcquireValueCallArguments), then, the per-call inputs solved from them, the waiting
//   parts at their instantiated parameters (PLAN G494); the substitution, and the result (ValueCallResult). P9: a Closure's result
//   naming its call receiver ρ takes the receiver acquisition's Origin (ReceiverImage), the image of the Closure binder's reserved atom
//   built only where the result names it (CallAtomsOf, PerCallImages); its call result atom, which names a Refuted relation A5 judges in
//   the Closure's body (P8), is published as it is, never an Unbound atom resting on no Binding failure (R4-M118; R7-O1 H); a Closure value
//   returned from its maker's call is called through its Type's signature (ClosureSignature, DECISIONS R4-M159), and declares its
//   definition's (CallPlan.DeclaredSignature); a result still naming an atom of a Closure that does not enclose the call is F1's located
//   Unsupported_Kd.
// - Inputs: the call, the callee's call signature (TryCallable; a Closure Type's definition, BoundClosure.Of) and its receiver mode.
//   Outputs: the call's plan (CallPlan.SetValue, SetCaseReceiver) and its result Type, or a failure at the call.
// - Invariants: a bound call's result names no input or reserved atom of its callee's binder (otherwise the call is Unsupported_Kd);
//   the images are this call's alone. A value call selects no callee, so an argument's failed acquisition (a bare Place without its
//   spelling) leaves its Type known and stops none of the call's other judgments (SPEC 3.8, 10, 23.1.2): the other arguments'
//   acquisitions, the solve, each fit's Origin rows, the input premises and the clauses; the call then rests on that record and forms
//   no result. A failed callee stops the call: its record reads no argument, and must not rest on one. A named function, method or
//   constructor call is not one: there a bare Place's Copy proof is its candidate's applicability (SPEC 10.1 step 5), so no candidate
//   is selected and the relations judged after selection rest on it (DECISIONS BU-96).
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
        Foreign = 2,
    }

    // What one formation of a value call's callee acquisition gives (FormValueCallee): formed; formed, lending the callee's or the handle's
    // Place, whose static root AcquireValueCallee judges in the call's mode (FailStaticBorrow); or the failure whose record it makes at the
    // call on its callee: a write, an object authority, a required transfer or a located limit. A BindingFailure cannot carry it:
    // AccessFailure's Unsupported is not RecordLocated's limit.
    private enum CalleeOutcome : byte
    {
        Formed,
        LendsCallee,
        LendsHandle,
        Write,
        ObjectAuthority,
        TransferRequired,
        Located,
    }

    // The checked plan of a callable value's call (SetValue below) of the current pass (IsCurrent); null before selection and for a named
    // callee's call.
    internal CallPlan? ValueCallOf(InvocationKoto call) => this.IsCurrent(call) && call.CallStorage is { Kind: CalleeKind.Value } plan ? plan : null;

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

    // SPEC 15.6.4, 8.10: the binder of a callee's own per-call input Origins: its Item or closure declaration, otherwise the
    // Function Type of the signature, whose own inputs are the only Input atoms it binds at an outer layer.
    private static Koto? CalleeBinder(BoundType? callee, BoundType signature)
    {
        return callee is not null && SignatureOwner(callee) is { } declaration ? declaration : signature.Binder;
    }

    // One node's atom (CallAtomsOf); a meet node is none, its operands being nodes of their own.
    private static CallAtoms CallAtomOf(BoundOrigin node, FunctionKoto closure, Koto call)
    {
        if (node.Kind is not (OriginKind.Projection or OriginKind.Input or OriginKind.Parameter) ||
            node.Binder is not FunctionKoto { IsAnonymous: true } binder)
        {
            return CallAtoms.None;
        }

        if (!ReferenceEquals(binder, closure))
        {
            return IsWithin(call, binder) ? CallAtoms.None : CallAtoms.Foreign;
        }

        return node.Kind != OriginKind.Projection ? CallAtoms.None : node.Slot switch
        {
            ClosureValueSlot => CallAtoms.Receiver,
            _ => CallAtoms.None,
        };
    }

    // An object callee Place the payload lowering reaches only through a reference (the callee's own stored layers included), an object
    // layer or an element; a binding, an owned Field path below one, an object view value and a Temporary Value are reached directly.
    private bool IndirectObjectCallee(Koto handle)
    {
        var node = KotoHelper.UnwrapParentheses(handle);
        if (!ReferenceEquals(ComparisonReferent(handle.BoundType!), handle.BoundType))
        {
            return true;
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.ObjRef or SemanticsKind.ObjUniq } ||
            (node is not (IdentifierNameKoto or MemberAccessKoto or IndexKoto or ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow }) &&
                !(node is BinaryKoto element && this.IsSyntax(element))))
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

    // Whether a declared signature names a per-call input or a universal Origin of `binder`: a test of its Origin summary's Nodes, which
    // raw layers' Origins are not (Binding.OriginSummaries).
    private bool HasUnsubstitutedInput(BoundType type, Koto binder)
    {
        var nodes = this.SummaryOf(type).Nodes;
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Kind is OriginKind.Input or OriginKind.Parameter && ReferenceEquals(nodes[i].Binder, binder))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a result names an environment binding of the closure `function` or its call receiver Origin ρ, so it depends on the
    // receiver (SPEC 15.8.2, 7.7.4; DECISIONS R4-M126 P12): a test of its Origin summary's Nodes.
    private bool HasEnvironmentOrigin(BoundType type, Koto function)
    {
        var nodes = this.SummaryOf(type).Nodes;
        for (var i = 0; i < nodes.Length; i++)
        {
            if (ReferenceEquals(nodes[i].Binder, function) && ReceiverAtom(nodes[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The atoms of Closures a value call's result names in its Origin positions, raw layers included (SPEC 15.8.2, 5.7; DECISIONS R4-M152,
    // R4-M159): its callee Closure's call receiver ρ and call result, each of which the call substitutes, and F1's located limit, an atom
    // of another Closure that does not enclose the call, which no image reached: a Closure value returned from its maker's call reads that
    // call's images through its Type's Origin arguments (ClosureSignature), so only a value that reached the call without passing its
    // maker's call can still name one (the owner's 「2c-5 F1: (c')」). The atoms of its Origin summary's Nodes and RawNodes
    // (Binding.OriginSummaries), a meet's operands each a node.
    private CallAtoms CallAtomsOf(BoundType type, FunctionKoto closure, Koto call)
    {
        var summary = this.SummaryOf(type);
        var atoms = CallAtoms.None;
        for (var i = 0; i < summary.Nodes.Length; i++)
        {
            atoms |= CallAtomOf(summary.Nodes[i], closure, call);
        }

        for (var i = 0; i < summary.RawNodes.Length; i++)
        {
            atoms |= CallAtomOf(summary.RawNodes[i], closure, call);
        }

        return atoms;
    }

    // SPEC 7.7.3, 15.8.2 (DECISIONS R4-M152): the Origin of a Closure call's receiver acquisition, which a result naming ρ takes: the
    // acquisition AcquireValueCallee publishes (a payload's borrow, FormPayloadReceiver; a reference callee's Reborrow through its layers);
    // any owned callee's Borrow (PlaceOrigin), for the Closure literal itself its own temporary, which is ρ's atom in its evaluating body.
    private BoundOrigin ReceiverImage(InvocationKoto call, in BoundArgumentOperation acquisition)
        => acquisition.AdaptedType?.Origin ?? this.PlaceOrigin(call.Method);

    // SPEC 8.10, 15.8.2: the Closure Type a callable value's calls read, the one TryCallable reads its signature from and a value call
    // declares (R4-M159): the value without its acquisition layers (CallableCore), else its normalized Callable contract Type; null for
    // any other callable.
    private BoundType? CallableClosure(BoundType type, BindingScope scope)
    {
        var owner = CallableCore(type)!;
        if (BoundClosure.Of(owner) is not null)
        {
            return owner;
        }

        var normalized = owner.Kind == BoundTypeKind.Function || this.FunctionItemSignature(owner) is not null
            ? owner : this.CallableContractType(owner, scope);
        return ReferenceEquals(normalized, owner) ? null : this.CallableClosure(normalized, scope);
    }

    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver) => this.TryCallable(type, scope, out signature, out receiver, out _);

    // SPEC 7.3.3, 3.4.3: a callee is called below its acquisition layers (CallableCore) and an object layer in every case (ObjectCases).
    private BoundType CalledThrough(BoundType type, BindingScope scope)
    {
        var core = CallableCore(type)!;
        return this.ObjectCases(core, scope, out var view) != SemanticsMask.None ? view : core;
    }

    // SPEC 8.10, 10.5: the one call signature of a callable value. Distinct Callable signatures on one F are call candidates
    // (`several`); with them there is no single signature S. A Closure Type's signature and receiver are its definition's
    // (BoundClosure.Of), whatever its literal's creation outcome (DECISIONS R5-M63, R4-M152).
    private bool TryCallable(BoundType type, BindingScope scope, out BoundType signature, out SemanticsKind receiver, out bool several)
    {
        several = false;
        var owner = this.CalledThrough(type, scope);
        if (this.FunctionItemSignature(owner) is { } itemSignature)
        {
            signature = this.CallableContractType(itemSignature, scope); // in the call's view, as a Callable premise's signature (ruling 268)
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (owner.Kind == BoundTypeKind.Function)
        {
            signature = owner;
            receiver = SemanticsKind.Ref;
            return true;
        }

        if (this.CallableClosure(owner, scope) is { } closureType)
        {
            signature = this.ClosureSignature(closureType)!;
            receiver = BoundClosure.Of(closureType)!.Receiver;
            return true;
        }

        var normalizedOwner = this.CallableContractType(owner, scope);
        if (!ReferenceEquals(normalizedOwner, owner))
        {
            return this.TryCallable(normalizedOwner, scope, out signature, out receiver, out several);
        }

        // SPEC 8.10, 8.3.2: the Callable Constraints on the callee are the available Callable rows its premises supply, at every
        // depth and whatever its spelling (ContractSolver's FirstRows).
        signature = null!;
        receiver = SemanticsKind.Owner;
        var start = this.premiseRows.Count;
        this.FirstRows(owner, scope, this.premiseRows);
        for (var i = start; i < this.premiseRows.Count; i++)
        {
            if (this.premiseRows[i] is not { Available: true, Clause: { Kind: ConstraintKind.Callable } fact })
            {
                continue;
            }

            var currentSignature = this.CallableContractType(fact.RequiredType!, scope);
            if (signature is not null && !this.SameCallableSignature(signature, currentSignature))
            {
                several = true;
                break;
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

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        return !several && signature is not null;
    }

    // SPEC 7.3.3 (object-kind column and check 1), 7.7.3, 13.5.5: a callee reached through an obj/rc/arc handle or an objref/objuniq view,
    // and a written payload follow, is acquired as its complete payload, `h@follow@ref` for a Shared call and `h@follow@uniq` for an
    // Exclusive one, through the same checked projection as the explicit spelling; the payload offers no Take.
    // The checks follow SPEC 7.3's order: the operation's legality and authority (a payload offers no Take and rc/arc/objref only Read),
    // then the acquisition, whose unimplemented forms are located limits; a Shared call needs no Sealed proof, as an open View's
    // shared receiver does not (SPEC 13.5.5). A handle Place is lent as SPEC lends it (LendsHandle), its static root failing the handle
    // alone (LendsHandle's outcome, which AcquireValueCallee judges), the call bound on (SPEC 23.1.2).
    private CalleeOutcome FormPayloadReceiver(Koto handle, SemanticsKind receiver, BindingScope scope, out BoundArgumentOperation operation)
    {
        operation = default;
        var handleType = this.ContractType(handle.BoundType!, scope);
        // The handle's or view's cases (ObjectCases; a pair layer's too) and the authority of its own layer and path (PlaceAuthority).
        var objects = this.ObjectCases(handleType, scope, out var payload);
        var shared = this.SharesOwnLayer(handle) || this.ReachedThroughShared(handle);
        var view = (objects & ~SemanticsMask.ObjectBorrow) == 0;
        if (receiver != SemanticsKind.Ref && shared)
        {
            return CalleeOutcome.ObjectAuthority;
        }

        if (receiver == SemanticsKind.Owner || (receiver == SemanticsKind.Uniq && !view && this.IsBarePlace(handle) && !this.LendsPlace(handle, scope, true, out _)))
        {
            return CalleeOutcome.Write;
        }

        var unsupported = this.IndirectObjectCallee(handle) || (!view && !this.LendsHandle(handle, this.LendFold(handle, scope, receiver == SemanticsKind.Uniq), out _));
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
            return CalleeOutcome.Located;
        }

        operation = new(handle, handleType, adapted, ArgumentOperationKind.PayloadProjection, ArgumentAdaptation.CrossSemanticsBorrow, AdaptedType: adapted);
        return view ? CalleeOutcome.Formed : CalleeOutcome.LendsHandle;
    }

    // SPEC 7.3, 7.7.3: the callee of a value call is a Receiver Expression, acquired in the call's mode on the path of SPEC 3.4.3, which
    // continues below every ref and uniq layer (ComparisonReferent) and stops at a handle's or view's View Target (CallableCore): a
    // reference callee as its Reborrow through its layers, in a Shared call through a concrete handle or view below them too (SPEC 13.5.5:
    // its payload is read through the layer); any other handle or view as its payload (FormPayloadReceiver, whose limit is a handle below a
    // reference); owned only by the value read of a Copy closure; a Shared or Exclusive call borrows an owned callee Place as SPEC lends it
    // (LendsPlace), exclusively only where its lending point is exclusively writable, whose static root is the located limit until P42,
    // final here as a value call selects no other callee: it fails the callee alone, the call bound on (SPEC 23.1.2; FailStaticBorrow).
    // False when the acquisition fails, its record made: the one place a callee's records are made, of the formation (FormValueCallee),
    // then the static root of the Place it lends, once, in the call's mode.
    private bool AcquireValueCallee(InvocationKoto call, BindingScope scope, SemanticsKind receiver, out BoundArgumentOperation receiverOperation)
    {
        switch (this.FormValueCallee(call, scope, receiver, out receiverOperation))
        {
            case CalleeOutcome.Write:
                this.FailWrite(call, call.Method);
                return false;
            case CalleeOutcome.ObjectAuthority:
                this.FailObjectAuthority(call, call.Method);
                return false;
            case CalleeOutcome.TransferRequired:
                this.FailAcquisition(call, BindingFailure.TransferRequired, call.Method);
                return false;
            case CalleeOutcome.Located:
                this.RecordLocated(call, call.Method.Span);
                return false;
            case CalleeOutcome.LendsCallee:
                _ = this.FailStaticBorrow(call.Method, ArgumentOperationKind.Borrow, receiver);
                return true;
            case CalleeOutcome.LendsHandle:
                _ = this.FailStaticBorrow(receiverOperation.Source!, ArgumentOperationKind.PayloadProjection, receiver);
                return true;
            default: // Formed.
                return true;
        }
    }

    // The callee's acquisition in a receiver mode as AcquireValueCallee's contract states it, formed only: it records nothing, and returns
    // what it formed or the failure whose record AcquireValueCallee makes, each check in SPEC 7.3's order (authority, legality, then the
    // acquisition).
    private CalleeOutcome FormValueCallee(InvocationKoto call, BindingScope scope, SemanticsKind receiver, out BoundArgumentOperation receiverOperation)
    {
        var receiverType = call.Method.BoundType!;
        receiverOperation = default;
        var terminal = ComparisonReferent(receiverType, out var layers);
        var callable = CallableCore(receiverType)!;
        var read = layers != 0 && receiver == SemanticsKind.Ref && !ReferenceEquals(callable, terminal);
        var handle = !read && this.ObjectCases(terminal, scope, out _) != SemanticsMask.None ? call.Method
            : receiver != SemanticsKind.Owner && KotoHelper.UnwrapParentheses(call.Method) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } follow ? follow.Left : null;
        if (handle is not null)
        {
            return this.FormPayloadReceiver(handle, receiver, scope, out receiverOperation);
        }

        if (receiver == SemanticsKind.Owner)
        {
            // SPEC 7.3 (owning row), 3.5.3: a reference supplies an owning receiver only by the value read of a read Type, which a
            // callable value never is; `r@follow()` Copies a Copy referent.
            if (layers != 0)
            {
                return CalleeOutcome.Write;
            }

            // SPEC 7.7.3: a Consuming call Copies a Copy closure; a Non-Copy closure Place needs c@move().
            return receiverType.Semantics == SemanticsKind.Owner && this.IsBarePlace(call.Method) && this.ProveCopy(receiverType, this.ConstraintScope(call)) != ConstraintProof.Proven
                ? CalleeOutcome.TransferRequired : CalleeOutcome.Formed;
        }

        if (layers != 0)
        {
            // SPEC 7.7.3, 7.3.3, 3.5.1, 15.6.3 (DECISIONS BU-27): a reference callee is a borrow value, acquired as `p@follow@ref` or
            // `p@follow@uniq` on the path through every layer: one reference in the call's mode to the Callable the layers reach, of the
            // Origin they give (ReferentOrigin, SPEC 10.2), named as every Reborrow is, exclusive only where the path grants exclusive
            // capability (ReferentAuthority: `ref/F` supplies no Exclusive call). One layer in its own mode over the Callable itself is the
            // bare acquisition (SPEC 3.5): a `ref` Copied (a value operation, as AdaptInput's), a stored `uniq` Reborrowed. The MIR builder
            // types the acquisition by the operation's AdaptedType.
            if (receiver == SemanticsKind.Uniq && this.ReferentAuthority(call.Method) == SemanticsKind.Ref)
            {
                return CalleeOutcome.Write;
            }

            var reborrow = this.types.Reference(receiver, callable, this.ReferentOrigin(call.Method, receiverType, layers));
            var own = layers == 1 && receiverType.Semantics == receiver && !read;
            receiverOperation = own && receiver == SemanticsKind.Ref
                ? new(call.Method, receiverType, null, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, AdaptedType: reborrow)
                : new(call.Method, receiverType, null, ArgumentOperationKind.Reborrow, own ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow, AdaptedType: reborrow);
            return CalleeOutcome.Formed;
        }

        // SPEC 7.3, 7.7.3: the callee is a Receiver Expression, acquired exclusively without a spelling when its lending point is
        // exclusively writable; a let-bound closure Place is never Copied instead.
        if (receiver == SemanticsKind.Uniq && receiverType.Semantics == SemanticsKind.Owner && this.IsBarePlace(call.Method) && !this.LendsPlace(call.Method, scope, true, out _))
        {
            return CalleeOutcome.Write;
        }

        return receiverType.Semantics == SemanticsKind.Owner ? CalleeOutcome.LendsCallee : CalleeOutcome.Formed;
    }

    private BoundType? BindValueCall(InvocationKoto call, BindingScope scope, BoundType signature, SemanticsKind receiver = SemanticsKind.Ref)
    {
        // A function value has no Type parameters of its own, so explicit Type arguments select nothing (SPEC 7.7, 12.5.2);
        // the receiver checks below read the bound callee, which a generic application never is.
        if (call.Method is GenericsKoto)
        {
            return this.Fail(call, BindingFailure.NoApplicableCandidate);
        }

        // The callee's failure is the call's own record, which reads no argument: an argument judged after it would become its
        // prerequisite (SPEC 23.1.2), so the arguments are then bound on their own, outside the call's consultation (CompleteUnselected).
        if (!this.AcquireValueCallee(call, scope, receiver, out var receiverOperation))
        {
            return null;
        }

        // SPEC 7.7.3, 8.15.2 (PLAN G13): the Closure Type whose Receiver gave the call's mode (TryCallable's), which the call declares
        // (R4-M159) and which decides its acquisition by case where it is Exclusive only in the admitted cases where a capture Reborrows
        // (CaseFreeReceiver Shared): each case then acquires the callee as that Type's case receiver gives it, the Shared acquisition formed
        // beside the Exclusive one. That formation records nothing and cannot fail where the Exclusive one was formed (a shared lending
        // holds wherever an exclusive one does, SPEC 15.6.2; its static root is judged once, above), so its failure is a broken contract
        // (DECISIONS BU-102).
        var closureType = this.CallableClosure(this.CalledThrough(call.Method.BoundType!, scope), scope);
        var decider = receiver == SemanticsKind.Uniq && closureType is not null && BoundClosure.Of(closureType)!.CaseFreeReceiver == SemanticsKind.Ref ? closureType : null;
        var sharedReceiver = default(BoundArgumentOperation);
        if (decider is not null && this.FormValueCallee(call, scope, SemanticsKind.Ref, out sharedReceiver) is not (CalleeOutcome.Formed or CalleeOutcome.LendsCallee or CalleeOutcome.LendsHandle))
        {
            throw new InvalidOperationException("A by-case value call's Shared callee acquisition fails where its Exclusive one was formed (PLAN G13).");
        }

        var parameters = signature.Components[0];
        var count = ReferenceEquals(parameters, BoundType.Unit) ? 0 : parameters.Components.Count;
        // SPEC 10.1.1 (MapValueCallArguments): the arguments are matched before any part; the record reads no part, so the parts are then
        // bound on their own (CompleteUnselected) and none is its prerequisite.
        if (!MapValueCallArguments(call, signature).Valid)
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
        var rows = this.rowScratch.Rent(count);
        var childSlice = this.indexScratch.Rent(2);
        var childBase = this.childRows.Count;
        Array.Clear(argumentOrigins, 0, inputCount);
        Array.Clear(origins, 0, originCount);
        try
        {
            // R3 (DECISIONS BU-104): the arguments' root rows at the known parameter Types and their child rows, composed once before the
            // parts' completion and judged after it (JudgePositionRows).
            var solution = new SlotSolution { Binding = this, Call = call, Rows = rows.AsSpan(0, count), ChildSlice = ResetSlice(childSlice) };
            var formed = true;
            this.AcquireValueCallArguments(call, scope, parameters, operations.AsSpan(0, count), instantiated.AsSpan(0, count), ref solution, ref formed);

            // Collect the complete parameter structure, including nested slots, through the ordinary call solver, from the evidence parts;
            // a failed part and a waiting one are no evidence (rule W). Scratch inputs hold acquired Types until substitution commits the one
            // selected call's contract.
            var conditioned = ownBinder is FunctionKoto { IsAnonymous: false } named && this.HasOriginConditions(named) ? named : null;
            if (ownBinder is not null && this.HasUnsubstitutedInput(signature, ownBinder))
            {
                this.SolveValueCallOrigins(ownBinder, parameters, instantiated.AsSpan(0, count), origins, argumentOrigins, call, solution, scope);
            }

            // SPEC 10.5.1, 10.5.4, 15.6.4 step 2 (PLAN G494): each waiting part is completed at its instantiated parameter, after the solve,
            // as a named call completes its waiting parts and erasures after selection (CompleteWaiting, CompleteArguments); every taken
            // part's operation takes its instantiated parameter.
            for (var i = 0; i < count; i++)
            {
                if (operations[i] is not { Source: not null, ParameterType: { } declared })
                {
                    continue;
                }

                var parameter = ownBinder is null ? declared :
                    this.SubstituteStoredOrigins(declared, ownBinder, origins.AsSpan(0, originCount), argumentOrigins.AsSpan(0, inputCount));
                if (operations[i].AdaptedType is null)
                {
                    this.AcquireValueCallPart(call, i, parameter, scope, operations, instantiated, ref formed);
                }
                else
                {
                    operations[i] = operations[i] with { ParameterType = parameter };
                }
            }

            // SPEC 15.6.1, 23.1.2 (rule W, DECISIONS BU-79): each taken part's fit to its instantiated parameter. A fit whose structure holds,
            // or a Closure's or Function Item's erasure, is the argument's own record (RecordMismatch; an erasure is decided at its
            // completion, CompleteValueCallPart, which published its failed outcome and recorded a failed condition there); a structural
            // misfit is the call's. Either way the part is a wildcard for the call's other judgments, and the call rests on it.
            Koto? failedFit = null;
            for (var i = 0; i < count; i++)
            {
                if (operations[i] is not { Source: { } at, ParameterType: { } parameter })
                {
                    continue;
                }

                // An input's Borrow of a static root is the located limit, its relations waiting for P42 (FailStaticBorrow).
                if (!this.FailStaticBorrow(at, operations[i].Kind, parameter.Semantics) && !this.CheckTypeUse(instantiated[i], parameter, at, out var failure))
                {
                    var erasure = instantiated[i] is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } && parameter.Kind == BoundTypeKind.Function;
                    if (failure.Kind == ComparisonFailureKind.Shape && !erasure)
                    {
                        this.Fail(call, BindingFailure.NoApplicableCandidate);
                    }
                    else
                    {
                        this.RecordMismatch(at, at, instantiated[i], parameter, failure);
                        failedFit ??= at;
                    }

                    operations[i] = default;
                    formed = false;
                    continue;
                }

                instantiated[i] = parameter;
            }

            // SPEC 15.6.1, 15.6.4 step 3: as at a selected call, each input's instantiated Type is well formed and each clause holds, also
            // where no input is per-call: a written input's well-formedness is its signature's premise, proven at each call (R4-M137).
            // The value call's solve (SolveValueCallOrigins) is its own inference, not its solution's, so these checks read no solved
            // Marked fact (G1128 R1.3 (i)'s residue, D-971): a check over a failed equality's variable does not rest here.
            for (var i = 0; i < count; i++)
            {
                this.ChildGroup(solution, i, out var childStart, out var childEnd);
                if (childStart != childEnd && operations[i] is { Source: { } whole, ParameterType: { } type })
                {
                    this.JudgePositionRows(whole, rows[i] with { Type = type }, solution, solved: null); // R4: by its rows.
                }
                else if (operations[i] is { Source: { } at, AdaptedType: { } adapted, ParameterType: { } parameter })
                {
                    this.RequireInputPremises(at, parameter, parameter, adapted, rows[i].Contribution, solved: null);
                }
            }

            if (conditioned is not null)
            {
                this.JudgeCallClauses(call, conditioned, operations.AsSpan(0, count + 1), origins, argumentOrigins, null, solved: null);
            }

            // A call with a part it did not take (failed, misfitting or not acquired) rests on that part's record and forms no result.
            if (!formed)
            {
                return failedFit is null ? Complete(call, null) : this.CompleteDependent(call, failedFit);
            }

            var result = this.ValueCallResult(
                call, signature, ownBinder, origins.AsSpan(0, originCount), argumentOrigins.AsSpan(0, inputCount), receiverOperation);
            if (result is null)
            {
                return null;
            }

            if (conditioned is not null)
            {
                this.RequireResultPremises(conditioned, result, call); // SPEC 15.3.7: the callee's result premises.
            }

            var inputs = count == 0 ? BoundType.Unit : this.types.Tuple(instantiated.AsSpan(0, count));
            // A Closure callee declares its definition's signature, the world retention reads (R4-M159).
            var declaredSignature = closureType is not null ? this.ClosureDefinitionSignature(closureType)! : signature;
            signature = this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, result]);
            var plan = call.CallStorage ??= new();
            plan.SetValue(call.Method, signature, operations.AsSpan(0, count), declaredSignature, receiverOperation, receiver);
            plan.SetCaseReceiver(decider, sharedReceiver);
            return Complete(call, result);
        }
        finally
        {
            this.rowScratch.Return(rows, clearArray: true);
            this.indexScratch.Return(childSlice);
            this.childRows.RemoveRange(childBase, this.childRows.Count - childBase);
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.typeScratch.Return(instantiated, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(argumentOrigins, clearArray: true);
        }
    }

    // SPEC 15.6.4, 10.5.1 (PLAN G494): the first pass over a value call's arguments. Each is prepared, its rows composed, and a part whose
    // Type is the solve's evidence is completed and adapted at its declared parameter (AcquireValueCallPart); a part that waits for the
    // solve (WaitsForValueCallSolve) keeps an operation without an adapted Type, which BindValueCall completes at its instantiated
    // parameter. Rule W (DECISIONS BU-79; SPEC 23.1.2): a failed part and a misfitting one (the call's record) take no operation and are
    // no evidence; the pass goes on, and `formed` tells the call that it forms no result.
    private void AcquireValueCallArguments(
        InvocationKoto call,
        BindingScope scope,
        BoundType parameters,
        Span<BoundArgumentOperation> operations,
        Span<BoundType> instantiated,
        ref SlotSolution solution,
        ref bool formed)
    {
        for (var i = 0; i < instantiated.Length; i++)
        {
            var source = call.ArgumentNodes[i];
            var parameter = parameters.Components[i];
            if (!this.PrepareArgument(source, scope, default, callableContext: true))
            {
                formed = false;
            }
            else
            {
                solution.Rows[i] = new(i, -1, 0, source, parameter, parameter, this.PartContribution(source)); // Once the part is recorded.
                this.RetainArgumentRows(ref solution, i); // R3: composed while the part is prepared, before its completion.
                if (this.WaitsForValueCallSolve(source, parameter))
                {
                    operations[i] = new(source, null, parameter, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: i);
                }
                else
                {
                    this.AcquireValueCallPart(call, i, parameter, scope, operations, instantiated, ref formed);
                }
            }
        }
    }

    // SPEC 15.6.4, 15.1.5: a prepared argument of a value call completed at `parameter` (CompleteValueCallPart) and adapted to it: its
    // operation and the Type it acquires, which the call's substitution then instantiates; an unfitted literal, which waits for the
    // solve (WaitsForValueCallSolve), adapts as a Literal. An acquisition is deferred, as a payload's is (CompletePayloads): where the
    // argument fits structurally, a bare Place without its spelling is recorded at the call (the first, SPEC 15.1.5) with the Type it
    // supplies kept. A part that fails, or misfits (the call's record), takes no operation (rule W). `formed` turns false unless the
    // part is taken and acquired.
    private void AcquireValueCallPart(
        InvocationKoto call,
        int i,
        BoundType parameter,
        BindingScope scope,
        Span<BoundArgumentOperation> operations,
        Span<BoundType> instantiated,
        ref bool formed)
    {
        var source = call.ArgumentNodes[i];
        operations[i] = default;
        var literal = IsUnfittedLiteral(source);
        var actual = this.CompleteValueCallPart(source, parameter, scope);
        if (actual is null || source.BindingState != BindingState.Resolved)
        {
            formed = false;
            return;
        }

        actual = this.ArgumentType(source, actual);
        this.acquisitionFailure = null;
        if (!this.AdaptInput(source, FixedPattern.Open(parameter), actual, scope, null, null, out var adapted, out var quality, out var kind, deferAcquisition: true) ||
            (this.acquisitionFailure is not null && !this.FitsStructuralPartAt(adapted, parameter, source)))
        {
            this.Fail(call, BindingFailure.NoApplicableCandidate);
            formed = false;
            return;
        }

        if (this.acquisitionFailure is { } acquisition)
        {
            this.FailAcquisition(call, acquisition.Kind, acquisition.Place, acquisition.Object, unresolved: true);
            formed = false;
        }

        // ArgumentType includes the already selected expected adaptation. Retain the original syntax
        // Type as the source identity, as ordinary calls do; ownership applies that adaptation once.
        operations[i] = new(
            source, source.BoundType, parameter, kind, literal ? ArgumentAdaptation.ExpectedTypeFitting : quality, ParameterIndex: i, AdaptedType: adapted);
        instantiated[i] = adapted;
    }

    // SPEC 15.6.4, 15.8.2 (DECISIONS R4-M152): a value call's result. Fresh input Origins and fixed shared capture results retain their
    // complete call contracts; a result over the per-call inputs takes the arguments' Origins, as an ordinary call's does; one written
    // over a fixed Origin of the calling body stays as written. A Closure's result naming its call receiver ρ takes the receiver
    // acquisition's Origin, the image built only where the result names it, and its call result atom stays as published (P8: its
    // relation is A5's record in the Closure's body); one naming another Closure's atom is F1's located limit.
    private BoundType? ValueCallResult(
        InvocationKoto call,
        BoundType signature,
        Koto? ownBinder,
        ReadOnlySpan<BoundOrigin> origins,
        ReadOnlySpan<BoundOrigin> inputs,
        in BoundArgumentOperation acquisition)
    {
        var result = signature.Components[1];
        var closure = ownBinder is FunctionKoto { IsAnonymous: true, ClosureDefinition: not null } binder ? binder : null;
        var atoms = closure is null ? CallAtoms.None : this.CallAtomsOf(result, closure, call);
        if ((atoms & CallAtoms.Foreign) != 0)
        {
            return this.Fail(call, BindingFailure.Unsupported); // F1's located limit (DECISIONS R4-M152): the owner's (c').
        }

        if (ownBinder is null)
        {
            return result;
        }

        var image = (atoms & CallAtoms.Receiver) != 0 ? this.ReceiverImage(call, acquisition) : null;
        var images = new PerCallImages(inputs, image, null);
        return this.LacksImage(result, ownBinder, origins, images)
            ? this.Fail(call, BindingFailure.Unsupported) : this.SubstituteStoredOrigins(result, ownBinder, origins, images);
    }
}
