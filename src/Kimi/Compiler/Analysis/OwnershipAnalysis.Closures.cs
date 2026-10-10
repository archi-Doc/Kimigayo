// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int CreateClosure(FunctionKoto source)
    {
        var closure = source.BoundClosure!;
        var mark = this.arguments.Count;
        for (var i = 0; i < closure.Captures.Count; i++)
        {
            var capture = closure.Captures[i];
            var prepared = this.TryDefaultSlot(capture.Source, out var place);
            if (prepared ? place < 0 : !this.body.TrySymbolPlace(capture.Source, this.defaultContext, out place))
            {
                this.Unsupported(source);
                return -1;
            }

            var read = -1;
            var acquisitionKind = capture.Environment.CaptureAcquisition;
            if (acquisitionKind == CaptureAcquisition.Bare && this.body.Places[place].Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 })
            {
                // SPEC 8.9, 8.10: a bare entry of a pair binding Reborrows in the case or instance whose Type is an exclusive
                // reference, the binding's case Type, and Copies in every other case.
                acquisitionKind = CaptureAcquisition.Reborrow;
            }

            if (closure.EnvironmentType is not null && acquisitionKind is CaptureAcquisition.Reborrow or CaptureAcquisition.SharedSlotBorrow or CaptureAcquisition.ExclusiveSlotBorrow)
            {
                // SPEC 7.6.2: the entry initializes its environment binding as `let x = x` (a Reborrow) or `let x = x@ref`
                // and `x@uniq` (a borrow of the outer slot) would; the closure keeps the borrow's Loan (SPEC 15.8.2).
                var borrowed = this.BorrowCapture(source, place, this.Resolve(capture.Environment.Type!, this.Active)!);
                var acquired = this.Place(source, capture.Environment.Type, OwnershipPlaceKind.Temporary, false);
                var borrowedValue = this.Value(borrowed);
                read = this.Emit(OwnershipOperationKind.Consume, source, borrowed, acquired, AcquisitionKind.Move);
                this.SetValue(read, OwnershipValueKind.Alias, [borrowedValue]); // The consumed temporary's one prepared value.
                this.Emit(OwnershipOperationKind.CallEntry, source, acquired);
            }
            else if (closure.EnvironmentType is not null)
            {
                // SPEC 7.6.2: a bare capture Copies and needs definition-side Copy proof; x@move transfers.
                var transfer = capture.Environment.TransferCapture;
                var acquisition = transfer ? AcquisitionKind.Move : this.body.Places[place].Acquisition;
                if (!transfer && acquisition != AcquisitionKind.Copy)
                {
                    if (prepared)
                    {
                        // SPEC 7.2.3: the entry is the default's own error, which its declaration check reports once. A default
                        // never moves a prepared argument, which the pending call still owns, so the call's entry is no moved Place.
                        acquisition = AcquisitionKind.Copy;
                    }
                    else
                    {
                        // A rejected capture is read as the Copy it asks for, so the binding stays initialized and later uses report only
                        // their own problems (SPEC 23.3.6.4).
                        this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired, Capture: i));
                        acquisition = AcquisitionKind.Copy;
                    }
                }

                var acquired = this.Place(source, capture.Environment.Type, OwnershipPlaceKind.Temporary, false);
                read = this.Emit(OwnershipOperationKind.Consume, source, place, acquired, acquisition);
                this.Emit(OwnershipOperationKind.CallEntry, source, acquired);
            }
            else
            {
                read = this.Emit(OwnershipOperationKind.Read, source, place);
            }

            this.arguments.Add(read);
        }

        var result = this.Temporary(source);
        this.SetValue(this.Value(result), OwnershipValueKind.Closure, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.arguments).Slice(mark));
        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        return result;
    }

    // SPEC 7.6.2: the borrow that initializes a capture's environment binding. A stored exclusive reference is Reborrowed
    // through its referent when the environment keeps its Type; a slot borrow adds a reference layer over the binding.
    private int BorrowCapture(FunctionKoto source, int place, BoundType type)
    {
        var stored = this.body.Places[place].Type;
        var borrowValue = ReferenceTypes.IsBorrow(stored);
        if (borrowValue)
        {
            this.Emit(OwnershipOperationKind.Read, source, place);
        }

        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        this.SetValue(operation, OwnershipValueKind.Address, borrowValue ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }

    private int CallValue(InvocationKoto call, BoundValueCall plan)
    {
        var depth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        // SPEC 7.3, 13.5.5.1: an object callee lends its complete payload through the same checked borrow as `h@follow@ref`/`@uniq`,
        // also when the callee is a written object view (`(h@objuniq)()`).
        var payload = plan.ReceiverOperation.Kind == ArgumentOperationKind.PayloadProjection;
        var explicitReceiver = !payload && plan.ReceiverKind == SemanticsKind.Uniq && IsDirectExclusiveBorrow(plan.Receiver);
        var reserved = explicitReceiver || (payload && plan.ReceiverKind == SemanticsKind.Uniq);

        // A common Function value stored in a field or element is called through a shared borrow of that part, as `@ref`
        // would take it, since reading the Non-Copy value out would transfer it.
        var direct = KotoHelper.UnwrapParentheses(plan.Receiver);
        // SPEC 7.6.3, 7.1.1: a user-Indexable element (`m[k]`) and a Place result (`first(xs@ref)`) are such parts too.
        var part = plan.ReceiverKind == SemanticsKind.Ref && plan.ReceiverType.Kind == BoundTypeKind.Function &&
            (direct is MemberAccessKoto || (direct is BinaryKoto element && ElementAccess.IsSyntax(element)) || ElementAccess.IsUserIndex(direct) ||
                (direct is InvocationKoto && ElementAccess.PlaceCallReference(direct) is not null));
        var receiver = explicitReceiver
            ? this.PrepareCallArgument(call, plan.Receiver, new(plan.Receiver, plan.ReceiverType, plan.ReceiverType, ArgumentOperationKind.Reborrow, ArgumentAdaptation.SameSemanticsReborrow))
            : payload ? this.PrepareCallArgument(call, plan.ReceiverOperation.Source!, plan.ReceiverOperation, immediate: plan.ReceiverKind != SemanticsKind.Uniq)
            : part ? this.PrepareCallArgument(call, plan.Receiver, new(plan.Receiver, plan.ReceiverType, this.compilation.Binding.Reference(SemanticsKind.Ref, plan.ReceiverType), ArgumentOperationKind.Borrow, ArgumentAdaptation.CrossSemanticsBorrow), immediate: true)
            : plan.ReceiverKind == SemanticsKind.Uniq && KotoHelper.UnwrapParentheses(plan.Receiver) is IdentifierNameKoto { BoundSymbol: { } binding } && this.body.SymbolPlaces.TryGetValue(binding, out var local)
            ? local // SPEC 15.6.7: the reserved read below acquires an exclusive receiver binding, as a method receiver's entry does.
            : this.Expression(plan.Receiver, plan.ReceiverKind == SemanticsKind.Owner ? PlaceUseKind.Consume : PlaceUseKind.Read);
        if (receiver < 0)
        {
            this.comparisonDepth = depth;
            return -1;
        }

        // Even a temporary has a distinct read marking the receiver Loan's start.
        var reservation = plan.ReceiverKind == SemanticsKind.Uniq && !reserved ? this.NewCallReservation(call) : -1;
        var receiverValue = this.Value(receiver);
        var read = this.Emit(OwnershipOperationKind.Read, plan.Receiver, receiver, reservation: reservation);
        if (ReferenceTypes.IsBorrow(this.body.Places[receiver].Type) && this.body.Places[receiver].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
        {
            this.SetValue(read, OwnershipValueKind.Alias, [receiverValue]);
        }

        if (plan.ReceiverKind != SemanticsKind.Owner)
        {
            var loan = this.BeginSharedLoan(receiver);
            if (this.body.Places[receiver].Type.Kind != BoundTypeKind.Function)
            {
                this.body.ComparisonLoans[loan] = this.body.ComparisonLoans[loan] with { Callable = call, Mode = plan.ReceiverKind == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref };
            }

            if (reservation >= 0)
            {
                this.CompleteCallReservation(reservation, read, loan: loan);
            }
        }

        var mark = this.arguments.Count;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            this.arguments.Add(this.PrepareCallArgument(call, call.ArgumentNodes[i], plan.Arguments[i]));
        }

        if (!reserved && reservation < 0 && ReferenceTypes.IsBorrow(this.body.Places[receiver].Type))
        {
            // A reference receiver is used again at the call, so the Place it borrows stays lent while the arguments run; an
            // exclusive receiver is instead held by its call reservation.
            read = this.Emit(OwnershipOperationKind.Read, plan.Receiver, receiver);
            if (this.body.Places[receiver].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
            {
                this.SetValue(read, OwnershipValueKind.Alias, [receiverValue]);
            }
        }

        // As at an ordinary call, the reserved exclusive arguments activate after the receiver's preparation, immediately before
        // the entries (SPEC 15.6.7), while an owned receiver still holds its environment's Loans.
        this.ActivateCallReservations(call, reservationMark);
        if (plan.ReceiverKind == SemanticsKind.Owner)
        {
            // Preparation owns the receiver until every argument completes. An early return in an argument
            // must still destroy that prepared value; ownership enters the call only at this common boundary.
            // The argument entries follow, contiguous before the call, as an ordinary call's are.
            this.Emit(OwnershipOperationKind.CallEntry, plan.Receiver, receiver);
        }

        var inputStart = this.body.CallInputCount;
        var acquired = true;
        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.arguments[i] < 0)
            {
                acquired = false;
            }
            else
            {
                var entry = this.Emit(OwnershipOperationKind.CallEntry, call, this.arguments[i]);
                this.body.RecordCallInput(entry, i - mark, this.Resolve(plan.Arguments[i - mark].ParameterType, this.Active));
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);

        var invoke = this.Emit(OwnershipOperationKind.Call, call, input: receiver);
        this.body.RecordCall(invoke, new(inputStart, this.body.CallInputCount - inputStart, null, CallResultSource.Environment, read, plan.ReceiverKind is SemanticsKind.Uniq or SemanticsKind.Owner));
        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (!acquired || ReferenceEquals(plan.ReturnType, BoundType.Never))
        {
            this.current = -1;
            this.EndComparisonLoans(depth, call);
            this.comparisonDepth = depth;
            return -1;
        }

        var result = plan.Signature.ResultMode == FunctionResultMode.Value ? this.Temporary(call) : this.ReferenceTemporary(call, plan.ReturnType);
        this.body.OperationStorage[invoke] = this.body.Operations[invoke] with { Place = result };
        // An instance's `(T) -> T` returns its substituted T.
        if (this.Resolve(plan.ReturnType, this.Active) is { } returned && ScalarResult(returned))
        {
            this.SetValue(invoke, OwnershipValueKind.Call, []);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [invoke]);
        }

        this.EndComparisonLoans(depth, call);
        if (this.instance is null)
        {
            this.CallableEffects(call, plan, invoke, result);
        }

        this.comparisonDepth = depth;
        return result;
    }

    // SPEC 8.4.10.7: abstract input effects use the same regions as requirement calls. The callable value, rather than a
    // requirement's receiver, identifies earlier calls covered by preserves results. No result borrows its environment. The plan's
    // declared Types are interpreted in the active context, so a default's replica with a concrete callable records none.
    private void CallableEffects(InvocationKoto call, BoundValueCall plan, int invoke, int result)
    {
        if (this.Resolve(plan.ReceiverType, this.Active) is not { } type)
        {
            return;
        }

        if (type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
        {
            type = type.Components[0];
        }

        if (!AbstractTypes.IsAbstract(type) || this.Resolve(plan.DeclaredSignature, this.Active) is not { } signature)
        {
            return;
        }

        var path = KotoHelper.UnwrapParentheses(plan.Receiver);
        while (path is ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Follow or ConversionBinding.PayloadFollow } conversion)
        {
            path = KotoHelper.UnwrapParentheses(conversion.Left);
        }

        var receiver = this.ValueIdentity(path) with { CallablePath = path };
        var effects = this.body.RequirementEffects ??= new();
        var bounds = true;
        var preserves = this.compilation.Binding.AvailableCallableEffects(type, signature, plan.ReceiverKind, call).Preserves;
        var acquired = plan.ReceiverKind == SemanticsKind.Owner ? type : this.compilation.Binding.Reference(plan.ReceiverKind, type);
        this.RequirementEffect(call, acquired, type, invoke, receiver, receiver, null, ref bounds, ref preserves);
        var mark = effects.Count;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            var argument = plan.Arguments[i];
            var input = argument.Source is { } source ? this.ValueIdentity(source) : new(-1, null);
            this.RequirementEffect(call, this.Resolve(argument.ParameterType, this.Active), type, invoke, input, receiver, null, ref bounds, ref preserves);
        }

        if (result < 0 || effects.Count == mark || this.Resolve(plan.ReturnType, this.Active) is not { } returned || !AbstractTypes.HasAbstractPart(returned) || ReferenceTypes.IndependentResult(returned))
        {
            return;
        }

        var results = this.body.RequirementResults ??= new();
        for (var i = mark; i < effects.Count; i++)
        {
            results.Add(new(invoke, result, effects[i].Region, effects[i].Mode, type, effects[i].Input, receiver));
        }
    }
}
