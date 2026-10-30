// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly Dictionary<BoundType, TypeSupport> supportedTypes = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundType> visitingTypes = new();
    private readonly HashSet<BoundType> completeTuples = new(ReferenceEqualityComparer.Instance);

    private static int TypeDepth(BoundType type)
    {
        var depth = 0;
        for (var i = 0; i < type.Components.Count; i++)
        {
            depth = Math.Max(depth, TypeDepth(type.Components[i]));
        }

        return depth + 1;
    }

    // Storage without bytes: unit, empty or all-zero-sized structs, Tuples and closures, and fixed arrays of
    // length zero or of a zero-sized element. Call only after SupportsType accepted the Type (no inline cycles).
    private static bool IsZeroSized(BoundType type)
    {
        if (ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.Never) || type.Kind == BoundTypeKind.FunctionItem)
        {
            return true;
        }

        if (AdtDef.IsStruct(type))
        {
            if (AdtDef.Base(type) is { } parent && !IsZeroSized(parent))
            {
                return false;
            }

            for (var i = 0; i < AdtDef.Count(type); i++)
            {
                if (AdtDef.FieldType(type, i) is not { } field || !IsZeroSized(field))
                {
                    return false;
                }
            }

            return true;
        }

        if (type.Kind == BoundTypeKind.FixedArray)
        {
            return type.Length == 0 || (type.Components.Count == 1 && IsZeroSized(type.Components[0]));
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.Closure)
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!IsZeroSized(type.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    // Whether a value of the Type completes: no Never through its Tuples. A Tuple found complete is kept for the analysis, so a shared part
    // is decided once (DECISIONS BU-19, the owner's D5 exception).
    private bool AggregateCanComplete(BoundType type)
    {
        if (ReferenceEquals(type, BoundType.Never))
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Tuple && !this.completeTuples.Contains(type))
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!this.AggregateCanComplete(type.Components[i]))
                {
                    return false;
                }
            }

            this.completeTuples.Add(type);
        }

        return true;
    }

    // Why SupportsType refuses a Type, in precedence order (DECISIONS R4-M159): Refused, a Type this subset does not analyze, whose
    // located Unsupported_Kd lets the analysis go on; UndeclaredOriginArguments, a Type (or a part) carrying Origin arguments its
    // symbol does not declare (a Closure Type with its maker's images), at which the analysis stops (Stop).
    private enum TypeSupport : byte
    {
        Supported,
        Refused,
        UndeclaredOriginArguments,
    }

    private static TypeSupport Join(TypeSupport left, TypeSupport right) => left >= right ? left : right;

    // SPEC 6.1.3: the Origin parameters a Type's symbol declares, its enclosing declarations' included (the symbol's schema); a
    // symbol without one, a Tuple's, an array's or a Closure's, declares none.
    private static int DeclaredOrigins(BoundType type) => type.Symbol?.Schema?.Origins.Count ?? 0;

    // A Type's Origin arguments beyond the ones its symbol declares are undeclared (fewer is an incomplete Type, refused).
    private static TypeSupport Arguments(BoundType type)
        => type.OriginArguments.Count > DeclaredOrigins(type) ? TypeSupport.UndeclaredOriginArguments
            : type.OriginArguments.Count < DeclaredOrigins(type) ? TypeSupport.Refused : TypeSupport.Supported;

    // This is a conservative subset gate, not the language's finite-storage validation.
    // Every Case is checked because a whole value can arrive from a parameter or branch.
    private bool SupportsType(BoundType type) => this.Support(type) == TypeSupport.Supported;

    private TypeSupport Part(BoundType type) => type.Kind == BoundTypeKind.Parameter ? TypeSupport.Supported : this.Support(type);

    // SupportsType's answer with its refusal's reason; a refusal found first ends the walk of the remaining parts, as before.
    private TypeSupport Support(BoundType type)
    {
        var handle = type.HandleMode;
        if (handle is null && ReferenceTypes.IsReference(type))
        {
            handle = type.Components[0].HandleMode;
        }

        if (handle is not null)
        {
            return TypeSupport.Supported;
        }

        if (type.Kind == BoundTypeKind.Dictionary || ReferenceTypes.IsDictionary(type))
        {
            var library = this.compilation.Library;
            this.CollectLibraryBody(library.DictionaryAppendSlot);
            this.CollectLibraryBody((FunctionKoto)library.DictionaryStorage(DictionaryCallee.Initialize).Declaration);
            this.CollectLibraryBody(library.DictionaryClearLinks);
            this.CollectLibraryBody((FunctionKoto)library.DictionaryStorage(DictionaryCallee.FindEntry).Declaration);
            this.CollectLibraryBody((FunctionKoto)library.DictionaryStorage(DictionaryCallee.ClearEntries).Declaration);
            // The typed placement helper calls append by name; reserve and shrinkToFit are reached through ordinary calls.
            this.CollectLibraryBody((FunctionKoto)library.DictionaryStorage(DictionaryCallee.Append).Declaration);
        }

        if (ReferenceTypes.IsString(type) || ReferenceTypes.IsBorrow(type) || ReferenceTypes.IsPointer(type))
        {
            return TypeSupport.Supported;
        }

        if (type.Kind is BoundTypeKind.Slice or BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
        {
            return TypeSupport.Supported;
        }

        if (type.Kind == BoundTypeKind.Array)
        {
            // SPEC 4.5: a handle owns its buffer and recursively destroys its stored elements. A generic element is
            // checked again by each instance under its substitution; zero-sized elements still need no allocation.
            return this.Part(type.Components[0]);
        }

        if (type.Kind == BoundTypeKind.Dictionary)
        {
            if (type.Components.Count != 2)
            {
                return TypeSupport.Refused;
            }

            var key = this.Part(type.Components[0]);
            return key != TypeSupport.Supported ? key : this.Part(type.Components[1]);
        }

        if (type.Kind == BoundTypeKind.Primitive)
        {
            return ReferenceEquals(type, BoundType.Never) ? TypeSupport.Refused : TypeSupport.Supported;
        }

        if (this.supportedTypes.TryGetValue(type, out var supported))
        {
            return supported;
        }

        if (AdtDef.Declaration(type) is { } structure)
        {
            if (AdtDef.Destructor(type) is { } destructor)
            {
                this.CollectLibraryBody(destructor);
            }

            // Reserve the key before following fields to reject recursive inline storage.
            this.supportedTypes[type] = TypeSupport.Refused;
            var parent = AdtDef.Base(type);
            supported = parent is null ? TypeSupport.Supported : this.Support(parent);
            // An inherited Origin is declared (DeclaredOrigins) but outside this subset, refused as before with the analysis going on.
            supported = supported != TypeSupport.Supported ? supported
                : type.OriginArguments.Count > DeclaredOrigins(type) ? TypeSupport.UndeclaredOriginArguments
                : type.OriginArguments.Count > structure.OriginNames.Count ? TypeSupport.Refused : TypeSupport.Supported;
            for (var i = 0; i < AdtDef.Count(type) && supported == TypeSupport.Supported; i++)
            {
                // Collection fields retain their recursive destruction plans (SPEC 16.3.2).
                supported = AdtDef.FieldType(type, i) is { } field ? this.Part(field) : TypeSupport.Refused;
            }

            this.supportedTypes[type] = supported;
            return supported;
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure)
        {
            supported = type.Semantics == SemanticsKind.Owner && type.Origin is null ? Arguments(type) : TypeSupport.Refused;
            for (var i = 0; i < type.Components.Count; i++)
            {
                supported = Join(supported, this.Part(type.Components[i]));
            }

            this.supportedTypes[type] = supported;
            return supported;
        }

        if (type.Semantics != SemanticsKind.Owner || type.Origin is not null ||
            type.Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) ||
            type.Symbol is not { Declaration: EnumKoto, Adt.Types: { } storage })
        {
            this.supportedTypes[type] = TypeSupport.Refused;
            return TypeSupport.Refused;
        }

        if (Arguments(type) is not TypeSupport.Supported and var arguments)
        {
            this.supportedTypes[type] = arguments;
            return arguments;
        }

        // Allow finite nesting such as Option<Option<i32>>, but refuse inline cycles
        // and growing generic instances before storage substitution can expand forever.
        for (var i = 0; i < this.visitingTypes.Count; i++)
        {
            var ancestor = this.visitingTypes[i];
            if (ReferenceEquals(ancestor.Symbol, type.Symbol) && TypeDepth(type) >= TypeDepth(ancestor))
            {
                return TypeSupport.Refused;
            }
        }

        this.visitingTypes.Add(type);
        supported = TypeSupport.Supported;
        for (var i = 0; i < type.Components.Count && supported == TypeSupport.Supported; i++)
        {
            supported = this.Part(type.Components[i]);
        }

        for (var i = 0; i < storage.Count && supported == TypeSupport.Supported; i++)
        {
            supported = this.compilation.Binding.StoredType(storage[i], type) is { } payload ? this.Part(payload) : TypeSupport.Refused;
        }

        this.visitingTypes.RemoveAt(this.visitingTypes.Count - 1);
        supported = supported == TypeSupport.Supported && Array.IndexOf(AdtDef.CaseTypes(type)!, null) >= 0 ? TypeSupport.Refused : supported;
        this.supportedTypes[type] = supported;
        return supported;
    }

    private void CheckAcquisition(int place, AcquisitionKind? acquisition)
    {
        if (place < 0 || acquisition is not { } committed)
        {
            return;
        }

        // A committed CopyOrMove is checked as the exact effect of the source Place's own Type under the environment its Place was
        // built in (SPEC 8.15), and a transfer (@move) moves a Copy or Copy-unproven Place (SPEC 13.5.3).
        var source = this.body.PlaceStorage[place];
        var expected = committed == AcquisitionKind.CopyOrMove
            ? this.ExactAcquisition(this.CopyProof(source.Type, source.Source, this.body.ContextOf(place)), source.Type, source.Source) : committed;
        if (source.Acquisition != expected && !(expected == AcquisitionKind.Move && source.Acquisition is AcquisitionKind.Copy or AcquisitionKind.CopyOrMove))
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
            var acquisition = this.body.PlaceStorage[start + i].Acquisition; // Committed, or resolved for an instance.
            if ((acquisition == AcquisitionKind.None) == (operation.Kind is ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.ReferenceRead))
            {
                throw new InvalidOperationException("Committed payload operation has no matching acquisition kind.");
            }

            // SPEC 10.2, 12.2: payload borrows use the same prepared reference and actual dependency as call inputs.
            // They enter the enum immediately, without a call activation; an explicitly transferred reference keeps its acquisition.
            var borrow = operation.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection or ArgumentOperationKind.ReferenceRead &&
                !(operation.Kind == ArgumentOperationKind.Reborrow && KotoHelper.UnwrapParentheses(operation.Source!) is ConversionKoto { ConversionBinding: ConversionBinding.Transfer });
            var value = borrow && source is InvocationKoto invocation ? this.PrepareCallArgument(invocation, operation.Source!, operation, immediate: true)
                : this.Argument(operation.Source!, operation.Kind, acquisition == AcquisitionKind.None ? null : acquisition);
            var payload = start + i;
            this.Emit(OwnershipOperationKind.PayloadPlacement, operation.Source!, payload, value);
            // Placement completes after evaluation, interleaving with surviving temporaries.
            this.RegisterTemporary(payload);
        }

        var complete = this.Emit(OwnershipOperationKind.CompleteConstruction, source, output);
        this.body.OperationSteps[complete] = plan;
        return this.RegisterTemporary(output);
    }

    private int ConstructAggregate(Koto source, IReadOnlyList<Koto> elements)
    {
        if (source.BoundType is { } sourceType && !this.AggregateCanComplete(sourceType))
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
            var component = type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array ? type.Components[0] : type.Components[i];
            var payload = this.Place(elements[i], component, OwnershipPlaceKind.Payload, true);
            this.Emit(OwnershipOperationKind.Declare, source, payload);
        }

        var plan = this.body.ConstructionStorage.Count;
        this.body.ConstructionStorage.Add(new(output, null, start, elements.Count));
        // Placed exclusive references are reserved until the literal completes, as the arguments of a call are.
        var loanDepth = this.comparisonDepth++;
        var completes = true;
        for (var i = 0; i < elements.Count; i++)
        {
            var value = this.Expression(elements[i]);
            if (value < 0)
            {
                completes = false;
                continue;
            }

            var reservationMark = this.body.CallReservations.Count;
            this.ReservePlacedReference(source, elements[i], start + i, value);
            this.ActivateCallReservations(source, reservationMark);
            this.Emit(OwnershipOperationKind.PayloadPlacement, elements[i], start + i, value);
            this.RegisterTemporary(start + i);
        }

        if (!completes)
        {
            this.EndComparisonLoans(loanDepth, source);
            this.comparisonDepth = loanDepth;
            return -1;
        }

        var complete = this.Emit(OwnershipOperationKind.CompleteConstruction, source, output);
        this.body.OperationSteps[complete] = plan;
        this.EndComparisonLoans(loanDepth, source);
        this.comparisonDepth = loanDepth;
        return this.RegisterTemporary(output);
    }
}
