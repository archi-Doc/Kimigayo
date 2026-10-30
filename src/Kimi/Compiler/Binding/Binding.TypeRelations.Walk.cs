// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Type relation's one walk (SPEC 3.8, 10.7, 15.6.1; DECISIONS R4-M155).
// - Responsibility: whether `actual` is a subtype of `expected`, part by part, and a Function Type by one Callable comparison
//   (FunctionFits), whose implementation conditions it judges at every nesting.
// - Inputs: the query: the Binding and use whose premises and Origins the Origin part reads (none: the relation as written), and which
//   Origin bindings count (OriginPart).
// - Outputs: whether the relation holds and, when it does not, the first failure where the walk found it (ComparisonFailure): the
//   reader that records a failed Callable comparison takes it from the walk that decided (the one judgment, DECISIONS R4-M155).
// - Invariants: one order of the parts: the outer Origin, the Origin arguments, then the components; within a Callable comparison its
//   inputs, its result, then the implementation's own well-formedness. The walk stops at its first failure, which is the one record
//   of a whole-contract comparison (SPEC 15.6.1); a failure of another value or position is another walk's.
// - Prohibitions: no reader walks a relation again to explain it; a mode that changes what the relation is goes into the query.
public sealed partial class Binding
{
    // What failed where a whole-contract comparison does not hold (SPEC 10.7, 15.6.1).
    internal enum ComparisonFailureKind : byte
    {
        None,

        // A part's shape: Kind, Semantics, Symbol, arity or an Origin's presence: a Type mismatch.
        Structural,

        // An Origin position of a member, under the comparison's instantiation.
        Origin,

        // The implementation's own well-formedness, its Part-th input's or (Part -1) its result's (the owner's (a)).
        WellFormedness,

        // A Function Item's declared clause, proven for the comparison's solution.
        Clause,

        // A named function's result premise, proven for its substituted result.
        ResultPremise,
    }

    // SPEC 10.7, 15.6.1, 23.1 (DECISIONS R4-M155): the first failure of one walk, where the walk found it: the failing condition (Kind,
    // Part, Clause), its ends as written with the comparison that instantiates them, and the members of the enclosing Callable comparisons
    // from the root (Path, one byte per member, the outermost highest: a parameter n + 1, the result ResultStep).
    internal readonly record struct ComparisonFailure(ComparisonFailureKind Kind, BoundOrigin? Longer, BoundOrigin? Shorter, bool Equality, CallableInstance Instance)
    {
        internal const byte ResultStep = 0xFF;

        internal int Part { get; init; }

        internal OriginRelationKoto? Clause { get; init; }

        internal ulong Path { get; init; }

        internal byte Depth { get; init; }

        // Whether it is a condition of a Callable comparison, recorded as UnprovenOriginContract_Kd: not the shape, and an Origin position
        // only inside a comparison's members (one of the Function handle itself is a relation of its own position).
        internal bool IsCondition => this.Kind is not (ComparisonFailureKind.None or ComparisonFailureKind.Structural) &&
            (this.Kind != ComparisonFailureKind.Origin || this.Depth > 0);

        // The failure as the enclosing comparison sees it: within its member `step`.
        internal ComparisonFailure Within(byte step) => this.Kind == ComparisonFailureKind.None || this.Depth == 8 ? this :
            this with { Path = this.Path | ((ulong)step << (8 * this.Depth)), Depth = (byte)(this.Depth + 1) };
    }

    // Which Origin bindings a relation walk compares and how: every one at the use (Judged), every one as written while the
    // implementation's conditions are judged at the use (Written: a Closure's conversion and a Callable proof, DECISIONS R4-M155), none
    // outside Function Types (Structural, SPEC 15.6.1), or none at all (Everywhere, SPEC 10.4 step 2, since Origin bindings never rank
    // candidates).
    private enum OriginPart : byte
    {
        Judged,
        Written,
        Structural,
        Everywhere,
    }

    // One walk of the relation with its query and its result (see the file contract).
    private struct RelationWalk
    {
        private readonly Binding? binding;
        private readonly Koto? use;
        private OriginPart part;
        private ComparisonFailure failure;

        internal RelationWalk(Binding? binding, Koto? use, OriginPart part)
        {
            this.binding = binding;
            this.use = use;
            this.part = part;
        }

        // The first failure of the walk's last false answer.
        internal readonly ComparisonFailure Failure => this.failure;

