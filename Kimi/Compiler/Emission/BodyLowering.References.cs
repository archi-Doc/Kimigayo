// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] referenceRoots = [];
    private int[] callLoanPlans = [];

    private bool PrepareReferences(OwnershipBody body, out string? failure)
    {
        failure = null;
        Grow(ref this.referenceRoots, body.Operations.Count);
        this.referenceRoots.AsSpan(0, body.Operations.Count).Fill(-1);
        Grow(ref this.callLoanPlans, body.Operations.Count);
        this.callLoanPlans.AsSpan(0, body.Operations.Count).Fill(-1);
        for (var i = 0; i < body.CallLoans.Count; i++)
        {
            var plan = body.CallLoans[i];
            if ((uint)plan.Call >= (uint)body.Operations.Count || this.callLoanPlans[plan.Call] >= 0 ||
                body.Operations[plan.Call] is not { Kind: OwnershipOperationKind.Call, Source: InvocationKoto { BoundCall: { } call } } ||
                plan.ResultRequirement != (plan.Result < 0 || ReferenceTypes.IndependentResult(call.ReturnType) ? LoanRequirement.None : LoanRequirement.Ref))
            {
                return Fail("Invalid call result Loan contract.", out failure);
            }

            var noReturn = ReferenceEquals(call.ReturnType, BoundType.Never);
            if (!body.IsReachable(plan.Call))
            {
                noReturn |= this.CannotCompleteCall((InvocationKoto)body.Operations[plan.Call].Source);
            }

            if (noReturn)
            {
                if (plan.Result != -1 || plan.End != -1)
                {
                    return Fail("A noncompleting call cannot deliver a borrowed result or release Loans on normal return.", out failure);
                }
            }
            else if ((uint)plan.Result >= (uint)body.Operations.Count || body.Operations[plan.Result].Kind != OwnershipOperationKind.Produce ||
                !ReferenceEquals(body.Operations[plan.Result].Source, body.Operations[plan.Call].Source) ||
                (body.IsReachable(plan.Call) && (plan.Result != plan.Call + 1 || plan.End != plan.Result + 1)) ||
                (plan.End >= 0 && ((uint)plan.End >= (uint)body.Operations.Count || body.Operations[plan.End].Kind != OwnershipOperationKind.EndComparisonLoans ||
                    !ReferenceEquals(body.Operations[plan.End].Source, body.Operations[plan.Call].Source))))
            {
                return Fail("Call Loans must end after securing the normal result and its Origin dependencies.", out failure);
            }

            this.callLoanPlans[plan.Call] = i;
            if (body.IsReachable(plan.Call) && plan.End >= 0)
            {
                var head = body.LoanInputs[plan.End];
                if (head < 0 || !ReferenceEquals(body.ComparisonLoans[head].Call, body.Operations[plan.Call].Source))
                {
                    return Fail("Normal return has no argument Loans to release.", out failure);
                }

                while (head >= 0 && ReferenceEquals(body.ComparisonLoans[head].Call, body.Operations[plan.Call].Source))
                {
                    head = body.ComparisonLoans[head].Parent;
                }

                if (body.LoanStates[plan.End] != head)
                {
                    return Fail("Normal return must release exactly its own argument Loans.", out failure);
                }
            }
        }

        var parameterStart = 1;
        while (parameterStart < body.Operations.Count && body.Operations[parameterStart].Kind == OwnershipOperationKind.Exit)
        {
            parameterStart++;
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var operation = body.Operations[id];
            var value = body.Values[id];
            var type = ValueType(body, id);
            if (!ReferenceTypes.IsString(type))
            {
                if (value.Kind == OwnershipValueKind.Borrow)
                {
                    return Fail("Borrow has no supported reference result.", out failure);
                }

                continue;
            }

            var place = body.Places[ValuePlace(operation)];
            // Only implicit call/guard inspections need this additional short-lived Loan plan.
            // Stored references, returned references and explicit storage borrows use ordinary
            // pointer values, validated by the common scalar and Origin/Loan paths.
            var alias = value.Kind == OwnershipValueKind.Alias && value.Count == 1 ? Input(body, id, 0) : -1;
            if (value.Kind != OwnershipValueKind.Borrow && value.Kind != OwnershipValueKind.Parameter &&
                !(operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.CallEntry &&
                    alias >= 0 && alias < id && this.referenceRoots[alias] >= 0))
            {
                continue;
            }

            if (place.Kind is not (OwnershipPlaceKind.Parameter or OwnershipPlaceKind.Temporary) || place.Acquisition != AcquisitionKind.Copy)
            {
                return Fail("Reference storage and results are not implemented.", out failure);
            }

            switch (operation.Kind)
            {
                case OwnershipOperationKind.Read when value.Kind == OwnershipValueKind.Borrow:
                    // Candidate provenance and dominance are checked while lowering,
                    // after match preparation has built the semantic dominators.
                    this.referenceRoots[id] = id;
                    break;
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.Parameter:
                    if (place.Kind != OwnershipPlaceKind.Parameter || (ulong)value.Constant >= (ulong)body.Function.Parameters.Count || id != parameterStart + value.Constant ||
                        !ReferenceEquals(place.Source, body.Function.Parameters[(int)value.Constant].Type) || !ReferenceEquals(operation.Source, place.Source) ||
                        !ReferenceEquals(type, SignatureType(this, body.Function.Parameters[(int)value.Constant].Type.BoundType)))
                    {
                        return Fail("Invalid reference parameter definition.", out failure);
                    }

                    this.referenceRoots[id] = id;
                    break;
                case OwnershipOperationKind.Borrow when value.Kind == OwnershipValueKind.Borrow:
                    var loan = id < body.LoanStates.Count ? body.LoanStates[id] : -1;
                    var originSource = Binding.PlaceOriginSource(operation.Source);
                    if ((uint)operation.Place >= (uint)body.Places.Count || place.Kind != OwnershipPlaceKind.Temporary || operation.LoanMode != LoanRequirement.Ref ||
                        operation.Acquisition != AcquisitionKind.None || loan < 0 || body.ComparisonLoans[loan].Read != id || body.ComparisonLoans[loan].Call is null ||
                        (operation.Projection < 0 && (!ReferenceEquals(body.Places[operation.Place].Type, BoundType.String) || !this.ValidateBorrowSource(body, operation))) ||
                        type!.Origin is not { Kind: OriginKind.Projection } origin ||
                        !ReferenceEquals(origin.Binder, Binding.PlaceOriginBinder(originSource)) || origin.Slot != Binding.PlaceOriginSlot(originSource))
                    {
                        return Fail("Reference formation lacks its source and argument Loan.", out failure);
                    }

                    this.referenceRoots[id] = id;
                    break;
                case OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.CallEntry when value.Kind == OwnershipValueKind.Alias:
                    var input = Input(body, id, 0);
                    if ((uint)input >= (uint)id || this.referenceRoots[input] < 0 || !ReferenceEquals(type, ValueType(body, input)) ||
                        (operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition != AcquisitionKind.Copy) ||
                        ValuePlace(body.Operations[input]) != operation.Place)
                    {
                        return Fail("Reference Copy has no matching source value.", out failure);
                    }

                    this.referenceRoots[id] = this.referenceRoots[input];
                    break;
                case OwnershipOperationKind.Cleanup when value.Kind == OwnershipValueKind.None:
                    break;
                default:
                    return Fail("Unsupported reference value operation.", out failure);
            }
        }

        return true;
    }

    private bool ValidateBorrowSource(OwnershipBody body, OwnershipOperation operation)
    {
        var source = body.Places[operation.Place];
        if (source.Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter)
        {
            return operation.Source is IdentifierNameKoto { BoundSymbol: { } symbol } &&
                body.SymbolPlaces.TryGetValue(symbol, out var anchor) && anchor == operation.Place;
        }

        // A transferred or Identity-acquired operand (text@move, text@owner) is borrowed through the
        // operand's own temporary. Storage authorization is checked after the string plans are prepared.
        return source.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && ReferenceEquals(source.Source, ElementAccess.ValueSource(operation.Source));
    }

    private EmissionOperand ReferenceOperand(OwnershipBody body, int value)
    {
        var root = this.referenceRoots[value];
        if (root < 0)
        {
            return this.PhysicalOperand(body, value);
        }

        // A guard candidate may be read under another argument's element Loan.
        // Only a reference formed from a projection uses that projection's address.
        return body.Values[root].Kind switch
        {
            OwnershipValueKind.Parameter => new(EmissionOperandKind.Argument, body.Values[root].Constant),
            _ => StringPlaceOperand(body, body.Operations[root].Place, body.Operations[root].Projection >= 0 ? body.LoanStates[root] : -1),
        };
    }

    private bool ValidateReferenceUse(OwnershipBody body, int value, int at)
    {
        if ((uint)value >= (uint)body.Operations.Count || !ReferenceTypes.IsStringReference(ValueType(body, value)) ||
            (body.IsReachable(at) && !this.Dominates(value, at)))
        {
            return false;
        }

        var root = this.referenceRoots[value];
        if (root < 0)
        {
            return true; // The common value lowering and retained Origin dependencies validate storage.
        }

        // A parameter needs no Loan of this body: its source outlives it.
        if (body.Values[root].Kind == OwnershipValueKind.Parameter || !body.IsReachable(at))
        {
            return true;
        }

        var loan = body.LoanStates[root];
        if (body.Operations[root].Projection >= 0)
        {
            return this.ValidateElementBorrow(body, root, at);
        }

        if (body.Operations[root].Kind == OwnershipOperationKind.Read)
        {
            while (loan >= 0 && body.ComparisonLoans[loan].Guard != body.OperationSteps[root])
            {
                loan = body.ComparisonLoans[loan].Parent;
            }
        }

        return body.HasComparisonLoan(at, loan);
    }

    private bool LowerReference(OwnershipBody body, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (operation.Kind == OwnershipOperationKind.Produce)
        {
            return this.referenceRoots[id] == id;
        }

        if (operation.Place < 0 || (body.IsReachable(id) && (body.GetInputState(id, operation.Place) & PlaceState.MustInit) == 0))
        {
            return Fail("Reference source is not initialized.", out failure);
        }

        if (operation.Kind == OwnershipOperationKind.Borrow)
        {
            return this.referenceRoots[id] == id && this.IsStringStorage(body.Places[operation.Place]);
        }

        if (body.Values[id].Kind == OwnershipValueKind.Borrow)
        {
            return this.ValidateCandidateRead(body, id, out failure);
        }

        return this.ValidateReferenceUse(body, Input(body, id, 0), id) || Fail("Reference source does not dominate its use or has expired.", out failure);
    }
}
