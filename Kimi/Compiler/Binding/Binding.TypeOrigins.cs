// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundType WithOrigins(BoundType type, BoundOrigin? origin, ReadOnlySpan<BoundOrigin> arguments)
        => this.InternType(type.Kind, type.Symbol, type.Semantics, (BoundType[])type.Components, type.Length, origin, arguments, type.LengthExpression);

    private BoundType SelfType(BindingSymbol symbol)
    {
        var schema = symbol.Schema!;
        var types = ArrayPool<BoundType>.Shared.Rent(schema.GenericSlots.Count);
        var origins = ArrayPool<BoundOrigin>.Shared.Rent(schema.Origins.Count);
        try
        {
            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                types[i] = schema.GenericSlots[i].Symbol.WholeType!;
            }

            for (var i = 0; i < schema.Origins.Count; i++)
            {
                origins[i] = schema.Origins[i].Origin;
            }

            if (schema.GenericSlots.Count == 0 && schema.Origins.Count == 0)
            {
                return symbol.Type!;
            }

            return this.InternType(schema.GenericSlots.Count == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, types.AsSpan(0, schema.GenericSlots.Count), originArguments: origins.AsSpan(0, schema.Origins.Count));
        }
        finally
        {
            ArrayPool<BoundType>.Shared.Return(types, clearArray: true);
            ArrayPool<BoundOrigin>.Shared.Return(origins, clearArray: true);
        }
    }

    private BoundType? CompleteOrigins(BoundType type, TypeSemanticsKoto? annotation, Koto use, BindingScope scope, TypeBindingContext context)
    {
        var written = annotation is not null && (annotation.OriginName is not null || annotation.OriginExpression is not null || annotation.OriginArguments is not null);
        if (IsBorrow(type.Semantics) || (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.SemanticsApplication && written))
        {
            if (annotation?.OriginArguments is not null)
            {
                return Fail(use, BindingFailure.InvalidOrigin);
            }

            var origin = written ? annotation!.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(annotation.OriginName!, use, scope) : type.Origin;
            if (written && type.Origin is not null && !ReferenceEquals(type.Origin, origin))
            {
                return Fail(use, BindingFailure.InvalidOrigin);
            }

            if (written && type.Kind == BoundTypeKind.Parameter)
            {
                this.AddObligation(new(BindingObligationKind.TypeFormation, use, BindingDeadline.Definition, type));
            }

            if (origin is null && !written && !context.SuppressOuter)
            {
                origin = this.OmittedOrigin(use, scope, context, IsExclusive(type.Semantics) ? LoanRequirement.Uniq : LoanRequirement.Ref);
            }

            if (origin is null && (written || !context.SuppressOuter))
            {
                return null;
            }

            if (origin?.Kind == OriginKind.Static && IsExclusive(type.Semantics))
            {
                return Fail(use, BindingFailure.InvalidOrigin);
            }

            if (!ReferenceEquals(origin, type.Origin))
            {
                type = this.WithOrigins(type, origin, (BoundOrigin[])type.OriginArguments);
            }

            if (origin is not null && type.Components.Count != 0)
            {
                this.RetainInnerOutlives(type.Components[0], origin, use);
            }
        }
        else
        {
            var target = type;
            var objectLayer = type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc;
            if (objectLayer)
            {
                target = type.Components[0];
            }

            var schema = target.Symbol?.Schema;
            var count = schema?.Origins.Count ?? 0;
            if (written && count == 0)
            {
                return Fail(use, BindingFailure.InvalidOrigin);
            }

            if (count != 0 && (written || (!context.SuppressOuter && target.OriginArguments.Count != count)))
            {
                var arguments = ArrayPool<BoundOrigin>.Shared.Rent(count);
                Array.Clear(arguments, 0, count);
                try
                {
                    for (var i = 0; i < target.OriginArguments.Count; i++)
                    {
                        arguments[i] = target.OriginArguments[i];
                    }

                    if (annotation?.OriginArguments is { } named)
                    {
                        // A supplied mapping is checked against declaration slots, independently of source order.
                        var seen = ArrayPool<bool>.Shared.Rent(count);
                        Array.Clear(seen, 0, count);
                        try
                        {
                            for (var i = 0; i < named.Length; i++)
                            {
                                var slot = -1;
                                for (var j = 0; j < count; j++)
                                {
                                    if (schema!.Origins[j].Name == named[i].Name)
                                    {
                                        slot = j;
                                        break;
                                    }
                                }

                                if (slot < 0 || seen[slot])
                                {
                                    Fail(use, BindingFailure.InvalidOrigin);
                                    return null;
                                }

                                seen[slot] = true;
                                var bound = this.BindOrigin(named[i].Value, scope);
                                if (bound is null)
                                {
                                    return null;
                                }

                                arguments[slot] = bound;
                            }
                        }
                        finally
                        {
                            ArrayPool<bool>.Shared.Return(seen);
                        }
                    }
                    else if (written)
                    {
                        if (count != 1)
                        {
                            return Fail(use, BindingFailure.InvalidOrigin);
                        }

                        var bound = annotation!.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(annotation.OriginName!, use, scope);
                        if (bound is null)
                        {
                            return null;
                        }

                        arguments[0] = bound;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        if (arguments[i] is not null)
                        {
                            continue;
                        }

                        // Direct-input quantification applies only to a borrow layer, never aggregate slots.
                        var bound = this.OmittedOrigin(use, scope, context with { Direct = false, Slot = i }, schema!.Origins[i].LoanRequirement);
                        if (bound is null)
                        {
                            return null;
                        }

                        arguments[i] = bound;
                    }

                    target = this.WithOrigins(target, target.Origin, arguments.AsSpan(0, count));
                    type = objectLayer ? this.InternType(type.Kind, type.Symbol, type.Semantics, [target], type.Length, type.Origin) : target;
                }
                finally
                {
                    ArrayPool<BoundOrigin>.Shared.Return(arguments, clearArray: true);
                }
            }
        }

        // Transparent syntax must expose the same completed type as its normalized owner.
        for (var node = use; ;)
        {
            Koto? inner = node switch
            {
                ParenthesizedTypeKoto p => p.Type,
                TypeSemanticsKoto s when s.Type is not null && (s.IsTransparentWrapper || (s.SemanticsParameter is null && s.SemanticsKind == SemanticsKind.Owner)) => s.Type,
                _ => null,
            };
            if (inner is null)
            {
                break;
            }

            Complete(inner, type);
            node = inner;
        }

        return type;
    }

    private void RetainInnerOutlives(BoundType inner, BoundOrigin outer, Koto use)
    {
        if (inner.Origin is { } origin && !OriginOutlives(origin, outer))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, inner, origin, outer));
        }

        for (var i = 0; i < inner.OriginArguments.Count; i++)
        {
            var argument = inner.OriginArguments[i];
            if (!OriginOutlives(argument, outer))
            {
                this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, inner, argument, outer));
            }
        }

        for (var i = 0; i < inner.Components.Count; i++)
        {
            this.RetainInnerOutlives(inner.Components[i], outer, use);
        }
    }

    private void AccumulateRequirements(BoundType type, DeclarationSchema schema, int polarity, ref bool changed)
    {
        if (type.Kind == BoundTypeKind.Parameter)
        {
            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                var slot = schema.GenericSlots[i];
                if (!ReferenceEquals(slot.Symbol, type.Symbol))
                {
                    continue;
                }

                var variance = polarity == 0 ? OriginVariance.Invariant : polarity > 0 ? OriginVariance.Covariant : OriginVariance.Contravariant;
                var combined = slot.OriginVariance == OriginVariance.Unused ? variance : slot.OriginVariance == variance ? variance : OriginVariance.Invariant;
                if (combined != slot.OriginVariance)
                {
                    slot.OriginVariance = combined;
                    changed = true;
                }
            }
        }

        if (type.Origin is { } origin)
        {
            this.AccumulateOrigin(origin, schema, polarity, IsExclusive(type.Semantics) ? LoanRequirement.Uniq : LoanRequirement.Ref, ref changed);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var source = type.Symbol?.Schema?.Origins[i];
            if (source is null || source.Variance == OriginVariance.Unused)
            {
                continue;
            }

            var sign = source.Variance == OriginVariance.Invariant ? 0 : source.Variance == OriginVariance.Contravariant ? -polarity : polarity;
            this.AccumulateOrigin(type.OriginArguments[i], schema, sign, source.LoanRequirement, ref changed);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var sign = type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? 0 : type.Kind == BoundTypeKind.Function && i == 0 ? -polarity : polarity;
            if (type.Kind == BoundTypeKind.Constructed && type.Symbol?.Schema is { } target)
            {
                var variance = target.GenericSlots[i].OriginVariance;
                if (variance == OriginVariance.Unused)
                {
                    continue;
                }

                sign = variance == OriginVariance.Invariant ? 0 : variance == OriginVariance.Contravariant ? -polarity : polarity;
            }

            this.AccumulateRequirements(type.Components[i], schema, sign, ref changed);
        }
    }

    private void AccumulateOrigin(BoundOrigin origin, DeclarationSchema schema, int polarity, LoanRequirement loan, ref bool changed)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                this.AccumulateOrigin(origin.Operands[i], schema, polarity, loan, ref changed);
            }

            return;
        }

        for (var i = 0; i < schema.Origins.Count; i++)
        {
            var parameter = schema.Origins[i];
            if (!ReferenceEquals(parameter.Origin, origin))
            {
                continue;
            }

            var variance = polarity == 0 ? OriginVariance.Invariant : polarity > 0 ? OriginVariance.Covariant : OriginVariance.Contravariant;
            var combined = parameter.Variance == OriginVariance.Unused ? variance : parameter.Variance == variance ? variance : OriginVariance.Invariant;
            if (combined != parameter.Variance)
            {
                parameter.Variance = combined;
                changed = true;
            }

            if (loan > parameter.LoanRequirement)
            {
                parameter.LoanRequirement = loan;
                changed = true;
            }
        }
    }

    private void ValidateOriginRequirements()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            var node = this.nodes[n];
            if (node.BoundType is not { } type)
            {
                continue;
            }

            if (type.Symbol?.Schema is { } schema)
            {
                for (var i = 0; i < type.OriginArguments.Count; i++)
                {
                    if (type.OriginArguments[i].Kind == OriginKind.Static && schema.Origins[i].LoanRequirement == LoanRequirement.Uniq)
                    {
                        Fail(node, BindingFailure.InvalidOrigin);
                    }
                }
            }

            if (node is PropertyKoto property && this.symbols[property].Scope.Owner is GroupKoto)
            {
                this.borrowVisiting.Clear();
                if (this.RetainsBorrow(type, this.borrowVisiting))
                {
                    Fail(node, BindingFailure.InvalidTypeFormation);
                }
            }
        }
    }

    private bool RetainsBorrow(BoundType type, HashSet<BindingSymbol> visiting)
    {
        if (IsBorrow(type.Semantics))
        {
            return true;
        }

        if (type.Semantics == SemanticsKind.Unsafe || type.Kind == BoundTypeKind.Function)
        {
            return false;
        }

        if (type.Symbol?.Declaration is DeclarationContainerKoto container)
        {
            if (!visiting.Add(type.Symbol))
            {
                return false;
            }

            try
            {
                for (var i = 0; i < container.Members.Count; i++)
                {
                    if (container.Members[i] is not VariableKoto { BoundType: { } field })
                    {
                        continue;
                    }

                    var actual = type.Kind == BoundTypeKind.Constructed ? this.SubstituteType(field, container, (BoundType[])type.Components) : field;
                    if (actual is not null && this.RetainsBorrow(actual, visiting))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                visiting.Remove(type.Symbol);
            }

            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.RetainsBorrow(type.Components[i], visiting))
            {
                return true;
            }
        }

        return false;
    }
}