        // The Binding whose premises judge the Origin positions: none when they are compared as written.
        private readonly Binding? Judge => this.part == OriginPart.Written ? null : this.binding;

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

            if (actual.Kind == BoundTypeKind.Primitive || expected.Kind == BoundTypeKind.Primitive || !SameShape(actual, expected))
            {
                return this.Fail(new(ComparisonFailureKind.Structural, null, null, false, instance));
            }

            var structural = this.part is OriginPart.Structural or OriginPart.Everywhere;
            var judge = this.Judge;
            var (outer, longer, shorter) = (OuterOriginFit.None, BoundOrigin.Static, BoundOrigin.Static);
            if (!skipOrigin)
            {
                outer = FitOuterOrigins(actual, expected, judge, this.use, out longer, out shorter);
            }

            var condition = outer == OuterOriginFit.Relation && !structural ? SlotCondition(actual, judge, this.use) : 0;
            if (outer == OuterOriginFit.Mismatch)
            {
                return this.Fail(new(ComparisonFailureKind.Structural, null, null, false, instance));
            }

            if (outer == OuterOriginFit.Relation && !structural &&
                (!this.OriginFits(longer, shorter, instance, condition) || (invariant && !this.OriginFits(shorter, longer, instance, condition))))
            {
                return this.Fail(new(ComparisonFailureKind.Origin, longer, shorter, invariant, instance));
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
                var both = invariant || variance is OriginVariance.Invariant or OriginVariance.Unused;
                if (((both || variance == OriginVariance.Covariant) && !this.OriginFits(a, b, instance)) ||
                    ((both || variance == OriginVariance.Contravariant) && !this.OriginFits(b, a, instance)))
                {
                    return this.Fail(new(ComparisonFailureKind.Origin, a, b, both, instance));
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

                if (!this.Fits(a, b, invariant || IsInvariantLayer(actual, judge), instance: instance))
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

        private bool Fail(in ComparisonFailure failure)
        {
            this.failure = failure;
            return false;
        }

        private readonly bool OriginFits(BoundOrigin a, BoundOrigin b, in CallableInstance instance, ulong condition = 0)
            => instance.Actual is not null ? InstanceOutlives(a, b, instance, this.Judge, this.use, 0) :
                this.Judge is not { } judge ? OriginOutlives(a, b) : judge.FitOrigin(a, b, this.use!, instance, condition);

        // SPEC 10.7, 15.3.7: an implementation fits a required signature when its inputs accept the required inputs and its result fits
        // the required result. Each input the implementation binds per call is instantiated at the required input in the same position,
        // per call or fixed, so only its referent is compared; every other input is compared as written, contravariantly. Inside a Function
        // Type only `Everywhere` treats Origin bindings as equal. Under the owner's (a) and (A), the implementation's own well-formedness,
        // its inputs' and result's, is one of its conditions, proven from the required signatures' own well-formedness (the instance's
        // premises) and the premises at the use (FailedSignatureWellFormedness).
        private bool FunctionFits(BoundType actual, BoundType expected, bool invariant, in CallableInstance instance)
        {
            var actualInputs = actual.Components[0];
            var expectedInputs = expected.Components[0];
            if (!ReferenceEquals(actualInputs, expectedInputs) &&
                (actualInputs.Kind != expectedInputs.Kind || actualInputs.Components.Count != expectedInputs.Components.Count))
            {
                return this.Fail(new(ComparisonFailureKind.Structural, null, null, false, instance));
            }

            var outer = this.part;
            var everywhere = outer == OriginPart.Everywhere;
            this.part = outer is OriginPart.Everywhere or OriginPart.Written ? outer : OriginPart.Judged;
            var fits = this.MembersFit(actual, expected, invariant, instance);
            this.part = outer;
            if (!fits || this.binding is null || everywhere || this.binding.FailedSignatureWellFormedness(actual, instance, this.use!) is not { } formed)
            {
                return fits;
            }

            var input = formed.Part < actualInputs.Components.Count ? formed.Part : -1;
            return this.Fail(new ComparisonFailure(ComparisonFailureKind.WellFormedness, formed.Longer, formed.Shorter, false, instance) { Part = input });
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
                    return this.Fail(this.failure.Within((byte)(i + 1)));
                }
            }

            return this.Fits(actual.Components[1], expected.Components[1], invariant, instance: instance) ||
                this.Fail(this.failure.Within(ComparisonFailure.ResultStep));
        }
    }
}
