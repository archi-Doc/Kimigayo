// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, BoundType> inheritedOriginTypes = new(ReferenceEqualityComparer.Instance);

    // Supply omission defaults only. Normal Type binding and the owning feature's
    // complete-contract comparison still validate structure and explicit arguments.
    private void InheritOriginContract(Koto syntax, BoundType type)
    {
        this.inheritedOriginTypes[syntax] = type;
        switch (syntax)
        {
            case ParenthesizedTypeKoto grouped:
                this.InheritOriginContract(grouped.Type, type);
                break;
            case TypeSemanticsKoto { Type: { } target } semantics:
                if (semantics.IsTransparentWrapper || (semantics.SemanticsParameter is null && semantics.SemanticsKind == SemanticsKind.Owner))
                {
                    this.InheritOriginContract(target, type);
                }
                else if (type.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication && type.Components.Count == 1)
                {
                    this.InheritOriginContract(target, type.Components[0]);
                }

                break;
            case GenericsKoto generic when generic.TypeArguments.Count == type.Components.Count:
                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    this.InheritOriginContract(generic.TypeArguments[i], type.Components[i]);
                }

                break;
            case TupleTypeKoto tuple when tuple.ElementNodes.Count == type.Components.Count:
                for (var i = 0; i < tuple.ElementNodes.Count; i++)
                {
                    this.InheritOriginContract(tuple.ElementNodes[i], type.Components[i]);
                }

                break;
            case FixedArrayTypeKoto array when type.Kind == BoundTypeKind.FixedArray:
                this.InheritOriginContract(array.ElementType, type.Components[0]);
                break;
        }
    }

    private bool CompleteSpecializationOrigins(FunctionKoto function, FunctionKoto definition, BoundType?[] arguments)
    {
        var count = InputOriginCount(definition);
        var inputs = this.originScratch.Rent(count);
        Array.Clear(inputs, 0, count);
        try
        {
            for (var i = 0; i < definition.Parameters.Count; i++)
            {
                inputs[i] = this.OriginAtom(function, OriginKind.Input, i);
                if (definition.Parameters[i].Type.BoundType is { } pattern && function.Parameters[i].Type.BoundType is { } actual)
                {
                    MapInputSlots(pattern, actual);
                }
            }

            if (definition.BoundSymbol!.AggregateInputOrigins is { } aggregate)
            {
                var slots = function.BoundSymbol!.AggregateInputOrigins ??= new();
                for (var i = 0; i < aggregate.Count; i++)
                {
                    var original = aggregate[i];
                    if (inputs[original.Slot] is not null)
                    {
                        continue;
                    }

                    BoundOrigin? inherited = null;
                    for (var j = 0; j < slots.Count; j++)
                    {
                        if (ReferenceEquals(slots[j].Occurrence, original.Occurrence) && slots[j].TargetSlot == original.TargetSlot)
                        {
                            inherited = slots[j];
                            break;
                        }
                    }

                    if (inherited is null)
                    {
                        inherited = new(OriginKind.Input, function, function.Parameters.Count + slots.Count)
                        {
                            InputIndex = original.InputIndex,
                            Occurrence = original.Occurrence,
                            TargetSlot = original.TargetSlot,
                        };
                        slots.Add(inherited);
                    }

                    inputs[original.Slot] = inherited;
                }
            }

            var scope = this.scopes[function];
            var valid = true;
            for (var i = 0; i < definition.Parameters.Count; i++)
            {
                var pattern = this.SubstituteType(definition.Parameters[i].Type.BoundType!, definition, arguments);
                if (pattern is null)
                {
                    return false;
                }

                pattern = this.SubstituteStoredOrigins(pattern, definition, [], inputs.AsSpan(0, count));
                var syntax = function.Parameters[i].Type;
                this.InheritOriginContract(syntax, pattern);
                Reset(syntax);
                var actual = this.BindType(syntax, scope);
                this.symbols[function.Parameters[i]].Type = actual;
                valid &= ReferenceEquals(pattern, actual);
            }

            if (definition.BoundSymbol.Type is not { } output || this.SubstituteType(output, definition, arguments) is not { } result)
            {
                return false;
            }

            result = this.SubstituteStoredOrigins(result, definition, [], inputs.AsSpan(0, count));
            var actualResult = BoundType.Unit;
            if (function.ReturnType is { } returnSyntax)
            {
                this.InheritOriginContract(returnSyntax, result);
                Reset(returnSyntax);
                actualResult = this.BindType(returnSyntax, scope);
            }

            function.BoundSymbol!.Type = actualResult;
            return valid && ReferenceEquals(result, actualResult);
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
        }

        void MapInputSlots(BoundType pattern, BoundType actual)
        {
            // Reuse the corresponding preliminary occurrence; inheritance must
            // not allocate a second quantifier for the same input position.
            for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
            {
                if (pattern.OriginArguments[i] is { Kind: OriginKind.Input } original && ReferenceEquals(original.Binder, definition) &&
                    actual.OriginArguments[i] is { Kind: OriginKind.Input } inherited && ReferenceEquals(inherited.Binder, function))
                {
                    inputs[original.Slot] = inherited;
                }
            }

            if (pattern.Kind == BoundTypeKind.Slice && pattern.Origin is { Kind: OriginKind.Input } source && ReferenceEquals(source.Binder, definition) &&
                actual.Origin is { Kind: OriginKind.Input } target && ReferenceEquals(target.Binder, function))
            {
                inputs[source.Slot] = target;
            }

            for (var i = 0; i < Math.Min(pattern.Components.Count, actual.Components.Count); i++)
            {
                MapInputSlots(pattern.Components[i], actual.Components[i]);
            }
        }

        void Reset(Koto syntax)
        {
            // Preliminary elision is only a structural probe. Its obligations
            // mention different binders and must be regenerated with the inherited
            // contract, including obligations on explicitly written annotations.
            for (var i = this.obligations.Count - 1; i >= 0; i--)
            {
                for (var use = this.obligations[i].Use; use is not null && use is not FunctionKoto; use = use.Parent)
                {
                    if (ReferenceEquals(use, syntax))
                    {
                        this.obligationSet.Remove(this.obligations[i]);
                        this.obligations.RemoveAt(i);
                        break;
                    }
                }
            }

            var visitor = this.propertyTypeVisitor ??= new();
            var start = visitor.Snapshots.Count;
            visitor.Visit(syntax);
            // These preliminary signatures supplied only input structure. Retain
            // the newly completed contract instead of restoring old elision.
            visitor.Snapshots.RemoveRange(start, visitor.Snapshots.Count - start);
        }
    }
}
