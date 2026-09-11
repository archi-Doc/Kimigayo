// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly Dictionary<BoundType, bool> supportedTypes = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundType> visitingTypes = new();

    private static int TypeDepth(BoundType type)
    {
        var depth = 0;
        for (var i = 0; i < type.Components.Count; i++)
        {
            depth = Math.Max(depth, TypeDepth(type.Components[i]));
        }

        return depth + 1;
    }

    // This is a conservative subset gate, not the language's finite-storage validation.
    // Every Case is checked because a whole value can arrive from a parameter or branch.
    private bool SupportsType(BoundType type)
    {
        if (type.Kind == BoundTypeKind.Primitive)
        {
            return !ReferenceEquals(type, BoundType.Never);
        }

        if (this.supportedTypes.TryGetValue(type, out var supported))
        {
            return supported;
        }

        if (type.Semantics != SemanticsKind.Owner || type.Origin is not null || type.OriginArguments.Count != 0 ||
            type.Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) ||
            this.compilation.Binding.EnumStorage(type) is not { } storage)
        {
            this.supportedTypes[type] = false;
            return false;
        }

        // Allow finite nesting such as Option<Option<i32>>, but refuse inline cycles
        // and growing generic instances before storage substitution can expand forever.
        for (var i = 0; i < this.visitingTypes.Count; i++)
        {
            var ancestor = this.visitingTypes[i];
            if (ReferenceEquals(ancestor.Symbol, type.Symbol) && TypeDepth(type) >= TypeDepth(ancestor))
            {
                return false;
            }
        }

        this.visitingTypes.Add(type);
        supported = true;
        for (var i = 0; i < type.Components.Count && supported; i++)
        {
            supported = this.SupportsType(type.Components[i]);
        }

        for (var i = 0; i < storage.Count && supported; i++)
        {
            supported = this.compilation.Binding.StoredType(storage[i], type) is { } payload && this.SupportsType(payload);
        }

        this.visitingTypes.RemoveAt(this.visitingTypes.Count - 1);
        this.supportedTypes[type] = supported;
        return supported;
    }

    private void CheckAcquisition(int place, AcquisitionKind? acquisition)
    {
        if (place >= 0 && acquisition is { } expected && this.body.PlaceStorage[place].Acquisition != expected)
        {
            throw new InvalidOperationException("Committed payload acquisition disagrees with its source Place.");
        }
    }

    private int ConstructEnum(Koto source, BoundEnumConstruction construction)
    {
        var output = this.Temporary(source, false);
        this.Emit(OwnershipOperationKind.Declare, source, output);
        var start = this.body.PlaceStorage.Count;
        var operations = construction.PayloadOperations;
        for (var i = 0; i < operations.Length; i++)
        {
            var payload = this.Place(operations[i].Source!, operations[i].ParameterType, OwnershipPlaceKind.Payload, true, construction.Acquisitions[i]);
            this.Emit(OwnershipOperationKind.Declare, source, payload);
        }

        var plan = this.body.ConstructionStorage.Count;
        this.body.ConstructionStorage.Add(new(output, construction.Case, start, operations.Length));
        for (var i = 0; i < operations.Length; i++)
        {
            var operation = operations[i];
            var acquisition = construction.Acquisitions[i];
            if ((acquisition == AcquisitionKind.None) == (operation.Kind == ArgumentOperationKind.Value))
            {
                throw new InvalidOperationException("Committed payload operation has no matching acquisition kind.");
            }

            // The shared argument path records Borrow/Reborrow as unsupported until Loan checking exists.
            var value = this.Argument(operation.Source!, operation.Kind, acquisition == AcquisitionKind.None ? null : acquisition);
            var payload = start + i;
            this.Emit(OwnershipOperationKind.PayloadPlacement, operation.Source!, payload, value);
            // Placement completes after evaluation, interleaving with surviving temporaries.
            this.RegisterTemporary(payload);
        }

        var complete = this.Emit(OwnershipOperationKind.CompleteConstruction, source, output);
        this.body.OperationSteps[complete] = plan;
        return this.RegisterTemporary(output);
    }
}
