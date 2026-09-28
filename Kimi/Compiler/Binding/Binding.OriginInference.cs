// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Koto Use, Koto Binder), OriginInference> originInferences = new();

    private OriginInference BeginOriginInference(Koto use, Koto binder)
    {
        if (!this.originInferences.TryGetValue((use, binder), out var inference))
        {
            this.originInferences.Add((use, binder), inference = new(binder));
        }

        inference.Bounds.Clear();
        inference.Variables.Clear();
        return inference;
    }

    private void CollectOriginInference(BoundType pattern, BoundType actual, OriginInference inference, int polarity = 1, bool result = false)
    {
        if (!pattern.CarriesOrigin || !actual.CarriesOrigin)
        {
            return;
        }

        if (pattern.Origin is { } p && actual.Origin is { } a)
        {
            Add(p, a, polarity);
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            var variance = pattern.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant;
            Add(pattern.OriginArguments[i], actual.OriginArguments[i], Compose(polarity, variance));
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, actual.Components.Count); i++)
        {
            var sign = pattern.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Unsafe ? 0 :
                pattern.Kind == BoundTypeKind.Function && i == 0 ? -polarity : polarity;
            if (pattern.Kind == BoundTypeKind.Constructed && pattern.Symbol?.Schema is { } schema)
            {
                sign = Compose(polarity, schema.GenericSlots[i].OriginVariance);
            }

            this.CollectOriginInference(pattern.Components[i], actual.Components[i], inference, sign, result);
        }

        static int Compose(int sign, OriginVariance variance) => variance == OriginVariance.Covariant ? sign : variance == OriginVariance.Contravariant ? -sign : 0;

        void Add(BoundOrigin parameter, BoundOrigin value, int sign)
        {
            inference.Discover(parameter, sign == 0 ? 3 : sign > 0 ? 1 : 2);
            if ((sign >= 0) != result || sign == 0)
            {
                inference.Add(value, parameter, false, true);
            }

            if ((sign <= 0) != result || sign == 0)
            {
                inference.Add(parameter, value, true, false);
            }
        }
    }

    private bool SolveOriginInference(OriginInference inference, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use, BoundType? declaringType = null)
    {
        var binder = inference.Binder;
        if (this.originDeclarations.TryGetValue(binder, out var declaration))
        {
            foreach (var relation in declaration.Relations)
            {
                var longer = Project(relation.Longer);
                var shorter = Project(relation.Shorter);
                inference.Discover(longer, 0);
                inference.Discover(shorter, 0);
                inference.Add(longer, shorter);
                if (relation.Equality)
                {
                    inference.Add(shorter, longer);
                }
            }
        }

        // Preliminary Type inference may have proposed values. Recompute every mentioned
        // Origin from its bounds, keeping contextual inputs that have no Origin equation.
        foreach (var variable in inference.Variables)
        {
            Set(variable.Origin, null);
        }

        var count = inference.Variables.Count;
        var converged = false;
        for (var pass = 0; pass <= count * count; pass++)
        {
            var changed = false;
            foreach (var variable in inference.Variables)
            {
                BoundOrigin? upper = null;
                BoundOrigin? lower = null;
                var incomparable = false;
                foreach (var bound in inference.Bounds)
                {
                    if (bound.ShorterVariable && ReferenceEquals(bound.Shorter, variable.Origin) && Resolve(bound.Longer, bound.LongerVariable) is { } limit)
                    {
                        upper = upper is null ? limit : this.Meet(upper, limit);
                    }

                    if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable.Origin) && Resolve(bound.Shorter, bound.ShorterVariable) is { } floor)
                    {
                        if (lower is null || this.ProvesOriginOutlives(floor, lower, use))
                        {
                            lower = floor;
                        }
                        else if (!this.ProvesOriginOutlives(lower, floor, use))
                        {
                            incomparable = true;
                        }
                    }
                }

                var candidate = variable.Polarity == 2 ? (incomparable ? null : lower) : upper ?? (incomparable ? null : lower);
                if (variable.Polarity == 3 && (incomparable || upper is null || lower is null ||
                    !this.ProvesOriginOutlives(upper, lower, use) || !this.ProvesOriginOutlives(lower, upper, use)))
                {
                    candidate = null;
                }

                if (!ReferenceEquals(Get(variable.Origin), candidate))
                {
                    Set(variable.Origin, candidate);
                    changed = true;
                }
            }

            if (!changed)
            {
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            return false;
        }

        foreach (var bound in inference.Bounds)
        {
            var longer = Resolve(bound.Longer, bound.LongerVariable);
            var shorter = Resolve(bound.Shorter, bound.ShorterVariable);
            if (longer is null || shorter is null || !this.ProvesOriginOutlives(longer, shorter, use))
            {
                return false;
            }
        }

        return true;

        BoundOrigin Project(BoundOrigin origin) => declaringType?.Symbol is { } owner ?
            this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments) : origin;

        BoundOrigin? Get(BoundOrigin origin)
        {
            var values = origin.Kind == OriginKind.Parameter ? origins : inputs;
            return (uint)origin.Slot < (uint)values.Length ? values[origin.Slot] : null;
        }

        void Set(BoundOrigin origin, BoundOrigin? value)
        {
            var values = origin.Kind == OriginKind.Parameter ? origins : inputs;
            if ((uint)origin.Slot < (uint)values.Length)
            {
                values[origin.Slot] = value!;
            }
        }

        BoundOrigin? Resolve(BoundOrigin expression, bool substitute)
        {
            if (!substitute)
            {
                return expression;
            }

            if (ReferenceEquals(expression.Binder, binder) && expression.Kind is OriginKind.Parameter or OriginKind.Input)
            {
                return Get(expression);
            }

            if (expression.Kind != OriginKind.Intersection)
            {
                return expression;
            }

            var meet = BoundOrigin.Static;
            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Resolve(expression.Operands[i], true) is not { } operand)
                {
                    return null;
                }

                meet = this.Meet(meet, operand);
            }

            return meet;
        }
    }

    private sealed class OriginInference(Koto binder)
    {
        internal Koto Binder { get; } = binder;

        internal List<(BoundOrigin Longer, BoundOrigin Shorter, bool LongerVariable, bool ShorterVariable)> Bounds { get; } = new();

        internal List<(BoundOrigin Origin, int Polarity)> Variables { get; } = new();

        internal void Add(BoundOrigin longer, BoundOrigin shorter, bool longerVariable = true, bool shorterVariable = true)
        {
            var bound = (longer, shorter, longerVariable, shorterVariable);
            if ((!ReferenceEquals(longer, shorter) || longerVariable != shorterVariable) && !this.Bounds.Contains(bound))
            {
                this.Bounds.Add(bound);
            }
        }

        internal void Discover(BoundOrigin expression, int polarity)
        {
            if (ReferenceEquals(expression.Binder, this.Binder) && expression.Kind is OriginKind.Parameter or OriginKind.Input)
            {
                for (var i = 0; i < this.Variables.Count; i++)
                {
                    if (ReferenceEquals(this.Variables[i].Origin, expression))
                    {
                        this.Variables[i] = (expression, this.Variables[i].Polarity | polarity);
                        return;
                    }
                }

                this.Variables.Add((expression, polarity));
            }

            for (var i = 0; i < expression.Operands.Count; i++)
            {
                this.Discover(expression.Operands[i], polarity);
            }
        }
    }
}
