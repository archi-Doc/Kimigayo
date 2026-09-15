// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] elementOperations = [];
    private int[] elementOutputs = [];
    private bool hasElements;

    private static bool ConsecutiveElementEdge(OwnershipBody body, int from, int to)
    {
        var edge = body.EdgeHeads[from];
        return edge >= 0 && body.Edges[edge] is { Kind: OwnershipEdgeKind.Normal, Next: -1 } next && next.To == to &&
            body.IncomingEdges[to] == edge && body.IncomingCounts[to] == 1;
    }

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
        if (!loan.Access || loan.Read != id || operation.Kind != OwnershipOperationKind.LocateReceiver || operation.Input != -1 || operation.Place != loan.Place || operation.Source.AttributeChain is not null ||
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
            return body.ElementUpdates.Count == 0 || Fail("Element updates require projection plans.", out failure);
        }

        Grow(ref this.elementOperations, body.Operations.Count);
        Grow(ref this.elementOutputs, body.Operations.Count);
        this.elementOperations.AsSpan(0, body.Operations.Count).Fill(-1);
        this.elementOutputs.AsSpan(0, body.Operations.Count).Fill(-1);
        for (var i = 0; i < body.Projections.Count; i++)
        {
            var plan = body.Projections[i];
            if ((uint)plan.Operation >= (uint)body.Operations.Count || (uint)plan.Root >= (uint)body.Places.Count ||
                (uint)plan.Loan >= (uint)body.ComparisonLoans.Count || plan.Parent < -1 || plan.Parent >= i || plan.Output < -1 || plan.Write < -1 ||
                plan.Exclusive < -1 || (plan.Exclusive >= 0 && ((uint)plan.Exclusive >= (uint)body.ComparisonLoans.Count ||
                    body.ComparisonLoans[plan.Exclusive].Mode != LoanRequirement.Uniq || body.ComparisonLoans[plan.Exclusive].Projection != i || plan.Output < 0)) ||
                plan.Update < -1 || (plan.Update >= 0 && (plan.Update >= body.ElementUpdates.Count || plan.Output < 0 || plan.Write < 0 || plan.Exclusive < 0)) ||
                (plan.Output >= 0 && plan.Write >= 0 && plan.Update < 0) ||
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
                        body.Projections[plan.Parent].Output != -1 || body.Projections[plan.Parent].Write != -1 ||
                        !ReferenceEquals(body.Operations[body.Projections[plan.Parent].Operation].Source, KotoHelper.UnwrapParentheses(source.Left))
                    : !ReferenceEquals(body.Operations[loan.Read].Source, KotoHelper.UnwrapParentheses(source.Left))))
            {
                return Fail("Element address is not protected by its receiver Loan.", out failure);
            }

            if (source is IndexKoto)
            {
                if ((uint)plan.Index >= (uint)body.Values.Count || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                    !ReferenceEquals(body.Operations[plan.Index].Source, ElementAccess.ValueSource(source.Right)) ||
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

            if (plan.Write >= 0)
            {
                if (!this.PrepareElementWrite(body, plan, source, element!, out failure))
                {
                    return false;
                }

                this.elementOutputs[plan.Write] = i;
            }

            this.elementOperations[plan.Operation] = i;
        }

        for (var i = 0; i < body.ElementUpdates.Count; i++)
        {
            var update = body.ElementUpdates[i];
            if ((uint)update.Projection >= (uint)body.Projections.Count || body.Projections[update.Projection].Update != i)
            {
                return Fail("Element update has no unique projection owner.", out failure);
            }
        }

        return true;
    }

    private bool PrepareElementWrite(OwnershipBody body, OwnershipProjection plan, BinaryKoto target, BoundType element, out string? failure)
    {
        failure = null;
        if ((uint)plan.Write >= (uint)body.Operations.Count || this.elementOutputs[plan.Write] >= 0 ||
            body.Operations[plan.Write] is not { Kind: OwnershipOperationKind.WriteElement } write ||
            write.Place != plan.Root || (uint)write.Input >= (uint)body.Places.Count || write.Input == plan.Root ||
            write.Acquisition != AcquisitionKind.None || write.Placement != PlacementKind.None || write.LoanMode != LoanRequirement.None ||
            write.Source.AttributeChain is not null || ElementAccess.WritableRoot(target) is not { } root ||
            !ReferenceEquals(root.BoundSymbol, body.Operations[body.ComparisonLoans[plan.Loan].Read].Source.BoundSymbol) ||
            body.Places[plan.Root] is not { Kind: OwnershipPlaceKind.Local, Mutable: true } ||
            body.Places[write.Input] is not { Kind: OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result, Acquisition: AcquisitionKind.Copy } input ||
            !ReferenceEquals(input.Type, element) || !IsCopyElement(element) ||
            (IsScalar(element) ? body.Values[plan.Write] is not { Kind: OwnershipValueKind.Alias, Count: 1 }
                : body.Values[plan.Write].Kind != OwnershipValueKind.None))
        {
            return Fail("Element replacement requires writable local storage and a secured Copy input of its exact Type.", out failure);
        }

        var source = write.Source;
        var value = IsScalar(element) ? Input(body, plan.Write, 0) : -1;
        if (IsScalar(element) && ((uint)value >= (uint)body.Operations.Count ||
            ValuePlace(body.Operations[value]) != write.Input || !ReferenceEquals(ValueType(body, value), element)))
        {
            return Fail("Element replacement has no matching input value.", out failure);
        }

        var last = plan.Operation;
        if (plan.Update >= 0)
        {
            if (!this.PrepareElementUpdate(body, plan, target, element, value, out failure))
            {
                return false;
            }

            var release = plan.Write + 1;
            if (plan.Exclusive < 0 || release >= body.Operations.Count || plan.Write != value + 1 ||
                body.Operations[release] is not { Kind: OwnershipOperationKind.EndComparisonLoans, Place: -1, Input: -1 } ||
                !ReferenceEquals(body.Operations[release].Source, source) || body.LoanInputs[release] != plan.Exclusive ||
                body.LoanStates[release] != body.ComparisonLoans[plan.Loan].Parent ||
                !body.HasComparisonLoan(plan.Write, plan.Exclusive) ||
                !ConsecutiveElementEdge(body, value, plan.Write) || !ConsecutiveElementEdge(body, plan.Write, release))
            {
                return Fail("Element update must retain its exclusive Loan through the store and release it afterward.", out failure);
            }

            return true;
        }
        else if (source is not BinaryKoto { Akind: KotoKind.Equals } assignment ||
            !ReferenceEquals(KotoHelper.UnwrapParentheses(assignment.Left), target) || !ReferenceEquals(source.BoundType, BoundType.Unit) ||
            !ReferenceEquals(input.Source, ElementAccess.ValueSource(assignment.Right)) ||
            (IsScalar(element) && value >= body.ComparisonLoans[plan.Loan].Read))
        {
            return Fail("Simple element assignment must secure its RHS before locating the destination.", out failure);
        }

        // End only this access Loan, immediately before the store, with no intervening
        // user operation or alternate edge. Other active Loans keep their conflict checks.
        var end = plan.Write - 1;
        if (end <= 0 || last != end - 1 ||
            body.Operations[end] is not { Kind: OwnershipOperationKind.EndComparisonLoans, Place: -1, Input: -1 } ||
            !ReferenceEquals(body.Operations[end].Source, source) || body.LoanInputs[end] != plan.Loan ||
            body.LoanStates[end] != body.ComparisonLoans[plan.Loan].Parent || body.LoanInputs[plan.Write] != body.LoanStates[end] ||
            !ConsecutiveElementEdge(body, last, end) || !ConsecutiveElementEdge(body, end, plan.Write))
        {
            return Fail("Element replacement must immediately follow its own access Loan release.", out failure);
        }

        return true;
    }

    private bool PrepareElementUpdate(OwnershipBody body, OwnershipProjection plan, BinaryKoto target, BoundType type, int value, out string? failure)
    {
        failure = null;
        var update = body.ElementUpdates[plan.Update];
        var source = body.Operations[plan.Write].Source;
        var op = ElementAccess.UpdateOperator(source.Akind);
        var unary = source is UnaryKoto;
        if (op == KotoKind.Invalid || !type.IsNumeric || !IsScalar(type) || (unary && !type.IsInteger) ||
            !ReferenceEquals(source.BoundType, unary ? type : BoundType.Unit) ||
            !ReferenceEquals(KotoHelper.UnwrapParentheses(source is UnaryKoto increment ? increment.Operand : ((BinaryKoto)source).Left), target) ||
            value != update.Computation || value <= plan.Output ||
            body.Operations[value] is not { Kind: OwnershipOperationKind.Produce, Input: -1 } computation ||
            !ReferenceEquals(computation.Source, source) || !ReferenceEquals(body.Places[computation.Place].Source, source) ||
            body.Values[value] is not { Kind: OwnershipValueKind.Binary, Count: 2 } calculation || calculation.Operator != op ||
            Input(body, value, 0) != plan.Output || Input(body, value, 1) != update.Right ||
            update.Right <= plan.Output || update.Right >= value ||
            !ConsecutiveElementEdge(body, plan.Operation, plan.Output))
        {
            return Fail("Element update must calculate from its own single Copy read and RHS.", out failure);
        }

        var rightType = ValueType(body, update.Right);
        if (op is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan ? rightType?.IsInteger != true : !ReferenceEquals(rightType, type))
        {
            return Fail("Element update RHS has an incompatible numeric Type.", out failure);
        }

        if (unary ? body.Operations[update.Right].Kind != OwnershipOperationKind.Produce ||
            !ReferenceEquals(body.Operations[update.Right].Source, source) || body.Values[update.Right].Kind != OwnershipValueKind.Constant || body.Values[update.Right].Constant != 1
            : !ReferenceEquals(body.Operations[update.Right].Source, ElementAccess.ValueSource(((BinaryKoto)source).Right)))
        {
            return Fail("Element update has no matching RHS or increment constant.", out failure);
        }

        if ((uint)update.Result >= (uint)body.Operations.Count || update.Result != plan.Write + 2 ||
            body.Operations[update.Result] is not { Kind: OwnershipOperationKind.Produce, Input: -1 } result ||
            (uint)result.Place >= (uint)body.Places.Count || body.Places[result.Place].Kind != OwnershipPlaceKind.Temporary ||
            !ReferenceEquals(result.Source, source) || !ReferenceEquals(ValueType(body, update.Result), source.BoundType) ||
            !ConsecutiveElementEdge(body, plan.Write + 1, update.Result) ||
            (unary ? body.Values[update.Result] is not { Kind: OwnershipValueKind.Alias, Count: 1 } ||
                Input(body, update.Result, 0) != (source.Akind is KotoKind.PostfixIncrement or KotoKind.PostfixDecrement ? plan.Output : value)
                : body.Values[update.Result].Kind != OwnershipValueKind.None))
        {
            return Fail("Element update result must follow its store and select the specified old/new value.", out failure);
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
        var write = operation.Kind == OwnershipOperationKind.WriteElement;
        if ((!write && !body.HasComparisonLoan(id, !address && plan.Exclusive >= 0 ? plan.Exclusive : plan.Loan)) ||
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
        if (write)
        {
            var input = IsScalar(body.Places[operation.Input].Type) ? Input(body, id, 0) : -1;
            if (plan.Update >= 0)
            {
                var update = body.ElementUpdates[plan.Update];
                if (!body.HasComparisonLoan(update.Right, plan.Exclusive) || !body.HasComparisonLoan(input, plan.Exclusive) ||
                    (body.IsReachable(id) && (!this.Dominates(plan.Output, update.Right) || !this.Dominates(update.Right, input) || !this.Dominates(input, id))))
                {
                    return Fail("Element update requires ordered old/RHS/computed values under its access Loan.", out failure);
                }
            }
            else if (body.IsReachable(id) && ((body.GetInputState(loan.Read, operation.Input) & PlaceState.MustInit) == 0 ||
                (input >= 0 && !this.Dominates(input, loan.Read))))
            {
                return Fail("Element replacement input is unavailable or was not secured before destination access.", out failure);
            }

            if (body.IsReachable(id) && (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0)
            {
                return Fail("Element store input is no longer initialized.", out failure);
            }

            if (representation.Layout.Size != 0)
            {
                if (layout.Children[field] is { } aggregate)
                {
                    var start = function.Operands.Count;
                    function.Operands.Add(new(EmissionOperandKind.SlotAddress, operation.Input));
                    function.Operands.Add(new(EmissionOperandKind.ElementAddress, plan.Operation));
                    function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, OperandStart: start, OperandCount: 2, Aggregate: aggregate));
                }
                else
                {
                    function.AddScalar(EmissionOpcode.StoreElement, id, [this.PhysicalOperand(body, input)], representation.ComputationType, place: plan.Operation, representation: representation);
                }
            }

            return true;
        }

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
