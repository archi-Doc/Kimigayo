// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] stringComparisons = [];
    private int[] stringReads = [];
    private bool hasStringComparisons;

    private bool PrepareStringComparisons(OwnershipBody body, out string? failure)
    {
        failure = null;
        this.hasStringComparisons = body.StringComparisons.Count != 0 || body.ComparisonLoans.Count != 0;
        if (!body.ValidateComparisonLoans())
        {
            return Fail("Invalid comparison Loan lifetime or conflict.", out failure);
        }

        if (!this.hasStringComparisons)
        {
            return true;
        }

        Grow(ref this.stringComparisons, body.Operations.Count);
        Grow(ref this.stringReads, body.Operations.Count);
        this.stringComparisons.AsSpan(0, body.Operations.Count).Fill(-1);
        this.stringReads.AsSpan(0, body.Operations.Count).Fill(-1);
        for (var i = 0; i < body.ComparisonLoans.Count; i++)
        {
            this.stringReads[body.ComparisonLoans[i].Read] = i;
        }

        for (var i = 0; i < body.StringComparisons.Count; i++)
        {
            var plan = body.StringComparisons[i];
            if ((uint)plan.Operation >= (uint)body.Operations.Count || this.stringComparisons[plan.Operation] >= 0 ||
                body.Operations[plan.Operation].Kind != OwnershipOperationKind.Produce || body.Values[plan.Operation].Kind != OwnershipValueKind.StringComparison ||
                body.Operations[plan.Operation].Source is not BinaryKoto source ||
                !(ReferenceEquals(SignatureType(this, source.Left.BoundType), BoundType.String) || ReferenceTypes.IsString(SignatureType(this, source.Left.BoundType))) ||
                !(ReferenceEquals(SignatureType(this, source.Right.BoundType), BoundType.String) || ReferenceTypes.IsString(SignatureType(this, source.Right.BoundType))) ||
                !ReferenceEquals(ValueType(body, plan.Operation), BoundType.Boolean) ||
                source.Akind != body.Values[plan.Operation].Operator)
            {
                return Fail("Invalid string comparison result plan.", out failure);
            }

            this.stringComparisons[plan.Operation] = i;
        }

        return true;
    }

    private bool LowerStringRead(OwnershipBody body, int id, out string? failure)
    {
        failure = null;
        if (!this.hasStringComparisons || this.stringReads[id] < 0 ||
            (body.IsReachable(id) && (body.GetInputState(id, body.Operations[id].Place) & PlaceState.MustInit) == 0))
        {
            return Fail("String Read requires an initialized Place and a comparison Loan.", out failure);
        }

        return true; // Physical fields are read later, while the inspection Loan still protects them.
    }

    private bool ValidateStringInspection(OwnershipBody body, int id, int place, int loan, Koto operand, int reference)
    {
        // SPEC 13.4: a string reference, or an operand inspected through its shared borrow.
        var inspected = SignatureType(this, ElementAccess.AccessType(operand));
        if (ReferenceTypes.IsString(inspected))
        {
            return loan == -1 && this.ValidateReferenceUse(body, reference, id) && ValuePlace(body.Operations[reference]) == place &&
                ReferenceEquals(body.Operations[reference].Source, KotoHelper.UnwrapParentheses(operand)) && ReferenceEquals(ValueType(body, reference), inspected);
        }

        if (reference >= 0 && (uint)reference < (uint)body.Operations.Count && ReferenceTypes.IsPointer(ValueType(body, reference)))
        {
            // SPEC 5.2: a raw string Place is read in place through its dominating handle address.
            var address = ValueType(body, reference)!;
            return place == -1 && loan == -1 && ReferenceEquals(SignatureType(this, operand.BoundType), BoundType.String) &&
                ReferenceEquals(address.Components[0], BoundType.String) && (!body.IsReachable(id) || this.Dominates(reference, id));
        }

        if (reference != -1)
        {
            return false;
        }

        if ((uint)loan < (uint)body.ComparisonLoans.Count && body.ComparisonLoans[loan].Projection >= 0)
        {
            var inspection = body.ComparisonLoans[loan];
            return inspection.Place == place && inspection.Call is null &&
                ReferenceEquals(body.Operations[inspection.Read].Source, KotoHelper.UnwrapParentheses(operand)) &&
                this.ValidateElementBorrow(body, inspection.Read, id);
        }

        if ((uint)place >= (uint)body.Places.Count || !ReferenceEquals(body.Places[place].Type, BoundType.String) || !this.IsStringStorage(body.Places[place]) ||
            (body.IsReachable(id) && (body.GetInputState(id, place) & PlaceState.MustInit) == 0))
        {
            return false;
        }

        if (body.Places[place].Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter)
        {
            return body.HasComparisonLoan(id, loan) && body.ComparisonLoans[loan].Place == place &&
                ReferenceEquals(body.Operations[body.ComparisonLoans[loan].Read].Source, KotoHelper.UnwrapParentheses(operand)) &&
                (!body.IsReachable(id) || this.Dominates(body.ComparisonLoans[loan].Read, id));
        }

        return loan == -1 && this.IsStringValue(body.Places[place]);
    }

    private EmissionOperand StringOperand(OwnershipBody body, int place, int loan, int reference)
        => reference < 0 ? StringPlaceOperand(body, place, loan)
            : ReferenceTypes.IsPointer(ValueType(body, reference)) ? this.PhysicalOperand(body, reference) : this.ReferenceOperand(body, reference);

    private bool LowerStringComparison(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        if (!this.hasStringComparisons || this.stringComparisons[id] < 0)
        {
            return Fail("Missing string comparison plan.", out failure);
        }

        var plan = body.StringComparisons[this.stringComparisons[id]];
        var source = (BinaryKoto)body.Operations[id].Source;
        if (!this.ValidateStringInspection(body, id, plan.Left, plan.LeftLoan, source.Left, plan.LeftValue) || !this.ValidateStringInspection(body, id, plan.Right, plan.RightLoan, source.Right, plan.RightValue))
        {
            return Fail("String comparison is not protected through its physical reads.", out failure);
        }

        var predicate = body.Values[id].Operator switch
        {
            KotoKind.EqualsEquals => "eq",
            KotoKind.ExclamationEquals => "ne",
            KotoKind.LessThan => "slt",
            KotoKind.LessThanEquals => "sle",
            KotoKind.GreaterThan => "sgt",
            KotoKind.GreaterThanEquals => "sge",
            _ => null,
        };
        if (predicate is null)
        {
            return Fail("Unsupported string comparison operator.", out failure);
        }

        var left = this.StringOperand(body, plan.Left, plan.LeftLoan, plan.LeftValue);
        var right = this.StringOperand(body, plan.Right, plan.RightLoan, plan.RightValue);
        function.AddScalar(predicate is "eq" or "ne" ? EmissionOpcode.StringEquals : EmissionOpcode.StringCompare, id, [left, right], op: predicate);
        return true;
    }
}
