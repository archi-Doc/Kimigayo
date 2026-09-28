// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] elementNextCalls = [];

    private int[] elementOperations = [];
    private int[] elementOutputs = [];
    private bool hasElements;

    private static EmissionOperand StringPlaceOperand(OwnershipBody body, int place, int loan)
        => loan >= 0 && body.ComparisonLoans[loan].Projection is >= 0 and var projection
            ? new(EmissionOperandKind.ElementAddress, body.Projections[projection].Operation)
            : new(EmissionOperandKind.SlotAddress, place);

    private static bool ConsecutiveElementEdge(OwnershipBody body, int from, int to)
    {
        var edge = body.EdgeHeads[from];
        return edge >= 0 && body.Edges[edge] is { Kind: OwnershipEdgeKind.Normal, Next: -1 } next && next.To == to &&
            body.IncomingEdges[to] == edge && body.IncomingCounts[to] == 1;
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
        return ReferenceEquals(SignatureType(this, operation.Source.BoundType), place.Type) &&
            (operation.Source is IdentifierNameKoto { BoundSymbol.Kind: not BindingSymbolKind.PatternCandidate } identifier
                ? identifier.BoundSymbol is { } symbol && ((body.SymbolPlaces.TryGetValue(symbol, out var root) && root == place.Id) || this.IsPreparedArgument(body, id, symbol, place.Id))
                : ReferenceEquals(ElementAccess.ValueSource(operation.Source), place.Source)) &&
            this.IsElementOwnerStorage(place) &&
            (!body.IsReachable(id) || (body.GetStorageState(id, place.Id) & PlaceState.MustInit) != 0);
    }

    private bool IsPreparedArgument(OwnershipBody body, int read, BindingSymbol symbol, int place)
    {
        if (symbol.Kind != BindingSymbolKind.Parameter || (uint)read >= (uint)this.elementNextCalls.Length)
        {
            return false;
        }

        var next = this.elementNextCalls[read];
        if (next < 0 ||
            body.Operations[next].Source is not InvocationKoto { BoundCall: { } plan } call ||
            plan.Target.Declaration is not FunctionKoto target || !ReferenceEquals(symbol.Scope.Owner, target))
        {
            return false;
        }

        var omitted = false;
        foreach (var argument in plan.DefaultArguments)
        {
            if (symbol.Slot >= argument.Parameter.Slot)
            {
                continue;
            }

            for (var source = body.Operations[read].Source; source is not null && source != target; source = source.Parent)
            {
                if (ReferenceEquals(source, argument.Expression))
                {
                    omitted = true;
                    break;
                }
            }
        }

        if (!omitted)
        {
            return false;
        }

        var position = plan.Receiver is not null && plan.ReceiverOperation.ParameterIndex == symbol.Slot ? 0 : -1;
        for (var i = 0; i < plan.ArgumentToParameter.Length; i++)
        {
            if (plan.ArgumentToParameter[i] == symbol.Slot)
            {
                position = i + (plan.Receiver is null ? 0 : 1);
            }
        }

        if (position < 0)
        {
            return false;
        }

        var first = next;
        while (first > read && body.Operations[first - 1].Kind == OwnershipOperationKind.CallEntry)
        {
            first--;
        }

        var entry = first + position;
        return entry > read && entry < next && ReferenceEquals(body.Operations[entry].Source, call) && body.Operations[entry].Place == place;
    }

    private bool IsElementOwnerStorage(OwnershipPlace place) => place.Kind switch
    {
        OwnershipPlaceKind.Local => true,
        OwnershipPlaceKind.Parameter => this.slotFunctionPlaces[place.Id] == 1,
        OwnershipPlaceKind.Result => this.slotResultPlaces[place.Id] != 0,
        OwnershipPlaceKind.Temporary => this.constructionOwners[place.Id] >= 0 || this.slotFunctionInitializations[place.Id] >= 0 || this.aggregateReadInitializations[place.Id] >= 0,
        _ => false,
    };

    private bool ValidateElementOwner(OwnershipBody body, int id)
    {
        var place = body.Operations[id].Place;
        var initialized = this.constructionOwners[place] >= 0 ? this.aggregateCompletions[place]
            : this.aggregateReadInitializations[place] >= 0 ? this.aggregateReadInitializations[place] : this.slotFunctionInitializations[place];
        // Locals use their dataflow state. Selection results are checked against
        // their current declaration/Join lifetime in ValidateSlotResults.
        return body.Places[place].Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Result ||
            (initialized >= 0 && (!body.IsReachable(id) || this.Dominates(initialized, id)));
    }

    private bool PrepareElements(OwnershipBody body, out string? failure)
    {
        failure = null;
        this.hasElements = body.Projections.Count != 0;
        var preparedCopies = false;
        if (!this.hasElements)
        {
            for (var i = 0; i < body.Operations.Count; i++)
            {
                var operation = body.Operations[i];
                if (operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Read or OwnershipOperationKind.Borrow && operation.Source.BoundSymbol?.Kind == BindingSymbolKind.Parameter &&
                    (uint)operation.Place < (uint)body.Places.Count &&
                    body.Places[operation.Place] is { Kind: OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result } prepared &&
                    (prepared.Type.Kind == BoundTypeKind.Tuple || ReferenceTypes.IsStorage(prepared.Type)))
                {
                    preparedCopies = true;
                    break;
                }
            }
        }

        // Defaults in this subset contain no calls. Cache the following call once; receiver and prepared-argument
        // borrow checks then validate against its explicit acquired argument slots, in every body.
        Grow(ref this.elementNextCalls, body.Operations.Count);
        var nextCall = -1;
        for (var i = body.Operations.Count - 1; i >= 0; i--)
        {
            this.elementNextCalls[i] = nextCall;
            if (body.Operations[i].Kind == OwnershipOperationKind.Call)
            {
                nextCall = i;
            }
        }

        if (!this.hasElements && !preparedCopies)
        {
            return body.ElementUpdates.Count == 0 || Fail("Element updates require projection plans.", out failure);
        }

        for (var i = body.Operations.Count - 1; i >= 0; i--)
        {
            // Explicit aggregate arguments can be acquired from locals/parameters,
            // not just literals or call results. Their Consume is independently
            // validated by LowerAggregate; retain its identity for dominance checks.
            // A struct acquired by @move or @copy is a receiver of member selection.
            var operation = body.Operations[i];
            if (operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition is AcquisitionKind.Copy or AcquisitionKind.Move &&
                (uint)operation.Input < (uint)body.Places.Count &&
                body.Places[operation.Input] is { Kind: OwnershipPlaceKind.Temporary } acquired &&
                (acquired.Type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray || StructStorage.IsStruct(acquired.Type)))
            {
                if (this.slotFunctionInitializations[acquired.Id] >= 0 || this.constructionOwners[acquired.Id] >= 0 || this.slotFunctionPlaces[acquired.Id] != 0)
                {
                    return Fail("Acquired aggregate storage must have one initialization.", out failure);
                }

                this.slotFunctionInitializations[acquired.Id] = i;
            }
        }

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
                    body.ComparisonLoans[plan.Exclusive].Mode != LoanRequirement.Uniq || body.ComparisonLoans[plan.Exclusive].Projection != i)) ||
                plan.Update < -1 || (plan.Update >= 0 && (plan.Update >= body.ElementUpdates.Count || plan.Output < 0 || plan.Write < 0 || plan.Exclusive < 0)) ||
                plan.Borrow < -1 || (plan.Borrow >= 0 && ((uint)plan.Borrow >= (uint)body.Operations.Count ||
                    body.Operations[plan.Borrow].Projection != i || body.LoanStates[plan.Borrow] < 0 ||
                    (body.Values[plan.Borrow].Kind == OwnershipValueKind.Address ? body.LoanStates[plan.Borrow] != plan.Loan : body.ComparisonLoans[body.LoanStates[plan.Borrow]].Projection != i) ||
                    plan.Output != -1 || plan.Write != -1 || plan.Exclusive != -1 || plan.Update != -1 ||
                    !ConsecutiveElementEdge(body, plan.Operation, plan.Borrow))) ||
                (plan.Output >= 0 && plan.Write >= 0 && plan.Update < 0) ||
                this.elementOperations[plan.Operation] >= 0 ||
                body.Operations[plan.Operation] is not { Kind: OwnershipOperationKind.ProjectElement, Source: BinaryKoto source, Input: -1 } operation ||
                operation.Place != plan.Root || body.Values[plan.Operation].Kind != OwnershipValueKind.None ||
                !(ElementAccess.TryType(source, out var declared, out var position) && SignatureType(this, declared) is { } element) || position != plan.Element ||
                source.AttributeChain is not null || !ReferenceEquals(SignatureType(this, source.BoundType), element) || this.aggregateLayouts.Get(SignatureType(this, source.Left.BoundType)!) is null)
            {
                return Fail("Element address has no matching source and aggregate shape.", out failure);
            }

            var loan = body.ComparisonLoans[plan.Loan];
            if (!loan.Access || loan.Place != plan.Root || !this.IsElementReceiverRead(body, loan.Read) ||
                (plan.Parent >= 0
                    ? body.Projections[plan.Parent].Root != plan.Root || body.Projections[plan.Parent].Loan != plan.Loan ||
                        body.Projections[plan.Parent].Output != -1 || body.Projections[plan.Parent].Write != -1 ||
                        body.Projections[plan.Parent].Borrow != -1 ||
                        !ReferenceEquals(body.Operations[body.Projections[plan.Parent].Operation].Source, KotoHelper.UnwrapParentheses(source.Left))
                    : !ReferenceEquals(body.Operations[loan.Read].Source, KotoHelper.UnwrapParentheses(source.Left))))
            {
                return Fail("Element address is not protected by its receiver Loan.", out failure);
            }

            if (source is IndexKoto)
            {
                var keyType = source is IndexKoto { DictionaryKeyReference: { } keyReference } ? SignatureType(this, keyReference) : BoundType.ISize;
                if ((uint)plan.Index >= (uint)body.Values.Count || !ReferenceEquals(ValueType(body, plan.Index), keyType) ||
                    !ReferenceEquals(body.Operations[plan.Index].Source, ElementAccess.ValueSource(ElementAccess.KeySyntax((IndexKoto)source))) ||
                    (keyType == BoundType.ISize ? body.Operations[plan.Index].Kind is not (OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.Branch) : body.Operations[plan.Index].Kind != OwnershipOperationKind.Borrow))
                {
                    return Fail("Element selection requires its evaluated isize index or Dictionary key borrow.", out failure);
                }

                this.continuations[plan.Operation] = body.Operations.Count + plan.Operation;
            }
            else if (source is not MemberAccessKoto || plan.Index != -1)
            {
                return Fail("Unexpected element selector.", out failure);
            }

            if (plan.Output >= 0)
            {
                var copy = source.CodeContext.Compilation.Binding.ProveCopy(element!, source) == ConstraintProof.Proven;
                if ((uint)plan.Output >= (uint)body.Operations.Count || this.elementOutputs[plan.Output] >= 0 ||
                    body.Operations[plan.Output] is not { Kind: OwnershipOperationKind.Produce } output ||
                    (uint)output.Place >= (uint)body.Places.Count || !ReferenceEquals(output.Source, source) ||
                    body.Values[plan.Output].Kind != OwnershipValueKind.Element ||
                    body.Places[output.Place].Kind != OwnershipPlaceKind.Temporary ||
                    (body.Places[output.Place].Acquisition == AcquisitionKind.Copy ? output.Acquisition != AcquisitionKind.None || !copy :
                        body.Places[output.Place].Acquisition != AcquisitionKind.Move || output.Acquisition != AcquisitionKind.Move || plan.Path != i ||
                        !ElementAccess.SupportsMoveRoot(body.Places[plan.Root]) || copy) ||
                    !ReferenceEquals(body.Places[output.Place].Type, element))
                {
                    return Fail("Element acquisition requires a fresh Copy result or an eligible static owned Move of the selected Type.", out failure);
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

    private bool ValidateElementBorrow(OwnershipBody body, int borrow, int at)
    {
        var operation = body.Operations[borrow];
        if ((uint)operation.Projection >= (uint)body.Projections.Count)
        {
            return false;
        }

        var plan = body.Projections[operation.Projection];
        return plan.Borrow == borrow && this.elementOperations[plan.Operation] == operation.Projection &&
            (borrow == at || body.Values[borrow].Kind == OwnershipValueKind.Address ? body.HasComparisonLoan(at, plan.Loan) : body.HasComparisonLoan(at, body.LoanStates[borrow])) &&
            (!body.IsReachable(at) || (this.Dominates(plan.Operation, borrow) && (borrow == at || this.Dominates(borrow, at)) &&
                (body.GetElementState(at, operation.Projection) & PlaceState.MustInit) != 0));
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
            body.Places[write.Input] is not { Kind: OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result, Acquisition: AcquisitionKind.Copy or AcquisitionKind.Move } input ||
            !ReferenceEquals(input.Type, element) ||
            !(ScalarTypes.Supports(element) || ReferenceEquals(element, BoundType.Unit) || ReferenceEquals(element, BoundType.String) || this.aggregateLayouts.Get(element) is not null) ||
            (IsScalar(element) ? body.Values[plan.Write] is not { Kind: OwnershipValueKind.Alias, Count: 1 }
                : body.Values[plan.Write].Kind != OwnershipValueKind.None))
        {
            return Fail("Element replacement requires writable local storage and a secured supported input of its exact Type.", out failure);
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

            last = value;
        }
        else if (source is not BinaryKoto { Akind: KotoKind.Equals } assignment ||
            !ReferenceEquals(KotoHelper.UnwrapParentheses(assignment.Left), target) || !ReferenceEquals(SignatureType(this, source.BoundType), BoundType.Unit) ||
            !ReferenceEquals(input.Source, ElementAccess.ValueSource(assignment.Right)) ||
            (IsScalar(element) && value >= body.ComparisonLoans[plan.Loan].Read))
        {
            return Fail("Simple element assignment must secure its RHS before locating the destination.", out failure);
        }

        // One write operation destroys the complete old element and installs the secured
        // input. Retain exclusive authority throughout, releasing only after placement.
        var end = plan.Write + 1;
        if (plan.Exclusive < 0 || end >= body.Operations.Count || plan.Write != last + 1 ||
            body.Operations[end] is not { Kind: OwnershipOperationKind.EndComparisonLoans, Place: -1, Input: -1 } ||
            !ReferenceEquals(body.Operations[end].Source, source) || body.LoanInputs[end] != plan.Exclusive ||
            body.LoanStates[end] != body.ComparisonLoans[plan.Loan].Parent || body.LoanInputs[plan.Write] != plan.Exclusive ||
            !ConsecutiveElementEdge(body, last, plan.Write) || !ConsecutiveElementEdge(body, plan.Write, end))
        {
            return Fail("Element write must retain its exclusive Loan through placement and release it afterward.", out failure);
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
            !ReferenceEquals(SignatureType(this, source.BoundType), unary ? type : BoundType.Unit) ||
            !ReferenceEquals(KotoHelper.UnwrapParentheses(source is UnaryKoto increment ? increment.Operand : ((BinaryKoto)source).Left), target) ||
            value != update.Computation || value <= plan.Output ||
            body.Operations[value] is not { Kind: OwnershipOperationKind.Produce, Input: -1 } computation ||
            !ReferenceEquals(computation.Source, source) || !ReferenceEquals(body.Places[computation.Place].Source, source) ||
            body.Values[value] is not { Kind: OwnershipValueKind.Binary, Count: 2 } calculation || calculation.Operator != op ||
            Input(body, value, 0) != plan.Output || Input(body, value, 1) != update.Right ||
            update.Right >= value || (unary ? update.Right <= plan.Output : update.Right >= plan.Operation) ||
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
            !ReferenceEquals(result.Source, source) || !ReferenceEquals(ValueType(body, update.Result), SignatureType(this, source.BoundType)) ||
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
            (body.IsReachable(id) && ((body.GetElementState(id, index, address || write) & PlaceState.MustInit) == 0 ||
                !this.Dominates(loan.Read, id) ||
                (address ? (plan.Parent >= 0 && !this.Dominates(body.Projections[plan.Parent].Operation, id)) ||
                    (plan.Index >= 0 && !this.Dominates(plan.Index, id)) : !this.Dominates(plan.Operation, id)))))
        {
            return Fail("Element receiver, index or address is unavailable at acquisition.", out failure);
        }

        var source = (BinaryKoto)body.Operations[plan.Operation].Source;
        var receiverType = SignatureType(this, source.Left.BoundType)!;
        var dynamicArray = receiverType.Kind == BoundTypeKind.Array;
        var dictionary = receiverType.Kind == BoundTypeKind.Dictionary;
        var dynamicElement = dynamicArray || dictionary;
        var layout = this.aggregateLayouts.Get(receiverType)!;
        var field = layout.IsArray || dynamicElement ? 0 : plan.Element;
        // The Array layout describes its handle; the indexed storage has T's own layout.
        var stored = dynamicElement ? receiverType.Components[dictionary ? 1 : 0] : null;
        var representation = dynamicElement ? FunctionAbi.GetValue(stored!, this.aggregateLayouts) : layout.Fields[field];
        var elementLayout = dynamicElement ? this.aggregateLayouts.Get(stored!) : layout.Children[field];
        if (representation is null)
        {
            return Fail("Array element has no supported storage representation.", out failure);
        }

        if (write)
        {
            var input = IsScalar(body.Places[operation.Input].Type) ? Input(body, id, 0) : -1;
            if (plan.Update >= 0)
            {
                var update = body.ElementUpdates[plan.Update];
                var unary = body.Operations[plan.Write].Source is UnaryKoto;
                if ((unary && !body.HasComparisonLoan(update.Right, plan.Exclusive)) || !body.HasComparisonLoan(input, plan.Exclusive) ||
                    (body.IsReachable(id) && (!(unary ? this.Dominates(plan.Output, update.Right) : this.Dominates(update.Right, plan.Operation)) ||
                        !this.Dominates(update.Right, input) || !this.Dominates(input, id))))
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

            var child = elementLayout;
            var isString = ReferenceEquals(representation, WindowsLowering.String);
            var destination = new EmissionOperand(EmissionOperandKind.ElementAddress, plan.Operation);
            if (isString || child is { NeedsDestruction: true })
            {
                if (this.DestructionPath(body, operation) >= 0)
                {
                    if (!this.LowerPartDestruction(body, function, constants, directory, id, out failure))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!this.TryGetLocation(operation.Source, directory, constants, out var location))
                    {
                        return Fail("Element destruction requires a source location.", out failure);
                    }

                    AddOwnedDestruction(function, id, destination, location, child);
                }
            }

            if (representation.Layout.Size != 0)
            {
                if (child is { } aggregate)
                {
                    var start = function.Operands.Count;
                    function.Operands.Add(new(EmissionOperandKind.SlotAddress, operation.Input));
                    function.Operands.Add(destination);
                    function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, OperandStart: start, OperandCount: 2, Aggregate: aggregate));
                }
                else if (isString)
                {
                    function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, operation.Input), destination]);
                }
                else
                {
                    function.AddScalar(EmissionOpcode.StoreElement, id, [this.PhysicalOperand(body, input)], representation.ComputationType, place: plan.Operation, representation: representation);
                }
            }

            this.AddStringFlags(function, operation, id);
            return true;
        }

        if (address)
        {
            var location = -1;
            if ((layout.IsArray || dynamicElement) && !this.TryGetLocation(source, directory, constants, out location))
            {
                return Fail("Element bounds check requires a source location.", out failure);
            }

            // Zero-byte receivers have no slot. Any nonzero child can only be reached after
            // an impossible bounds success (an empty array), so use an explicit null base.
            var receiver = layout.Value.Layout.Size == 0 ? new EmissionOperand(EmissionOperandKind.NullAddress, 0)
                : plan.Parent < 0 ? new EmissionOperand(EmissionOperandKind.SlotAddress, plan.Root)
                : new EmissionOperand(EmissionOperandKind.ElementAddress, body.Projections[plan.Parent].Operation);
            if (dictionary)
            {
                if (source.CodeContext.Compilation.Binding.DictionaryComparison(receiverType) is not { } comparison ||
                    this.ComparisonHelpers?.GetValueOrDefault(comparison) is not { } equality ||
                    !this.TryGetArrayElement(receiverType.Components[0], out var key, allowEmpty: true) ||
                    !this.TryGetArrayElement(receiverType.Components[1], out var value, allowEmpty: true))
                {
                    return Fail("Dictionary indexing requires a verified equality and concrete entry layout.", out failure);
                }

                var helper = this.GetDictionaryHelper(DictionaryHelperKind.Find, key, value, equality: equality);
                function.AddCall(id, helper.Abi, [receiver, this.PhysicalOperand(body, plan.Index)]);
                function.AddScalar(EmissionOpcode.Sequence, id, [receiver, new(EmissionOperandKind.Value, id), new(EmissionOperandKind.Integer, helper.Stride), new(EmissionOperandKind.Integer, helper.ValueOffset)], place: body.Operations.Count + id, location: location, op: "DictionaryLocate", check: ArithmeticCheckKind.MissingKey, representation: representation);
                return true;
            }

            if (dynamicArray)
            {
                // A dynamic element retains the root-wide access Loan; resolving it never
                // creates a movable path. Replacement uses the ordinary destroy/store below.
                function.AddScalar(EmissionOpcode.Sequence, id, [receiver, this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, -1)], place: body.Operations.Count + id, location: location, op: "SliceAddress", check: ArithmeticCheckKind.Bounds, representation: representation);
                return true;
            }

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
                return Fail("Element result is already initialized.", out failure);
            }

            if (representation.Layout.Size == 0)
            {
                return true;
            }

            if (elementLayout is { } aggregate)
            {
                var start = function.Operands.Count;
                function.Operands.Add(new(EmissionOperandKind.ElementAddress, plan.Operation));
                function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, Place: operation.Place, OperandStart: start, OperandCount: 1, Aggregate: aggregate));
            }
            else if (ReferenceEquals(representation, WindowsLowering.String))
            {
                function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.ElementAddress, plan.Operation)], place: operation.Place);
                this.AddStringFlags(function, operation, id);
            }
            else
            {
                function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: plan.Operation, representation: representation);
            }
        }

        return true;
    }
}
