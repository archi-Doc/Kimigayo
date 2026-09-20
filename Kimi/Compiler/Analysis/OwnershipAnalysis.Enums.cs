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

    private static bool AggregateCanComplete(BoundType type)
    {
        if (ReferenceEquals(type, BoundType.Never))
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Tuple)
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!AggregateCanComplete(type.Components[i]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // This is a conservative subset gate, not the language's finite-storage validation.
    // Every Case is checked because a whole value can arrive from a parameter or branch.
    private bool SupportsType(BoundType type)
    {
        if (ReferenceTypes.IsString(type) || ReferenceTypes.IsBorrow(type) || ObjectTypes.IsOwner(type))
        {
            return true;
        }

        if (type.Kind is BoundTypeKind.ResolvedRange or BoundTypeKind.Slice or BoundTypeKind.Function)
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.Primitive)
        {
            return !ReferenceEquals(type, BoundType.Never);
        }

        if (this.supportedTypes.TryGetValue(type, out var supported))
        {
            return supported;
        }

        if (StructStorage.Declaration(type) is { } structure)
        {
            // Reserve the key before following fields to reject recursive inline storage.
            this.supportedTypes[type] = false;
            supported = structure.Bases.Count == 0 &&
                type.OriginArguments.Count <= structure.OriginNames.Count;
            var count = StructStorage.Count(type);
            if (type.StoredFields?.Length != count)
            {
                type.StoredFields = new BoundType[count];
            }

            for (var i = 0; i < StructStorage.Count(type) && supported; i++)
            {
                var field = this.compilation.Binding.StoredType(StructStorage.Field(type, i), type);
                supported = field is not null && (field.Kind == BoundTypeKind.Parameter || this.SupportsType(field));
                type.StoredFields[i] = field!;
            }

            this.supportedTypes[type] = supported;
            return supported;
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure)
        {
            supported = type.Semantics == SemanticsKind.Owner && type.Origin is null && type.OriginArguments.Count == 0;
            for (var i = 0; i < type.Components.Count; i++)
            {
                supported &= type.Components[i].Kind == BoundTypeKind.Parameter || this.SupportsType(type.Components[i]);
            }

            this.supportedTypes[type] = supported;
            return supported;
        }

        if (type.Semantics != SemanticsKind.Owner || type.Origin is not null ||
            type.OriginArguments.Count != (type.Symbol?.Schema?.Origins.Count ?? 0) ||
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
            supported = type.Components[i].Kind == BoundTypeKind.Parameter || this.SupportsType(type.Components[i]);
        }

        for (var i = 0; i < storage.Count && supported; i++)
        {
            supported = this.compilation.Binding.StoredType(storage[i], type) is { } payload && (payload.Kind == BoundTypeKind.Parameter || this.SupportsType(payload));
        }

        this.visitingTypes.RemoveAt(this.visitingTypes.Count - 1);
        supported = supported && this.compilation.Binding.PrepareEnumCases(type);
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

    private int ConstructAggregate(Koto source, List<Koto> elements)
    {
        if (source.BoundType is { } sourceType && !AggregateCanComplete(sourceType))
        {
            // Binding retains Never components in a tuple's inferred Type. Such a
            // tuple has no completed representation; earlier acquired temporaries
            // still participate in the transfer's ordinary reverse-order cleanup.
            for (var i = 0; i < elements.Count; i++)
            {
                this.Expression(elements[i]);
            }

            return -1;
        }

        var output = this.Temporary(source, false);
        this.Emit(OwnershipOperationKind.Declare, source, output);
        var type = this.body.Places[output].Type;
        var start = this.body.Places.Count;
        for (var i = 0; i < elements.Count; i++)
        {
            var component = type.Kind == BoundTypeKind.FixedArray ? type.Components[0] : type.Components[i];
            var payload = this.Place(elements[i], component, OwnershipPlaceKind.Payload, true);
            this.Emit(OwnershipOperationKind.Declare, source, payload);
        }

        var plan = this.body.ConstructionStorage.Count;
        this.body.ConstructionStorage.Add(new(output, null, start, elements.Count));
        var completes = true;
        for (var i = 0; i < elements.Count; i++)
        {
            var value = this.Expression(elements[i]);
            if (value < 0)
            {
                completes = false;
                continue;
            }

            this.Emit(OwnershipOperationKind.PayloadPlacement, elements[i], start + i, value);
            this.RegisterTemporary(start + i);
        }

        if (!completes)
        {
            return -1;
        }

        var complete = this.Emit(OwnershipOperationKind.CompleteConstruction, source, output);
        this.body.OperationSteps[complete] = plan;
        return this.RegisterTemporary(output);
    }
}
