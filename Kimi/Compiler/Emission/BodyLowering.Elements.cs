// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] elementOperations = [];
    private int[] elementOutputs = [];
    private bool hasElements;

    private static bool IsCopyElement(BoundType type, int depth = 0)
    {
        if (ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit))
        {
            return true;
        }

        if (depth == 64 || type.Semantics != SemanticsKind.Owner || type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray))
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!IsCopyElement(type.Components[i], depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsElementReceiverRead(OwnershipBody body, int id)
    {
        if ((uint)id >= (uint)body.LoanStates.Count || body.LoanStates[id] is not (>= 0 and var loanId) ||
            (uint)loanId >= (uint)body.ComparisonLoans.Count)
        {
            return false;
        }

        var loan = body.ComparisonLoans[loanId];
        var operation = body.Operations[id];
        if (!loan.Access || loan.Read != id || operation.Kind != OwnershipOperationKind.Read || operation.Input != -1 || operation.Place != loan.Place || operation.Source.AttributeChain is not null ||
            (uint)operation.Place >= (uint)body.Places.Count || this.aggregatePlaces[operation.Place] is null)
        {
            return false;
        }

        var place = body.Places[operation.Place];
        return ReferenceEquals(operation.Source.BoundType, place.Type) &&
            (operation.Source is IdentifierNameKoto identifier
                ? identifier.BoundSymbol is { } symbol && body.SymbolPlaces.TryGetValue(symbol, out var root) && root == place.Id
                : ReferenceEquals(operation.Source, place.Source)) &&
            (!body.IsReachable(id) || (body.GetInputState(id, place.Id) & PlaceState.MustInit) != 0);
    }

    private bool PrepareElements(OwnershipBody body, out string? failure)
    {
        failure = null;
        this.hasElements = body.Projections.Count != 0;
        if (!this.hasElements)
        {
            return true;
        }

        Grow(ref this.elementOperations, body.Operations.Count);
        Grow(ref this.elementOutputs, body.Operations.Count);
        this.elementOperations.AsSpan(0, body.Operations.Count).Fill(-1);
        this.elementOutputs.AsSpan(0, body.Operations.Count).Fill(-1);
        for (var i = 0; i < body.Projections.Count; i++)
        {
            var plan = body.Projections[i];
            if ((uint)plan.Operation >= (uint)body.Operations.Count || (uint)plan.Root >= (uint)body.Places.Count ||
                (uint)plan.Loan >= (uint)body.ComparisonLoans.Count || plan.Parent < -1 || plan.Parent >= i || plan.Output < -1 ||
                this.elementOperations[plan.Operation] >= 0 ||
                body.Operations[plan.Operation] is not { Kind: OwnershipOperationKind.ProjectElement, Source: BinaryKoto source, Input: -1 } operation ||
                operation.Place != plan.Root || body.Values[plan.Operation].Kind != OwnershipValueKind.None ||
                !ElementAccess.TryType(source, out var element, out var position) || position != plan.Element ||
                source.AttributeChain is not null || !ReferenceEquals(source.BoundType, element) || this.aggregateLayouts.Get(source.Left.BoundType!) is null)
            {
                return Fail("Element address has no matching source and aggregate shape.", out failure);
            }

            var loan = body.ComparisonLoans[plan.Loan];
            if (!loan.Access || loan.Place != plan.Root || !this.IsElementReceiverRead(body, loan.Read) ||
                (plan.Parent >= 0
                    ? body.Projections[plan.Parent].Root != plan.Root || body.Projections[plan.Parent].Loan != plan.Loan ||
                        body.Projections[plan.Parent].Output != -1 ||
                        !ReferenceEquals(body.Operations[body.Projections[plan.Parent].Operation].Source, KotoHelper.UnwrapParentheses(source.Left))
                    : !ReferenceEquals(body.Operations[loan.Read].Source, KotoHelper.UnwrapParentheses(source.Left))))
            {
                return Fail("Element address is not protected by its receiver Loan.", out failure);
            }

            if (source is IndexKoto)
            {
                if ((uint)plan.Index >= (uint)body.Values.Count || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                    !ReferenceEquals(body.Operations[plan.Index].Source, KotoHelper.UnwrapParentheses(source.Right)) ||
                    body.Operations[plan.Index].Kind is not (OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.Branch))
                {
                    return Fail("Fixed-array indices require an isize value.", out failure);
                }

                this.continuations[plan.Operation] = body.Operations.Count + plan.Operation;
            }
            else if (source is not MemberAccessKoto || plan.Index != -1)
            {
                return Fail("Unexpected element selector.", out failure);
            }

            if (plan.Output >= 0)
            {
                if ((uint)plan.Output >= (uint)body.Operations.Count || this.elementOutputs[plan.Output] >= 0 ||
                    body.Operations[plan.Output] is not { Kind: OwnershipOperationKind.Produce } output ||
                    (uint)output.Place >= (uint)body.Places.Count || !ReferenceEquals(output.Source, source) ||
                    body.Values[plan.Output].Kind != OwnershipValueKind.Element ||
                    body.Places[output.Place].Kind != OwnershipPlaceKind.Temporary ||
                    body.Places[output.Place].Acquisition != AcquisitionKind.Copy ||
                    !ReferenceEquals(body.Places[output.Place].Type, element) || !IsCopyElement(element!))
                {
                    return Fail("Element acquisition requires a fresh Copy result of the selected Type.", out failure);
                }

                this.elementOutputs[plan.Output] = i;
            }

            this.elementOperations[plan.Operation] = i;
        }

        return true;
    }

    private bool LowerElement(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var address = operation.Kind == OwnershipOperationKind.ProjectElement;
        var index = this.hasElements ? (address ? this.elementOperations[id] : this.elementOutputs[id]) : -1;
        if (index < 0)
        {
            return Fail("Element operation is missing its projection plan.", out failure);
        }

        var plan = body.Projections[index];
        var loan = body.ComparisonLoans[plan.Loan];
        if (!body.HasComparisonLoan(id, plan.Loan) ||
            (body.IsReachable(id) && ((body.GetInputState(id, plan.Root) & PlaceState.MustInit) == 0 ||
                !this.Dominates(loan.Read, id) ||
                (address ? (plan.Parent >= 0 && !this.Dominates(body.Projections[plan.Parent].Operation, id)) ||
                    (plan.Index >= 0 && !this.Dominates(plan.Index, id)) : !this.Dominates(plan.Operation, id)))))
        {
            return Fail("Element receiver, index or address is unavailable at acquisition.", out failure);
        }

        var source = (BinaryKoto)body.Operations[plan.Operation].Source;
        var layout = this.aggregateLayouts.Get(source.Left.BoundType!)!;
        var field = layout.IsArray ? 0 : plan.Element;
        var representation = layout.Fields[field];
        if (address)
        {
            var location = -1;
            if (layout.IsArray && !this.TryGetLocation(source, directory, constants, out location))
            {
                return Fail("Element bounds check requires a source location.", out failure);
            }

            // Zero-byte receivers have no slot. Any nonzero child can only be reached after
            // an impossible bounds success (an empty array), so use an explicit null base.
            var receiver = layout.Value.Layout.Size == 0 ? new EmissionOperand(EmissionOperandKind.NullAddress, 0)
                : plan.Parent < 0 ? new EmissionOperand(EmissionOperandKind.SlotAddress, plan.Root)
                : new EmissionOperand(EmissionOperandKind.ElementAddress, body.Projections[plan.Parent].Operation);
            function.AddScalar(
                EmissionOpcode.ElementAddress,
                id,
                [receiver, layout.IsArray ? this.PhysicalOperand(body, plan.Index) : new(EmissionOperandKind.Integer, layout.Offset(field)),
                    new(EmissionOperandKind.Integer, layout.Count), new(EmissionOperandKind.Integer, representation.Layout.Stride)],
                place: body.Operations.Count + id,
                location: location,
                check: layout.IsArray ? ArithmeticCheckKind.Bounds : ArithmeticCheckKind.None,
                representation: representation);
        }
        else
        {
            if (body.IsReachable(id) && (body.GetInputState(id, operation.Place) & PlaceState.MayInit) != 0)
            {
                return Fail("Element Copy result is already initialized.", out failure);
            }

            if (representation.Layout.Size == 0)
            {
                return true;
            }

            if (layout.Children[field] is { } aggregate)
            {
                var start = function.Operands.Count;
                function.Operands.Add(new(EmissionOperandKind.ElementAddress, plan.Operation));
                function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, Place: operation.Place, OperandStart: start, OperandCount: 1, Aggregate: aggregate));
            }
            else
            {
                function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: plan.Operation, representation: representation);
            }
        }

        return true;
    }
}
