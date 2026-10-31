// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Type relation's one walk (SPEC 3.8, 10.7, 15.6.1).
// - Responsibility: whether `actual` is a subtype of `expected`, part by part, and a Function Type by one Callable comparison
//   (FunctionFits).
// - Inputs: the query: the Binding and use whose premises and Origins the Origin part reads (none: the relation as written), and which
//   Origin bindings count (OriginPart).
// - Outputs: whether the relation holds.
// - Invariants: one order of the parts: the outer Origin, the Origin arguments, then the components; within a Callable comparison its
//   inputs, then its result.
// - Prohibitions: a mode that changes what the relation is goes into the query, never into a parameter of one entry.
public sealed partial class Binding
{
    // Which Origin bindings a relation walk compares: every one (Judged), none outside Function Types (Structural, SPEC 15.6.1), or
    // none at all (Everywhere, SPEC 10.4 step 2, since Origin bindings never rank candidates).
    private enum OriginPart : byte
    {
        Judged,
        Structural,
        Everywhere,
    }

    // One walk of the relation with its query (see the file contract).
    private struct RelationWalk
    {
        private readonly Binding? binding;
        private readonly Koto? use;
        private OriginPart part;

        internal RelationWalk(Binding? binding, Koto? use, OriginPart part)
        {
            this.binding = binding;
            this.use = use;
            this.part = part;
        }

        // SPEC 3.8: the relation; Never relates only to itself. `own` is the declaration whose own per-call inputs the root actual signature
        // names (SPEC 8.10); a nested Function Type's own syntax binds its inputs. `skipOrigin` leaves the outer Origin to an instantiation
        // that already fixed it; `instance` is the Callable comparison whose per-call Origins the parts are compared under.
        internal bool Fits(
            BoundType actual, BoundType expected, bool invariant = false, bool skipOrigin = false, CallableInstance instance = default, Koto? own = null)
        {
            if (ReferenceEquals(actual, expected))
            {
                return true;
            }

            if (actual.Kind == BoundTypeKind.Primitive || expected.Kind == BoundTypeKind.Primitive)
            {
                return false;
            }

            if (!SameShape(actual, expected))
            {
                return false;
            }

            var structural = this.part != OriginPart.Judged;
            var (outer, longer, shorter) = (OuterOriginFit.None, BoundOrigin.Static, BoundOrigin.Static);
            if (!skipOrigin)
            {
                outer = FitOuterOrigins(actual, expected, this.binding, this.use, out longer, out shorter);
            }

            var condition = outer == OuterOriginFit.Relation && !structural ? SlotCondition(actual, this.binding, this.use) : 0;
            if (outer == OuterOriginFit.Mismatch || (outer == OuterOriginFit.Relation && !structural &&
                (!this.OriginFits(longer, shorter, instance, condition) || (invariant && !this.OriginFits(shorter, longer, instance, condition)))))
            {
                return false;
            }

            for (var i = 0; i < actual.OriginArguments.Count; i++)
            {
                var a = actual.OriginArguments[i];
                var b = expected.OriginArguments[i];
                if (ReferenceEquals(a, b) || structural)
                {
                    continue;
                }

                var variance = actual.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant;
                if (invariant || variance is OriginVariance.Invariant or OriginVariance.Unused ?
                    !this.OriginFits(a, b, instance) || !this.OriginFits(b, a, instance) :
                    variance == OriginVariance.Covariant ? !this.OriginFits(a, b, instance) : !this.OriginFits(b, a, instance))
                {
                    return false;
                }
            }

            if (actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2)
            {
                return this.FunctionFits(actual, expected, invariant, Comparison(actual, own, expected, instance.Enclosing));
            }

            for (var i = 0; i < actual.Components.Count; i++)
            {
                var a = actual.Components[i];
                var b = expected.Components[i];
                if (actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema)
                {
                    var variance = schema.GenericSlots[i].OriginVariance;
                    var both = invariant || variance is OriginVariance.Invariant or OriginVariance.Unused;
                    if (both || variance == OriginVariance.Covariant ? !this.Fits(a, b, both, instance: instance) : !this.Fits(b, a, instance: instance))
                    {
                        return false;
                    }

                    continue;
                }

                if (!this.Fits(a, b, invariant || IsInvariantLayer(actual, this.binding), instance: instance))
                {
                    return false;
                }
            }

            return true;
        }

        // The parts of the relation no Origin binding decides: Kind, Semantics, Symbol, Closure context, lengths and arity; an inferred
        // length (`[_ of T]`) admits any.
        private static bool SameShape(BoundType actual, BoundType expected)
        {
            var inferredLength = expected.Kind == BoundTypeKind.FixedArray && expected.Length < 0;
            return actual.Kind == expected.Kind && actual.Semantics == expected.Semantics && ReferenceEquals(actual.Symbol, expected.Symbol) &&
                ReferenceEquals(actual.ClosureContext, expected.ClosureContext) && actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments) &&
                (inferredLength || (actual.Length == expected.Length && ReferenceEquals(actual.LengthExpression, expected.LengthExpression))) &&
                actual.Components.Count == expected.Components.Count && actual.OriginArguments.Count == expected.OriginArguments.Count;
        }

        private readonly bool OriginFits(BoundOrigin a, BoundOrigin b, in CallableInstance instance, ulong condition = 0)
            => instance.Actual is not null ? InstanceOutlives(a, b, instance, this.binding, this.use, 0) :
                this.binding is null ? OriginOutlives(a, b) : this.binding.FitOrigin(a, b, this.use!, instance, condition);

        // SPEC 10.7, 15.3.7: an implementation fits a required signature when its inputs accept the required inputs and its result fits
        // the required result. Each input the implementation binds per call is instantiated at the required input in the same position,
        // per call or fixed, so only its referent is compared; every other input is compared as written, contravariantly. Inside a Function
        // Type only `Everywhere` treats Origin bindings as equal.
        private bool FunctionFits(BoundType actual, BoundType expected, bool invariant, in CallableInstance instance)
        {
            var actualInputs = actual.Components[0];
            var expectedInputs = expected.Components[0];
            if (!ReferenceEquals(actualInputs, expectedInputs) &&
                (actualInputs.Kind != expectedInputs.Kind || actualInputs.Components.Count != expectedInputs.Components.Count))
            {
                return false;
            }

            var outer = this.part;
            this.part = outer == OriginPart.Everywhere ? OriginPart.Everywhere : OriginPart.Judged;
            var fits = this.MembersFit(actual, expected, invariant, instance);
            this.part = outer;
            return fits;
        }

        private bool MembersFit(BoundType actual, BoundType expected, bool invariant, in CallableInstance instance)
        {
            var actualInputs = actual.Components[0];
            var expectedInputs = expected.Components[0];
            for (var i = 0; !ReferenceEquals(actualInputs, expectedInputs) && i < actualInputs.Components.Count; i++)
            {
                var input = actualInputs.Components[i];
                if (!this.Fits(expectedInputs.Components[i], input, invariant, skipOrigin: instance.IsQuantifiedInput(input, i), instance: instance))
                {
                    return false;
                }
            }

            return this.Fits(actual.Components[1], expected.Components[1], invariant, instance: instance);
        }
    }
}
