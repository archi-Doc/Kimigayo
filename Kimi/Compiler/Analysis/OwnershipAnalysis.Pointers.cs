// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private static bool IsPointerPlace(Koto source)
        => source is DereferenceKoto || (source is IndexKoto index && ReferenceTypes.IsPointer(index.Left.BoundType));

    private bool SupportsPointerValue(Koto source)
    {
        var type = source.BoundType;
        return ScalarTypes.Supports(type) || ReferenceTypes.IsPointer(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) ||
            (type is not null && (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type)) &&
                this.compilation.Binding.ProveOwned(type, source) == ConstraintProof.Proven);
    }

    private int PointerAddress(Koto source)
    {
        if (source is DereferenceKoto dereference)
        {
            return this.Value(this.Expression(dereference.Operand, PlaceUseKind.Read));
        }

        // SPEC 5.3: form p + n once without evaluating a synthetic syntax tree.
        var index = (IndexKoto)source;
        var pointer = this.Value(this.Expression(index.Left, PlaceUseKind.Read));
        var offset = this.Value(this.Expression(index.Right, PlaceUseKind.Read));
        return pointer >= 0 && offset >= 0 && this.flow!.Nodes[source].CanCompleteNormally
            ? this.Value(this.ComputeUpdate(source, index.Left.BoundType, pointer, offset, KotoKind.Plus)) : -1;
    }

    private int ReadPointer(Koto source, PlaceUseKind use)
    {
        // A non-consuming access needs a raw Place/borrow plan, not a Move to a
        // disposable owner. In particular, string comparisons must not consume *p.
        if (!this.SupportsPointerValue(source) ||
            (use != PlaceUseKind.Consume && this.compilation.Binding.ProveCopy(source.BoundType!, source) != ConstraintProof.Proven))
        {
            this.Unsupported(source);
            return -1;
        }

        var pointer = this.PointerAddress(source);
        return pointer < 0 ? -1 : this.LoadPointer(source, pointer);
    }

    private int LoadPointer(Koto source, int pointer)
    {
        // SPEC 5.2: acquire Copy/Move into a fresh owner without a Loan. For Move,
        // the caller must prevent reads and destruction of the raw source storage.
        var loaded = this.Temporary(source);
        this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [pointer]);
        return loaded;
    }

    private int WritePointer(BinaryKoto assignment, Koto target)
    {
        if (!this.SupportsPointerValue(target))
        {
            this.Unsupported(assignment);
            return -1;
        }

        int value;
        int pointer;
        if (assignment.Akind == KotoKind.Equals)
        {
            // SPEC 13.7.1: secure the RHS before evaluating the destination.
            value = this.Expression(assignment.Right);
            pointer = value < 0 ? -1 : this.PointerAddress(target);
        }
        else
        {
            // SPEC 13.7.2: secure the address and old value once, before the RHS.
            pointer = this.PointerAddress(target);
            var loaded = pointer < 0 ? -1 : this.LoadPointer(target, pointer);
            var right = this.Expression(assignment.Right);
            value = loaded >= 0 && right >= 0 && this.flow!.Nodes[assignment].CanCompleteNormally
                ? this.ComputeUpdate(assignment, target.BoundType, this.Value(loaded), this.Value(right), KotoHelper.CompoundOperation(assignment.Akind)) : -1;
        }

        if (pointer < 0 || value < 0)
        {
            return -1;
        }

        this.StorePointer(target, pointer, value);
        return this.Temporary(assignment);
    }

    private void StorePointer(Koto target, int pointer, int value)
    {
        // Transfer the acquired source's responsibility to caller-managed storage.
        // No compiler-owned destination or temporary is created for the raw Place.
        var stored = this.Emit(OwnershipOperationKind.StorePointer, target, value, acquisition: this.body.Places[value].Acquisition);
        if (ScalarResult(target.BoundType!))
        {
            this.SetValue(stored, OwnershipValueKind.PointerStore, [pointer, this.Value(value)], constant: value);
        }
        else
        {
            this.SetValue(stored, OwnershipValueKind.PointerStore, [pointer], constant: value);
        }
    }

    private int UpdatePointer(UnaryKoto source, Koto target)
    {
        if (target.BoundType?.IsInteger != true)
        {
            this.Unsupported(source);
            return -1;
        }

        var pointer = this.PointerAddress(target);
        if (pointer < 0)
        {
            return -1;
        }

        var previous = this.Value(this.LoadPointer(target, pointer));
        var updated = this.ComputeUpdate(source, target.BoundType, previous, this.IncrementOne(source), ElementAccess.UpdateOperator(source.Akind));
        this.StorePointer(target, pointer, updated);
        return this.UpdateResult(source, previous, updated);
    }
}
