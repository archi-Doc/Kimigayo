// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, BoundType> inheritedOriginTypes = new(ReferenceEqualityComparer.Instance);

    private static OriginContractFact? ImplementationOriginMismatch(Koto at, FunctionKoto definition, BoundType expected, BoundType actual, string member)
    {
        if (expected.Origin is { } required && actual.Origin is { } written && !ReferenceEquals(required, written))
        {
            return new(at, member, required, written, true, Required: definition);
        }

        for (var i = 0; i < Math.Min(expected.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            if (!ReferenceEquals(expected.OriginArguments[i], actual.OriginArguments[i]))
            {
                return new(at, member, expected.OriginArguments[i], actual.OriginArguments[i], true, Required: definition);
            }
        }

        for (var i = 0; i < Math.Min(expected.Components.Count, actual.Components.Count); i++)
        {
            if (ImplementationOriginMismatch(at, definition, expected.Components[i], actual.Components[i], member) is { } mismatch)
            {
                return mismatch;
            }
        }

        return null;
    }

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
            case PlaceResultKoto place:
                this.InheritOriginContract(place.Type, type);
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

    private bool CompleteImplementationOrigins(FunctionKoto function, FunctionKoto definition, BoundType?[] arguments, BoundLength?[] lengths, out OriginContractFact? incompatible, BoundType? declaringType = null, BoundType? implementingType = null)
    {
        incompatible = null;
        var originalDeclaration = this.originDeclarations.GetValueOrDefault(definition);
        var count = InputOriginCount(definition);
        var definitionOrigins = definition.BoundSymbol!.Schema?.Origins ?? [];
        var inputs = this.originScratch.Rent(count);
        var binders = this.originScratch.Rent(definitionOrigins.Count);
        Array.Clear(inputs, 0, count);
        Array.Clear(binders, 0, definitionOrigins.Count);
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

            // SPEC 8.8.2 and 6.2.4: implementations inherit named Origin binders without adding or renaming them.
            var written = function.BoundSymbol!.Schema?.Origins ?? [];
            for (var i = 0; i < definitionOrigins.Count; i++)
            {
                BoundOrigin? inherited = null;
                for (var j = 0; j < written.Count; j++)
                {
                    if (written[j].Name == definitionOrigins[i].Name)
                    {
                        inherited = written[j].Origin;
                        break;
                    }
                }

                // An omitted name is inherited through the first parameter position the original binds it at.
                for (var p = 0; inherited is null && p < definition.Parameters.Count; p++)
                {
                    var originalOrigin = originalDeclaration is null ? definitionOrigins[i].Origin : this.ResolveOrigin(definitionOrigins[i].Origin, originalDeclaration);
                    if (definition.Parameters[p].Type.BoundType is { } parameter && MentionsOrigin(parameter, originalOrigin))
                    {
                        inherited = inputs[p];
                    }
                }

                if (inherited is null)
                {
                    return false; // A result-only binder has no position to inherit from.
                }

                binders[definitionOrigins[i].Slot] = inherited;
            }

            for (var j = 0; j < written.Count; j++)
            {
                var known = false;
                for (var i = 0; i < definitionOrigins.Count; i++)
                {
                    known |= definitionOrigins[i].Name == written[j].Name;
                }

                if (!known)
                {
                    return false; // A specialization cannot add an Origin parameter.
                }
            }

            var scope = this.scopes[function];
            var inheritedDeclaration = this.originDeclarations.GetValueOrDefault(function);
            if (originalDeclaration is not null || inheritedDeclaration is not null)
            {
                inheritedDeclaration ??= this.OriginDeclarationFor(function);
                inheritedDeclaration.Scope = scope;
                inheritedDeclaration.State = 3;
                // A written implementation clause is a claim to check, never its own premise.
                // Rebuild the environment solely from the original before rebinding the header.
                inheritedDeclaration.Replacements.Clear();
                inheritedDeclaration.Relations.Clear();
                if (originalDeclaration is not null)
                {
                    foreach (var replacement in originalDeclaration.Replacements)
                    {
                        if (ReferenceEquals(replacement.Key.Binder, definition) && replacement.Key.Kind is OriginKind.Parameter or OriginKind.Input)
                        {
                            var from = BindContractOrigin(replacement.Key);
                            var to = BindContractOrigin(this.ResolveOrigin(replacement.Value, originalDeclaration));
                            if (!ReferenceEquals(from, to))
                            {
                                inheritedDeclaration.Replacements[from] = to;
                            }
                        }
                    }

                    foreach (var relation in originalDeclaration.Relations)
                    {
                        inheritedDeclaration.Relations.Add(new(BindContractOrigin(relation.Longer), BindContractOrigin(relation.Shorter), relation.Equality, relation.Syntax));
                    }
                }
            }

            var valid = true;
            for (var i = 0; i < definition.Parameters.Count; i++)
            {
                var pattern = BindContractType(definition.Parameters[i].Type.BoundType!);
                if (pattern is null)
                {
                    return false;
                }

                pattern = this.SubstituteStoredOrigins(pattern, definition, binders.AsSpan(0, definitionOrigins.Count), inputs.AsSpan(0, count));
                if (implementingType is not null && i == definition.BoundSymbol.ReceiverIndex)
                {
                    // Only the receiver's core Self changes. Its public Origin is inherited unchanged.
                    pattern = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [implementingType], origin: pattern.Origin);
                }

                var syntax = function.Parameters[i].Type;
                this.InheritOriginContract(syntax, pattern);
                Reset(syntax);
                var actual = this.BindType(syntax, scope);
                this.symbols[function.Parameters[i]].Type = actual;
                valid &= ReferenceEquals(pattern, actual);
                if (actual is not null && !ReferenceEquals(pattern, actual))
                {
                    incompatible ??= ImplementationOriginMismatch(syntax, definition, pattern, actual, "the input '" + function.Parameters[i].ExternalName + "'");
                }
            }

            if (definition.BoundSymbol.Type is not { } output || BindContractType(output) is not { } result)
            {
                return false;
            }

            result = this.SubstituteStoredOrigins(result, definition, binders.AsSpan(0, definitionOrigins.Count), inputs.AsSpan(0, count));
            var actualResult = BoundType.Unit;
            if (function.ReturnType is { } returnSyntax)
            {
                this.InheritOriginContract(returnSyntax, result);
                Reset(returnSyntax);
                actualResult = this.BindType(returnSyntax, scope);
            }

            function.BoundSymbol!.Type = actualResult;
            if (actualResult is not null && !ReferenceEquals(result, actualResult))
            {
                incompatible ??= ImplementationOriginMismatch(function.ReturnType ?? function, definition, result, actualResult, "the result");
            }

            var clauses = OriginClauses.Get(function);
            for (var i = 0; i < clauses.Count; i++)
            {
                var clause = clauses[i];
                var a = this.BindOrigin(clause.Left, scope);
                var b = this.BindOrigin(clause.Right, scope);
                if (a is null || b is null || !this.ProvesOriginOutlives(a, b, function) || (clause.IsEquality && !this.ProvesOriginOutlives(b, a, function)))
                {
                    if (a is not null && b is not null)
                    {
                        incompatible ??= new(clause, "the Origin clause", a, b, clause.IsEquality, Required: definition);
                    }

                    valid = false;
                    break;
                }
            }

            return valid && ReferenceEquals(result, actualResult);
        }
        finally
        {
            this.originScratch.Return(binders, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
        }

        BoundType? BindContractType(BoundType type)
            => this.SubstituteType(type, definition, arguments, lengths) is { } substituted ? this.MemberType(substituted, declaringType) : null;

        BoundOrigin BindContractOrigin(BoundOrigin origin)
        {
            if (declaringType?.Symbol is { } owner)
            {
                origin = this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
            }

            return this.SubstituteStoredOrigin(origin, definition, binders.AsSpan(0, definitionOrigins.Count), inputs.AsSpan(0, count));
        }

        static bool MentionsOrigin(BoundType type, BoundOrigin origin)
        {
            if (ReferenceEquals(type.Origin, origin))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (ReferenceEquals(type.OriginArguments[i], origin))
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (MentionsOrigin(type.Components[i], origin))
                {
                    return true;
                }
            }

            return false;
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
