// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool IsBorrow(SemanticsKind semantics) => semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    private static bool IsExclusive(SemanticsKind semantics) => semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    private static int InputCount(Koto owner) => owner is FunctionKoto f ? f.Parameters.Count : owner is PropertyAccessorKoto ? 2 : owner is FunctionTypeKoto t ? t.Parameters is TupleTypeKoto tuple ? tuple.ElementNodes.Count : 1 : 0;

    private static int InputOriginCount(Koto owner) => InputCount(owner) + (owner.BoundSymbol?.AggregateInputOrigins?.Count ?? 0);

    private static Koto? InputType(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].Type : owner is PropertyAccessorKoto a ? index == 0 ? a.ReceiverType : a.ValueType : owner is FunctionTypeKoto t ? t.Parameters is TupleTypeKoto tuple ? tuple.ElementNodes[index] : t.Parameters : null;

    private static string InputName(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].InternalName : index == 0 ? "self" : "value";

    private static BoundType? BoundInputType(Koto owner, int index)
        => owner is PropertyAccessorKoto accessor ? index == 0 ? Accessor(accessor).Receiver : Accessor(accessor).Input : InputType(owner, index)?.BoundType;

    private static int CompareOrigins(BoundOrigin a, BoundOrigin b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        var order = a.Kind.CompareTo(b.Kind);
        if (order != 0)
        {
            return order;
        }

        if (a.Binder is { } left && b.Binder is { } right)
        {
            order = StringComparer.Ordinal.Compare(left.CodeContext.Kotonoha.Name, right.CodeContext.Kotonoha.Name);
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(left.CodeContext.SourceDocument?.Path, right.CodeContext.SourceDocument?.Path);
            }

            if (order == 0)
            {
                order = left.Span.Start.CompareTo(right.Span.Start);
            }

            if (order == 0)
            {
                order = left.Span.End.CompareTo(right.Span.End);
            }

            // Source-less generated fragments still have immutable source snapshots. Never order by addresses.
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(left.CodeContext.SourceDocument?.SourceText, right.CodeContext.SourceDocument?.SourceText);
            }

            if (order != 0)
            {
                return order;
            }
        }

        order = a.Slot.CompareTo(b.Slot);
        if (order != 0)
        {
            return order;
        }

        order = a.Operands.Count.CompareTo(b.Operands.Count);
        for (var i = 0; order == 0 && i < a.Operands.Count; i++)
        {
            order = CompareOrigins(a.Operands[i], b.Operands[i]);
        }

        return order;
    }

    /// <summary>Proves only context-independent outlives facts; lack of proof is not equality.</summary>
    private static bool OriginOutlives(BoundOrigin a, BoundOrigin b)
    {
        if (ReferenceEquals(a, b) || a.Kind == OriginKind.Static)
        {
            return true;
        }

        if (a.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < a.Operands.Count; i++)
            {
                if (!OriginOutlives(a.Operands[i], b))
                {
                    return false;
                }
            }

            return true;
        }

        if (b.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < b.Operands.Count; i++)
            {
                if (OriginOutlives(a, b.Operands[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private BoundOrigin OriginAtom(Koto binder, OriginKind kind, int slot, string? name = null)
    {
        var key = (binder, kind, slot);
        if (!this.originAtoms.TryGetValue(key, out var origin))
        {
            this.originAtoms.Add(key, origin = new(kind, binder, slot, name));
        }

        return origin;
    }

    private BoundOrigin Meet(BoundOrigin a, BoundOrigin b)
    {
        if (OriginOutlives(a, b))
        {
            return b;
        }

        if (OriginOutlives(b, a))
        {
            return a;
        }

        var capacity = (a.Kind == OriginKind.Intersection ? a.Operands.Count : 1) + (b.Kind == OriginKind.Intersection ? b.Operands.Count : 1);
        var scratch = this.originScratch.Rent(capacity);
        try
        {
            var count = 0;
            Add(a);
            Add(b);
            // Small Origin sets: insertion sort avoids a comparison delegate or boxed enumerator.
            for (var i = 1; i < count; i++)
            {
                var item = scratch[i];
                var j = i;
                while (j > 0 && CompareOrigins(item, scratch[j - 1]) < 0)
                {
                    scratch[j] = scratch[j - 1];
                    j--;
                }

                scratch[j] = item;
            }

            var hash = default(HashCode);
            for (var i = 0; i < count; i++)
            {
                hash.Add(RuntimeHelpers.GetHashCode(scratch[i]));
            }

            var key = hash.ToHashCode();
            if (!this.originExpressions.TryGetValue(key, out var bucket))
            {
                this.originExpressions.Add(key, bucket = new(1));
            }

            for (var i = 0; i < bucket.Count; i++)
            {
                var candidate = bucket[i];
                if (candidate.Operands.Count != count)
                {
                    continue;
                }

                var equal = true;
                for (var j = 0; j < count; j++)
                {
                    equal &= ReferenceEquals(candidate.Operands[j], scratch[j]);
                }

                if (equal)
                {
                    return candidate;
                }
            }

            var created = new BoundOrigin(OriginKind.Intersection, operands: scratch.AsSpan(0, count).ToArray());
            bucket.Add(created);
            return created;

            void Add(BoundOrigin origin)
            {
                if (origin.Kind == OriginKind.Intersection)
                {
                    for (var i = 0; i < origin.Operands.Count; i++)
                    {
                        Add(origin.Operands[i]);
                    }

                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    if (ReferenceEquals(scratch[i], origin))
                    {
                        return;
                    }
                }

                scratch[count++] = origin;
            }
        }
        finally
        {
            this.originScratch.Return(scratch, clearArray: true);
        }
    }

    private void BindSchemas()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            var node = this.nodes[n];
            IReadOnlyList<TypeKoto> parameters;
            IReadOnlyList<string> origins;
            if (node is FunctionKoto { IsGenerated: false, IsAnonymous: false } function)
            {
                // Specialization arguments are concrete Types, not generic declaration slots.
                parameters = function.IsSpecialization ? [] : function.GenericArguments;
                origins = function.Origins;
            }
            else if (node is PropertyAccessorKoto accessor)
            {
                parameters = [];
                origins = accessor.Origins;
            }
            else if (node is DeclarationContainerKoto { IsRoot: false } container)
            {
                if (container.Parent is not (GroupKoto or StructKoto) &&
                    container.Parent is not CodeBlockKoto { DeclarationContext: Lexing.TokenKind.Group or Lexing.TokenKind.Struct })
                {
                    Fail(node, BindingFailure.InvalidTypeFormation);
                }

                if (container.HasIncompatibleBindingHeader)
                {
                    Fail(node, BindingFailure.Duplicate);
                }

                parameters = container.GenericParameterNodes;
                origins = container.OriginNames;
            }
            else
            {
                continue;
            }

            var symbol = node.BoundSymbol!;
            var scope = this.scopes[node];
            origins = this.DiscoverOriginNames(node, origins, scope);
            var inherited = node is DeclarationContainerKoto && node.Parent is DeclarationContainerKoto parent ? parent.BoundSymbol?.Schema : null;
            var genericCount = parameters.Count + (inherited?.GenericSlots.Count ?? 0);
            var originCount = origins.Count + (inherited?.Origins.Count ?? 0);
            var schema = symbol.Schema;
            if (schema is null || schema.GenericSlots.Count != genericCount || schema.Origins.Count != originCount)
            {
                var slots = genericCount == 0 ? Array.Empty<GenericSlot>() : new GenericSlot[genericCount];
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = this.symbols[parameters[i]];
                    slots[i] = new(parameters[i] is LengthParameterKoto ? GenericSlotKind.Length : parameter.Pair is not null ? GenericSlotKind.Pair : GenericSlotKind.Type, parameter, parameter.Pair);
                }

                for (var i = 0; inherited is not null && i < inherited.GenericSlots.Count; i++)
                {
                    var outer = inherited.GenericSlots[i];
                    slots[parameters.Count + i] = new(outer.Kind, outer.Symbol, outer.Semantics);
                }

                var bindings = originCount == 0 ? Array.Empty<OriginParameter>() : new OriginParameter[originCount];
                for (var i = 0; i < origins.Count; i++)
                {
                    bindings[i] = new(origins[i], i, this.OriginAtom(node, OriginKind.Parameter, i, origins[i]), origins is OriginNameList locations && i < locations.Spans.Count ? locations.Spans[i] : node.Span);
                }

                for (var i = 0; inherited is not null && i < inherited.Origins.Count; i++)
                {
                    var outer = inherited.Origins[i];
                    bindings[origins.Count + i] = new(outer.Name, outer.Slot, outer.Origin, outer.Span);
                }

                symbol.Schema = schema = new(slots, bindings);
            }

            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                schema.GenericSlots[i].OriginVariance = OriginVariance.Unused;
                if (i >= parameters.Count)
                {
                    continue;
                }

                schema.GenericSlots[i].Symbol.Slot = i;
                if (schema.GenericSlots[i].Semantics is { } semantics)
                {
                    semantics.Slot = i;
                }
            }

            for (var i = 0; i < schema.Origins.Count; i++)
            {
                var origin = schema.Origins[i];
                origin.Variance = OriginVariance.Unused;
                origin.LoanRequirement = LoanRequirement.None;
                if (i < origins.Count && scope.Parent is { } enclosing && FindAbstractOrigin(origin.Name, enclosing) is not null)
                {
                    Fail(node, BindingFailure.Duplicate);
                }

                scope.Origins ??= new(StringComparer.Ordinal);
                if (!scope.Origins.TryAdd(origin.Name, origin.Origin))
                {
                    Fail(node, BindingFailure.Duplicate);
                }
            }
        }

        static BoundOrigin? FindAbstractOrigin(string name, BindingScope scope)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Origins?.TryGetValue(name, out var origin) == true)
                {
                    return origin;
                }
            }

            return null;
        }
    }

    private TypeBindingContext TypeContext(Koto syntax, BindingScope scope)
    {
        for (var current = syntax; current.Parent is { } parent; current = parent)
        {
            if (parent is PropertyAccessorKoto accessor)
            {
                if (ReferenceEquals(accessor.ReturnType, current))
                {
                    return new(TypePosition.Result, accessor);
                }

                return ReferenceEquals(accessor.ReceiverType, current) || ReferenceEquals(accessor.ValueType, current)
                    ? new(TypePosition.Parameter, accessor, ReferenceEquals(accessor.ReceiverType, current) ? 0 : 1, true)
                    : new(TypePosition.Explicit, accessor);
            }

            if (parent is FunctionKoto function)
            {
                if (ReferenceEquals(function.ReturnType, current))
                {
                    return new(TypePosition.Result, function);
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (ReferenceEquals(function.Parameters[i].Type, current))
                    {
                        return new(TypePosition.Parameter, function, i, true);
                    }
                }

                return new(TypePosition.Explicit, function);
            }

            if (parent is VariableKoto variable)
            {
                if (variable is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement } property && property.GetAccessor(PropertyAccessorKind.Get) is { } getter)
                {
                    return new(TypePosition.Result, getter);
                }

                return new(variable is PropertyKoto ? scope.Owner is GroupKoto ? TypePosition.StaticStorage : TypePosition.InstanceStorage : TypePosition.Local, variable);
            }
        }

        return new(TypePosition.Explicit, syntax);
    }

    private BoundOrigin? BindOriginName(string name, Koto use, BindingScope scope)
    {
        if (name == "static")
        {
            return BoundOrigin.Static;
        }

        if (this.OriginCandidate(name, use, scope, out var scalar, out var carrier))
        {
            if (scalar is not null)
            {
                return scalar;
            }

            if (carrier is { Origin: { } origin } && IsBorrow(carrier.Semantics))
            {
                return origin;
            }

            Fail(use, BindingFailure.InvalidOrigin);
            return null;
        }

        Fail(use, BindingFailure.MissingOrigin, true);
        return null;
    }

    private BoundOrigin? BindOrigin(Koto syntax, BindingScope scope)
    {
        BoundOrigin? result = null;
        if (syntax is IdentifierNameKoto name)
        {
            result = this.BindOriginName(name.IdentifierName, syntax, scope);
        }
        else if (syntax is ParenthesizedKoto parentheses)
        {
            result = this.BindOrigin(parentheses.Operand, scope);
        }
        else if (syntax is BinaryKoto binary && binary.Akind == KotoKind.And)
        {
            var left = this.BindOrigin(binary.Left, scope);
            var right = this.BindOrigin(binary.Right, scope);
            if (left is not null && right is not null)
            {
                result = this.Meet(left, right);
            }
        }
        else if (syntax is MemberAccessKoto member && member.Left is IdentifierNameKoto input && member.Right is IdentifierNameKoto target)
        {
            if (this.OriginCandidate(input.IdentifierName, syntax, scope, out _, out var type))
            {
                while (type is { Kind: BoundTypeKind.Semantics } && IsBorrow(type.Semantics))
                {
                    type = type.Components[0];
                }

                var schema = type?.Symbol?.Schema;
                for (var i = 0; schema is not null && i < schema.Origins.Count; i++)
                {
                    if (schema.Origins[i].Name == target.IdentifierName)
                    {
                        result = type!.Kind == BoundTypeKind.Slice ? type.Origin : i < type.OriginArguments.Count ? type.OriginArguments[i] : null;
                        break;
                    }
                }

                if (result is not null)
                {
                    input.BindingState = target.BindingState = BindingState.Resolved;
                    target.BoundOrigin = result;
                }
            }
        }

        if (result is null)
        {
            Fail(syntax, BindingFailure.InvalidOrigin);
        }
        else
        {
            result = this.OriginAtUse(result, syntax);
            syntax.BindingState = BindingState.Resolved;
            syntax.BoundOrigin = result;
            if (!OriginVisible(result, syntax))
            {
                Fail(syntax, BindingFailure.InvalidOrigin);
            }
        }

        return result;
    }

    private BoundOrigin? OmittedOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement = LoanRequirement.Ref, int aggregateSlot = -1, BindingSymbol? borrowCondition = null)
    {
        if (this.inheritedOriginTypes.TryGetValue(use, out var inherited))
        {
            var origin = aggregateSlot < 0 ? inherited.Origin : aggregateSlot < inherited.OriginArguments.Count ? inherited.OriginArguments[aggregateSlot] : null;
            if (origin is not null)
            {
                return origin;
            }
        }

        if (this.PendingOrigin(use, scope, context, requirement, aggregateSlot) is { } pending)
        {
            return pending;
        }

        if (aggregateSlot >= 0 && context.Position == TypePosition.Parameter &&
            context.Owner is FunctionKoto { IsAnonymous: false } or PropertyAccessorKoto)
        {
            var slots = context.Owner.BoundSymbol!.AggregateInputOrigins ??= new();
            foreach (var slot in slots)
            {
                if (ReferenceEquals(slot.Occurrence, use) && slot.TargetSlot == aggregateSlot)
                {
                    return slot;
                }
            }

            var origin = new BoundOrigin(OriginKind.Input, context.Owner, InputCount(context.Owner) + slots.Count)
            {
                InputIndex = context.Slot,
                Occurrence = use,
                TargetSlot = aggregateSlot,
            };
            slots.Add(origin);
            return origin;
        }

        if (context.Position == TypePosition.Parameter && context.Direct)
        {
            var origin = this.OriginAtom(context.Owner, OriginKind.Input, context.Slot);
            origin.BorrowCondition = null;
            return origin;
        }

        if (context.Position == TypePosition.Local && context.Owner is VariableKoto { InitializerKoto: not null })
        {
            var origin = this.OriginAtom(use, OriginKind.Inference, aggregateSlot);
            this.AddObligation(new(BindingObligationKind.OriginInference, use, BindingDeadline.BodyOrigins, Longer: origin));
            return origin;
        }

        if (context.Position == TypePosition.Result)
        {
            BoundOrigin? meet = null;
            var guaranteedBorrow = false;
            var inputCount = InputCount(context.Owner);
            for (var i = 0; i < inputCount; i++)
            {
                var type = BoundInputType(context.Owner, i);
                if (type?.Origin is not { } input || (!IsBorrow(type.Semantics) && type.Kind != BoundTypeKind.SemanticsApplication))
                {
                    continue;
                }

                meet = meet is null ? input : this.Meet(meet, input);
                guaranteedBorrow |= IsBorrow(type.Semantics) || (borrowCondition is not null && ReferenceEquals(type.Symbol, borrowCondition)) ||
                    (type.Symbol?.WholeType is { } whole && this.HasSemanticsRole(whole, SemanticsMask.ValueBorrow | SemanticsMask.ObjRef | SemanticsMask.ObjUniq, scope));
            }

            if (meet is not null)
            {
                if (!guaranteedBorrow && (requirement == LoanRequirement.Uniq || (aggregateSlot >= 0 && !OwnedWithoutConditionalBorrows())))
                {
                    Fail(use, BindingFailure.MissingOrigin);
                    return null;
                }

                return meet;
            }

            if (requirement != LoanRequirement.Uniq)
            {
                if (aggregateSlot >= 0)
                {
                    for (var i = 0; i < inputCount; i++)
                    {
                        if (BoundInputType(context.Owner, i) is { } input && this.ProveOwned(input, use) != ConstraintProof.Proven)
                        {
                            Fail(use, BindingFailure.MissingOrigin);
                            return null;
                        }
                    }
                }

                return BoundOrigin.Static;
            }
        }

        Fail(use, BindingFailure.MissingOrigin);
        return null;

        bool OwnedWithoutConditionalBorrows()
        {
            var count = InputCount(context.Owner);
            for (var i = 0; i < count; i++)
            {
                var input = BoundInputType(context.Owner, i);
                if (input?.Kind == BoundTypeKind.SemanticsApplication)
                {
                    input = input.Components[0];
                }

                if (input is not null && this.ProveOwned(input, use) != ConstraintProof.Proven)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private void AddObligation(BindingObligation obligation)
    {
        if (this.obligationSet.Add(obligation))
        {
            this.obligations.Add(obligation);
        }
    }
}
