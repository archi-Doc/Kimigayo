// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private const int BorrowedTemporaryFlag = 4;
    private int[] liveFlags = [];
    private int[] flagValidation = [];
    private int[] borrowedTemporaries = [];

    internal static bool ValidateStringFlags(OwnershipBody body, EmissionFunction function, Span<int> scratch, ReadOnlySpan<byte> execution = default)
    {
        if (scratch.Length < body.Places.Count + body.Operations.Count)
        {
            return false;
        }

        scratch.Clear();
        var flags = scratch[..body.Places.Count];
        var seen = scratch[body.Places.Count..];
        MarkBorrowedTemporaries(body, flags);
        foreach (var place in function.LiveFlags)
        {
            if ((uint)place >= (uint)flags.Length || (flags[place] & 3) != 0 || (body.Places[place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter) && (flags[place] & BorrowedTemporaryFlag) == 0) ||
                !HasOwnedStorage(body.Places[place].Type))
            {
                return false;
            }

            flags[place] |= 1;
        }

        for (var i = 0; i < body.CleanupSteps.Count; i++)
        {
            var step = body.CleanupSteps[i];
            if ((execution.IsEmpty || (execution[step.Operation] & NormalMark) != 0) && step.Action == CleanupAction.Conditional && body.MoveRoot(step.Place) < 0 && HasOwnedStorage(body.Places[step.Place].Type) && (flags[step.Place] & 3) == 0)
            {
                return false;
            }
        }

        foreach (var instruction in function.Instructions)
        {
            if (instruction.Opcode == EmissionOpcode.InitializeLiveFlag)
            {
                if (instruction.Operation != 0 || instruction.Constant != 0 || (uint)instruction.Place >= (uint)flags.Length || flags[instruction.Place] != (1 | BorrowedTemporaryFlag))
                {
                    return false;
                }

                flags[instruction.Place] |= 2;
                continue;
            }

            if (instruction.Opcode != EmissionOpcode.StoreLiveFlag)
            {
                continue;
            }

            if ((uint)instruction.Operation >= (uint)body.Operations.Count || (uint)instruction.Place >= (uint)flags.Length || (flags[instruction.Place] & 3) == 0)
            {
                return false;
            }

            StringFlagTransition(body.Operations[instruction.Operation], out var clear, out var initialize);
            var bit = instruction.Constant == 0 && instruction.Place == clear ? 1 : instruction.Constant == 1 && instruction.Place == initialize ? 2 : 0;
            if (bit == 0 || (seen[instruction.Operation] & bit) != 0)
            {
                return false;
            }

            seen[instruction.Operation] |= bit;
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            if (!execution.IsEmpty && (execution[id] & NormalMark) == 0)
            {
                if (seen[id] != 0)
                {
                    return false;
                }

                continue;
            }

            var operation = body.Operations[id];
            if (StartsStringLifetime(body, operation) && (flags[operation.Place] & 3) != 0)
            {
                flags[operation.Place] |= 2;
            }

            StringFlagTransition(operation, out var clear, out var initialize);
            var expected = (clear >= 0 && (flags[clear] & 3) != 0 ? 1 : 0) | (initialize >= 0 && (flags[initialize] & 3) != 0 ? 2 : 0);
            if (body.IsReachable(id) && seen[id] != expected)
            {
                return false;
            }
        }

        foreach (var place in function.LiveFlags)
        {
            if ((flags[place] & 3) != 3)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsStringLifetime(OwnershipBody body, OwnershipOperation operation) =>
        (uint)operation.Place < (uint)body.Places.Count && body.Places[operation.Place].Kind switch
        {
            OwnershipPlaceKind.Local => operation.Kind == OwnershipOperationKind.Declare,
            OwnershipPlaceKind.Parameter => operation.Kind == OwnershipOperationKind.Produce && ReferenceEquals(operation.Source, body.Places[operation.Place].Source),
            _ => false,
        };

    private static bool HasOwnedStorage(BoundType type)
    {
        if (type.Kind == BoundTypeKind.Function)
        {
            return true;
        }

        if (EnumStorage.IsEnum(type) && type.StoredCases is { } cases)
        {
            foreach (var payload in cases)
            {
                if (HasOwnedStorage(payload))
                {
                    return true;
                }
            }
        }

        if (StructStorage.IsStruct(type))
        {
            if (StructStorage.Destructor(type) is not null)
            {
                return true;
            }

            for (var i = 0; i < StructStorage.Count(type); i++)
            {
                if (HasOwnedStorage(StructStorage.FieldType(type, i)!))
                {
                    return true;
                }
            }
        }

        // SPEC 4.7.6: an Array handle owns its buffer even when its elements are Copy.
        if (ReferenceEquals(type, BoundType.String) || type.Kind == BoundTypeKind.Array)
        {
            return true;
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure && (type.Kind != BoundTypeKind.FixedArray || type.Length != 0))
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (HasOwnedStorage(type.Components[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void MarkBorrowedTemporaries(OwnershipBody body, Span<int> marks)
    {
        foreach (var comparison in body.StringComparisons)
        {
            MarkBorrowedTemporary(body, comparison.Left, marks);
            MarkBorrowedTemporary(body, comparison.Right, marks);
        }

        foreach (var loan in body.ComparisonLoans)
        {
            if (loan.Call is not null)
            {
                MarkBorrowedTemporary(body, loan.Place, marks);
            }
        }

        // A temporary borrowed as a whole or located as a receiver (for example an Array literal passed to a ref/
        // parameter, or make()[0]) lives until its full-expression cleanup, which may be conditional under a
        // short-circuit operand.
        for (var id = 0; id < body.Operations.Count; id++)
        {
            if (body.Operations[id].Kind is OwnershipOperationKind.Borrow or OwnershipOperationKind.LocateReceiver)
            {
                MarkBorrowedTemporary(body, body.Operations[id].Place, marks);
            }
        }
    }

    private static void MarkBorrowedTemporary(OwnershipBody body, int place, Span<int> marks)
    {
        if ((uint)place < (uint)body.Places.Count && body.Places[place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
        {
            marks[place] |= BorrowedTemporaryFlag;
        }
    }

    // The two sides mirror whole-Place responsibility changes in OwnershipBody.Transfer.
    // Clearing the source is independent from initializing the acquired destination.
    private static void StringFlagTransition(OwnershipOperation operation, out int clear, out int initialize)
    {
        clear = initialize = -1;
        switch (operation.Kind)
        {
            case OwnershipOperationKind.Declare:
            case OwnershipOperationKind.Cleanup:
            case OwnershipOperationKind.CallEntry:
            case OwnershipOperationKind.Deliver:
            case OwnershipOperationKind.StorePointer:
                clear = operation.Place;
                break;
            case OwnershipOperationKind.Produce:
            case OwnershipOperationKind.CompleteConstruction:
                initialize = operation.Place;
                break;
            case OwnershipOperationKind.Consume:
            case OwnershipOperationKind.AcquirePattern:
                clear = operation.Acquisition == AcquisitionKind.Move ? operation.Place : -1;
                initialize = operation.Input;
                break;
            case OwnershipOperationKind.Write:
            case OwnershipOperationKind.PayloadPlacement:
            case OwnershipOperationKind.InitializeSubject:
                clear = operation.Input;
                initialize = operation.Place;
                break;
            case OwnershipOperationKind.WriteElement:
                clear = operation.Input;
                break;
            case OwnershipOperationKind.UpdateBorrowed:
                if (operation.Source is InvocationKoto { BoundCall.Target.CompilerFunction: not CompilerFunctionKind.Swap })
                {
                    clear = operation.Input;
                }

                if (operation.Source is InvocationKoto { BoundCall.Target.CompilerFunction: CompilerFunctionKind.Exchange })
                {
                    initialize = operation.Place;
                }

                break;
        }
    }

    private static void AddOwnedDestruction(EmissionFunction function, int id, EmissionOperand address, int location, AggregateLayout? aggregate)
    {
        if (aggregate is not null)
        {
            var start = function.Operands.Count;
            function.Operands.Add(address);
            function.Instructions.Add(new(EmissionOpcode.DestroyAggregate, id, Constant: location, OperandStart: start, OperandCount: 1, Aggregate: aggregate));
        }
        else
        {
            function.AddCall(id, WindowsLowering.DestroyString, [address, new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        }
    }

    private bool IsStringStorage(OwnershipPlace place) => this.arrayIterationPlaces[place.Id] != 0 || this.payloadOwners[place.Id] >= 0 || this.decompositionOwners[place.Id] >= 0 || this.slotFunctionPlaces[place.Id] != 0 || (this.hasMatches && this.matchPlaces[place.Id] != 0) || place.Kind switch
    {
        OwnershipPlaceKind.Local => place.Source is FieldKoto or PropertyKoto or FunctionKoto { BoundClosure.EnvironmentType: not null },
        OwnershipPlaceKind.Temporary => place.Source is StringLiteralKoto or InterpolatedStringKoto { Formatting: not null } or IdentifierNameKoto or DereferenceKoto or FunctionKoto { BoundClosure.EnvironmentType: not null } or
            InvocationKoto { BoundCall.Target.CompilerFunction: CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap } || (place.Source is BinaryKoto element && ElementAccess.IsSyntax(element)),
        OwnershipPlaceKind.Result => this.slotResultPlaces[place.Id] != 0,
        _ => false,
    };

    private bool IsStringValue(OwnershipPlace place) => place.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && this.IsStringStorage(place);

    private bool PrepareStrings(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        Grow(ref this.borrowedTemporaries, body.Places.Count);
        this.borrowedTemporaries.AsSpan(0, body.Places.Count).Clear();
        MarkBorrowedTemporaries(body, this.borrowedTemporaries);
        var count = body.Operations.Count;
        Grow(ref this.liveFlags, body.Places.Count);
        this.liveFlags.AsSpan(0, body.Places.Count).Clear();
        Grow(ref this.continuations, count);
        for (var id = 0; id < count; id++)
        {
            // An operation can split at most once. Check results and conditional
            // destruction share the continuation namespace, not their value names.
            this.continuations[id] = this.checks[id] == ArithmeticCheckKind.None ? -1 : count + id;
        }

        for (var i = 0; i < body.CleanupSteps.Count; i++)
        {
            var step = body.CleanupSteps[i];
            if ((uint)step.Operation >= (uint)count || (uint)step.Place >= (uint)body.Places.Count)
            {
                return Fail("Invalid cleanup destination.", out failure);
            }

            if (body.MoveRoot(step.Place) >= 0 || step.Action != CleanupAction.Conditional || !HasOwnedStorage(body.Places[step.Place].Type))
            {
                continue;
            }

            if ((this.aggregatePlaces[step.Place] is null && !this.IsStringStorage(body.Places[step.Place])) || (body.Places[step.Place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter) && this.borrowedTemporaries[step.Place] == 0) ||
                body.Operations[step.Operation].Kind is not (OwnershipOperationKind.Cleanup or OwnershipOperationKind.Write) ||
                this.continuations[step.Operation] >= 0)
            {
                return Fail("Conditional string destruction requires a verified lifetime and one split.", out failure);
            }

            this.liveFlags[step.Place] = 1;
            this.continuations[step.Operation] = count + step.Operation;
        }

        for (var i = 0; i < count; i++)
        {
            var operation = body.Operations[i];
            if (StartsStringLifetime(body, operation) && this.liveFlags[operation.Place] != 0)
            {
                this.liveFlags[operation.Place] = 2;
            }
        }

        for (var p = 0; p < body.Places.Count; p++)
        {
            if (this.liveFlags[p] == 1 && this.borrowedTemporaries[p] != 0)
            {
                this.liveFlags[p] = 2; // Entry zeroing covers paths that skip this temporary entirely.
            }

            if (this.liveFlags[p] == 1)
            {
                return Fail("Conditional string lifetime has no declaration or parameter initialization.", out failure);
            }

            if (this.liveFlags[p] != 0)
            {
                this.liveFlags[p] = 1;
                function.LiveFlags.Add(p);
            }
        }

        return true;
    }

    private void AddStringFlags(EmissionFunction function, OwnershipOperation operation, int id)
    {
        StringFlagTransition(operation, out var clear, out var initialize);
        if (clear >= 0 && this.liveFlags[clear] != 0)
        {
            function.Add(EmissionOpcode.StoreLiveFlag, id, clear, 0);
        }

        if (initialize >= 0 && this.liveFlags[initialize] != 0)
        {
            function.Add(EmissionOpcode.StoreLiveFlag, id, initialize, 1);
        }
    }

    private bool LowerString(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, ReadOnlySpan<byte> marks, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var place = body.Places[operation.Place];
        if (!this.IsStringStorage(place) || operation.Source.AttributeChain is not null)
        {
            return Fail("Unsupported string storage or operation attributes.", out failure);
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.Declare:
                if (place.Kind != OwnershipPlaceKind.Local && this.payloadOwners[place.Id] < 0 && this.decompositionOwners[place.Id] < 0 &&
                    this.slotResultDeclarations[id] == 0 && (!this.hasMatches || this.matchPlaces[place.Id] != 1))
                {
                    return Fail("String Declare requires a local.", out failure);
                }

                break;
            case OwnershipOperationKind.Produce:
                if (this.slotFunctionProduces[id] != 0)
                {
                    break; // Parameter receipt or a call's normal result: the storage is already populated.
                }

                if (place.Kind != OwnershipPlaceKind.Temporary || operation.Source is not StringLiteralKoto literal)
                {
                    return Fail("Only string literals construct string temporaries.", out failure);
                }

                var constant = literal.Literal.Length == 0 ? -1 : constants.Intern(literal.Literal, LlvmConstantKind.Text);
                function.Add(EmissionOpcode.StoreStaticString, id, operation.Place, constant);
                break;
            case OwnershipOperationKind.Consume:
            case OwnershipOperationKind.Write:
                if ((uint)operation.Input >= (uint)body.Places.Count || operation.Input == operation.Place ||
                    !ReferenceEquals(body.Places[operation.Input].Type, BoundType.String) ||
                    !this.IsStringValue(body.Places[operation.Input]) ||
                    (operation.Kind == OwnershipOperationKind.Consume && (place.Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter) || body.Places[operation.Input].Kind != OwnershipPlaceKind.Temporary || operation.Acquisition != AcquisitionKind.Move)) ||
                    (operation.Kind == OwnershipOperationKind.Write && place.Kind != OwnershipPlaceKind.Local && this.slotResultWrites[id] == 0 && this.slotFunctionPlaces[place.Id] != 2))
                {
                    return Fail("String transfer requires distinct verified source and destination storage.", out failure);
                }

                var source = operation.Kind == OwnershipOperationKind.Consume ? operation.Place : operation.Input;
                var destination = operation.Kind == OwnershipOperationKind.Consume ? operation.Input : operation.Place;
                if (body.IsReachable(id) && (body.GetInputState(id, source) & PlaceState.MustInit) == 0)
                {
                    return Fail("String transfer source is not initialized.", out failure);
                }

                if (operation.Kind == OwnershipOperationKind.Write && this.slotFunctionPlaces[place.Id] == 2 && body.IsReachable(id) &&
                    (operation.Placement != PlacementKind.Initialization || (body.GetInputState(id, destination) & PlaceState.MayInit) != 0))
                {
                    return Fail("Return storage must be uninitialized before securing its result.", out failure);
                }

                if (operation.Kind == OwnershipOperationKind.Write && place.Kind == OwnershipPlaceKind.Local && !this.LowerStringDestruction(body, function, constants, directory, id, marks, out failure))
                {
                    return false;
                }

                function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, source)], place: destination);
                break;
            case OwnershipOperationKind.Cleanup:
                if (!this.LowerStringDestruction(body, function, constants, directory, id, marks, out failure))
                {
                    return false;
                }

                break;
            default:
                return Fail("Unsupported string lifetime operation.", out failure);
        }

        // No lifetime is published before successful destruction and placement.
        this.AddStringFlags(function, operation, id);
        return true;
    }

    private bool LowerStringDestruction(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, ReadOnlySpan<byte> marks, out string? failure, AggregateLayout? aggregate = null)
    {
        failure = null;
        var operation = body.Operations[id];
        if (!body.IsReachable(id))
        {
            return true; // Checking-only operations have no finalized replacement state.
        }

        var index = body.OperationSteps[id];
        if ((uint)index >= (uint)body.CleanupSteps.Count ||
            (operation.Kind == OwnershipOperationKind.Cleanup && (marks[id] & CleanupMark) == 0))
        {
            return Fail("String destruction has no cleanup step.", out failure);
        }

        var step = body.CleanupSteps[index];
        var state = body.GetStorageState(id, operation.Place);
        var expected = (state & PlaceState.MustInit) != 0 ? CleanupAction.Destroy : (state & PlaceState.MayInit) != 0 ? CleanupAction.Conditional : CleanupAction.Skip;
        var invalidPlacement = expected switch
        {
            CleanupAction.Destroy => operation.Placement != PlacementKind.Replacement,
            CleanupAction.Conditional => operation.Placement != PlacementKind.ConditionalReplacement,
            _ => operation.Placement is not (PlacementKind.Initialization or PlacementKind.Reinitialization or PlacementKind.EmptyPlacement),
        };
        if (step.Operation != id || step.Place != operation.Place || step.Action != expected ||
            (operation.Kind == OwnershipOperationKind.Write && invalidPlacement))
        {
            return Fail("String destruction disagrees with the verified placement state.", out failure);
        }

        if (this.IsStackFormattingBuffer(body.Places[operation.Place]))
        {
            return true; // Private stack storage has no release, including on an escaping transfer.
        }

        if (this.DestructionPath(body, operation) >= 0)
        {
            return this.LowerPartDestruction(body, function, constants, directory, id, out failure);
        }

        if (expected == CleanupAction.Skip)
        {
            return true;
        }

        // SPEC 4.7.6: replacing a whole Array destroys the old elements and releases its buffer first.
        var array = body.Places[operation.Place].Type.Kind == BoundTypeKind.Array;
        if (!array && aggregate is { NeedsDestruction: false })
        {
            return true;
        }

        if (!this.TryGetLocation(operation.Source, directory, constants, out var location))
        {
            return Fail("String destruction has no source location.", out failure);
        }

        if (array)
        {
            return this.LowerArrayRelease(body, function, id, location, expected == CleanupAction.Conditional, out failure);
        }

        if (expected != CleanupAction.Conditional)
        {
            AddOwnedDestruction(function, id, new(EmissionOperandKind.SlotAddress, operation.Place), location, aggregate);
        }
        else if (aggregate is not null)
        {
            function.Instructions.Add(new(EmissionOpcode.DestroyAggregate, id, operation.Place, location, Aggregate: aggregate, Continuation: this.continuations[id]));
        }
        else
        {
            function.AddScalar(EmissionOpcode.DestroyStringIfLive, id, [new(EmissionOperandKind.Block, this.continuations[id])], place: operation.Place, location: location);
        }

        return true;
    }
}
