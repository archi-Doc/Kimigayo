// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool IsBorrow(SemanticsKind semantics) => semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    private static bool IsExclusive(SemanticsKind semantics) => semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    private static int InputCount(Koto owner) => owner is FunctionKoto f ? f.Parameters.Count : owner is PropertyAccessorKoto ? 2 : 0;

    private static Koto? InputType(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].Type : owner is PropertyAccessorKoto a ? index == 0 ? a.ReceiverType : a.ValueType : null;

    private static string InputName(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].InternalName : index == 0 ? "self" : "value";

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
                parameters = function.GenericArguments;
                origins = function.Origins;
            }
            else if (node is DeclarationContainerKoto { IsRoot: false } container)
            {
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
            var schema = symbol.Schema;
            if (schema is null || schema.GenericSlots.Count != parameters.Count || schema.Origins.Count != origins.Count)
            {
                var slots = parameters.Count == 0 ? Array.Empty<GenericSlot>() : new GenericSlot[parameters.Count];
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = this.symbols[parameters[i]];
                    slots[i] = new(parameters[i] is LengthParameterKoto ? GenericSlotKind.Length : parameter.Pair is not null ? GenericSlotKind.Pair : GenericSlotKind.Type, parameter, parameter.Pair);
                }

                var bindings = origins.Count == 0 ? Array.Empty<OriginParameter>() : new OriginParameter[origins.Count];
                for (var i = 0; i < origins.Count; i++)
                {
                    bindings[i] = new(origins[i], i, this.OriginAtom(node, OriginKind.Parameter, i, origins[i]), origins is OriginNameList locations && i < locations.Spans.Count ? locations.Spans[i] : node.Span);
                }

                symbol.Schema = schema = new(slots, bindings);
            }

            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                schema.GenericSlots[i].Symbol.Slot = i;
                schema.GenericSlots[i].OriginVariance = OriginVariance.Unused;
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
                scope.Origins ??= new(StringComparer.Ordinal);
                if (!scope.Origins.TryAdd(origin.Name, origin.Origin))
                {
                    Fail(node, BindingFailure.Duplicate);
                }
            }
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

                return new(TypePosition.Parameter, accessor, ReferenceEquals(accessor.ReceiverType, current) ? 0 : 1, true);
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

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Origins?.TryGetValue(name, out var origin) == true)
            {
                return origin;
            }

            if (InputCount(current.Owner) != 0)
            {
                for (var i = 0; i < InputCount(current.Owner); i++)
                {
                    var inputSyntax = InputType(current.Owner, i);
                    if (inputSyntax is null || InputName(current.Owner, i) != name)
                    {
                        continue;
                    }

                    var type = inputSyntax.BoundType;
                    if (type is null && !ReferenceEquals(inputSyntax, use))
                    {
                        type = this.BindType(inputSyntax, current);
                    }

                    if (type?.Origin is { } input && IsBorrow(type.Semantics))
                    {
                        return input;
                    }

                    Fail(use, BindingFailure.InvalidOrigin);
                    return null;
                }
            }
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
            // Only a declared input carrier introduces this Origin path; there is no Value lookup fallback.
            for (var current = scope; current is not null && result is null; current = current.Parent)
            {
                if (InputCount(current.Owner) == 0)
                {
                    continue;
                }

                for (var i = 0; i < InputCount(current.Owner); i++)
                {
                    var inputSyntax = InputType(current.Owner, i);
                    if (inputSyntax is null || InputName(current.Owner, i) != input.IdentifierName)
                    {
                        continue;
                    }

                    var type = inputSyntax.BoundType ?? this.BindType(inputSyntax, current);
                    if (type is { Kind: BoundTypeKind.Semantics })
                    {
                        type = type.Components[0];
                    }

                    var schema = type?.Symbol?.Schema;
                    if (schema is null)
                    {
                        break;
                    }

                    for (var j = 0; j < schema.Origins.Count; j++)
                    {
                        if (schema.Origins[j].Name == target.IdentifierName && j < type!.OriginArguments.Count)
                        {
                            result = type.OriginArguments[j];
                        }
                    }

                    if (result is not null)
                    {
                        if (current.Owner is FunctionKoto function)
                        {
                            input.BoundSymbol = this.symbols[function.Parameters[i]];
                        }

                        target.BoundOrigin = result;
                        input.BindingState = target.BindingState = BindingState.Resolved;
                    }

                    break;
                }
            }
        }

        if (result is null)
        {
            Fail(syntax, BindingFailure.InvalidOrigin);
        }
        else
        {
            syntax.BindingState = BindingState.Resolved;
            syntax.BoundOrigin = result;
        }

        return result;
    }

    private BoundOrigin? OmittedOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement = LoanRequirement.Ref)
    {
        if (context.Position == TypePosition.Parameter && context.Direct)
        {
            return this.OriginAtom(context.Owner, OriginKind.Input, context.Slot);
        }

        if (context.Position == TypePosition.Local && context.Owner is VariableKoto { InitializerKoto: not null })
        {
            var origin = this.OriginAtom(use, OriginKind.Inference, context.Slot);
            this.AddObligation(new(BindingObligationKind.OriginInference, use, BindingDeadline.BodyOrigins, Longer: origin));
            return origin;
        }

        if (context.Position == TypePosition.Result)
        {
            BoundOrigin? meet = null;
            for (var i = 0; i < InputCount(context.Owner); i++)
            {
                var type = InputType(context.Owner, i)?.BoundType;
                if (type?.Origin is not { } input || !IsBorrow(type.Semantics))
                {
                    continue;
                }

                meet = meet is null ? input : this.Meet(meet, input);
            }

            if (meet is not null)
            {
                return meet;
            }

            if (requirement != LoanRequirement.Uniq)
            {
                return BoundOrigin.Static;
            }
        }

        Fail(use, BindingFailure.MissingOrigin);
        return null;
    }

    private void AddObligation(BindingObligation obligation)
    {
        if (this.obligationSet.Add(obligation))
        {
            this.obligations.Add(obligation);
        }
    }
}
