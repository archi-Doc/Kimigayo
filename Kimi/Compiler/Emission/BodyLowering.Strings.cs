// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] liveFlags = [];
    private int[] flagValidation = [];

    internal static bool ValidateStringFlags(OwnershipBody body, EmissionFunction function, Span<int> scratch)
    {
        if (scratch.Length < body.Places.Count + body.Operations.Count)
        {
            return false;
        }

        scratch.Clear();
        var flags = scratch[..body.Places.Count];
        var seen = scratch[body.Places.Count..];
        foreach (var place in function.LiveFlags)
        {
            if ((uint)place >= (uint)flags.Length || flags[place] != 0 || body.Places[place].Kind != OwnershipPlaceKind.Local ||
                !ReferenceEquals(body.Places[place].Type, BoundType.String))
            {
                return false;
            }

            flags[place] = 1;
        }

        for (var i = 0; i < body.CleanupSteps.Count; i++)
        {
            var step = body.CleanupSteps[i];
            if (step.Action == CleanupAction.Conditional && ReferenceEquals(body.Places[step.Place].Type, BoundType.String) && flags[step.Place] == 0)
            {
                return false;
            }
        }

        foreach (var instruction in function.Instructions)
        {
            if (instruction.Opcode != EmissionOpcode.StoreLiveFlag)
            {
                continue;
            }

            if ((uint)instruction.Operation >= (uint)body.Operations.Count || (uint)instruction.Place >= (uint)flags.Length || flags[instruction.Place] == 0)
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
            StringFlagTransition(body.Operations[id], out var clear, out var initialize);
            var expected = (clear >= 0 && flags[clear] != 0 ? 1 : 0) | (initialize >= 0 && flags[initialize] != 0 ? 2 : 0);
            if (body.IsReachable(id) && seen[id] != expected)
            {
                return false;
            }
        }

        return true;
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
                clear = operation.Place;
                break;
            case OwnershipOperationKind.Produce:
                initialize = operation.Place;
                break;
            case OwnershipOperationKind.Consume:
                clear = operation.Acquisition == AcquisitionKind.Move ? operation.Place : -1;
                initialize = operation.Input;
                break;
            case OwnershipOperationKind.Write:
                clear = operation.Input;
                initialize = operation.Place;
                break;
        }
    }

    private bool IsStringStorage(OwnershipPlace place) => place.Kind switch
    {
        OwnershipPlaceKind.Local => place.Source is FieldKoto,
        OwnershipPlaceKind.Temporary => place.Source is StringLiteralKoto or IdentifierNameKoto,
        OwnershipPlaceKind.Result => this.stringResultPlaces[place.Id] != 0,
        _ => false,
    };

    private bool IsStringValue(OwnershipPlace place) => place.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && this.IsStringStorage(place);

    private bool PrepareStrings(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
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

            if (step.Action != CleanupAction.Conditional || !ReferenceEquals(body.Places[step.Place].Type, BoundType.String))
            {
                continue;
            }

            if (!this.IsStringStorage(body.Places[step.Place]) || body.Places[step.Place].Kind != OwnershipPlaceKind.Local ||
                body.Operations[step.Operation].Kind is not (OwnershipOperationKind.Cleanup or OwnershipOperationKind.Write) ||
                this.continuations[step.Operation] >= 0)
            {
                return Fail("Conditional string destruction requires a local lifetime and one split.", out failure);
            }

            this.liveFlags[step.Place] = 1;
            this.continuations[step.Operation] = count + step.Operation;
        }

        for (var i = 0; i < count; i++)
        {
            var operation = body.Operations[i];
            if (operation.Kind == OwnershipOperationKind.Declare && operation.Place >= 0 && this.liveFlags[operation.Place] != 0)
            {
                this.liveFlags[operation.Place] = 2;
            }
        }

        for (var p = 0; p < body.Places.Count; p++)
        {
            if (this.liveFlags[p] == 1)
            {
                return Fail("Conditional string lifetime has no Declare.", out failure);
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
                if (place.Kind != OwnershipPlaceKind.Local && this.stringResultDeclarations[id] == 0)
                {
                    return Fail("String Declare requires a local.", out failure);
                }

                break;
            case OwnershipOperationKind.Produce:
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
                    (operation.Kind == OwnershipOperationKind.Consume && (place.Kind != OwnershipPlaceKind.Local || body.Places[operation.Input].Kind != OwnershipPlaceKind.Temporary || operation.Acquisition != AcquisitionKind.Move)) ||
                    (operation.Kind == OwnershipOperationKind.Write && place.Kind != OwnershipPlaceKind.Local && this.stringResultWrites[id] == 0))
                {
                    return Fail("String transfer requires distinct verified source and destination storage.", out failure);
                }

                var source = operation.Kind == OwnershipOperationKind.Consume ? operation.Place : operation.Input;
                var destination = operation.Kind == OwnershipOperationKind.Consume ? operation.Input : operation.Place;
                if (body.IsReachable(id) && (body.GetInputState(id, source) & PlaceState.MustInit) == 0)
                {
                    return Fail("String transfer source is not initialized.", out failure);
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

    private bool LowerStringDestruction(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, ReadOnlySpan<byte> marks, out string? failure)
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
        var state = body.GetInputState(id, operation.Place);
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

        if (expected == CleanupAction.Skip)
        {
            return true;
        }

        if (!this.TryGetLocation(operation.Source, directory, constants, out var location))
        {
            return Fail("String destruction has no source location.", out failure);
        }

        if (expected == CleanupAction.Conditional)
        {
            function.AddScalar(EmissionOpcode.DestroyStringIfLive, id, [new(EmissionOperandKind.Block, this.continuations[id])], place: operation.Place, location: location);
        }
        else
        {
            function.AddCall(id, WindowsLowering.DestroyString, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        }

        return true;
    }
}
