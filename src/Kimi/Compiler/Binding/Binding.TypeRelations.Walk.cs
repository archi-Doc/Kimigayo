// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Type relation's one walk (SPEC 3.8, 10.7, 15.6.1).
// - Responsibility: whether `actual` is a subtype of `expected`, part by part, and a Function Type by one Callable comparison
//   (FunctionFits), whose implementation conditions it judges at every nesting when it has a Binding.
// - Inputs: the query: the Binding and use whose premises and Origins the Origin part reads (none: the relation as written), and which
//   Origin bindings count (OriginPart).
// - Outputs: whether the relation holds and, when it does not, its first failure where the walk found it (ComparisonFailure), which the
//   reader that records the failure takes from the walk that decided; a recording root's walk also keeps one fact per failing position
//   (comparisonFacts, design-r2 Amendment 1 A2).
// - Invariants: one order of the parts: the outer Origin, the Origin arguments, then the components; within a Callable comparison its
//   inputs, its result, then the implementation's own well-formedness. A Callable comparison stops at its first failure, which is its
//   one fact (SPEC 15.6.1); outside every comparison a recording root's walk goes on past an Origin position's or a Function
//   component's failure, keeping each in position order; a structural failure ends it. The first fact is the walk's failure.
// - Prohibitions: no reader walks a relation again to explain it; a mode that changes what the relation is goes into the query, never
//   into a parameter of one entry.
public sealed partial class Binding
{
    // The facts of the last recording root's walk (FitsTypeAt, CheckTypeUse's walks), one per failing position, which the root clears
    // when it starts (warm 0 B); RecordMismatch reads them when their first is the failure it records.
    private readonly List<ComparisonFailure> comparisonFacts = new();

    // What failed where a relation does not hold (SPEC 10.7, 15.6.1); the closed rows of ComparisonFailure.Kind.
    internal enum ComparisonFailureKind : byte
    {
        // The relation holds.
        None,

        // The structural part (SPEC 15.6.1: the only Type mismatch).
        Shape,

        // A plain Origin position, outside every Callable comparison.
        Position,

        // A member's Origin position inside a Callable comparison.
        Origin,

        // The implementation's own well-formedness, its Part-th input's or (Part -1) its result's (the owner's (a)).
        WellFormedness,

        // An Item's declared clause, proven for the comparison's solution (Clause names it).
        Clause,

        // A named function's result premise, proven for its substituted result.
        ResultPremise,

        // A member's Origin position whose relation only an inferred or body-local region decides (the judge proves it through the
        // region's sources, or finds it Unrepresentable; SPEC 15.6.5): the located limit until region-inference edges exist, recorded as
        // the plain position's search records it (design-r2 Amendment 1, A1).
        Limit,
    }

    // SPEC 10.7, 15.6.1, 23.1: the first failure of one walk, where the walk found it: the failing condition (Kind, Part), its ends as
    // written with the comparison that instantiates them, and the members from the nearest Callable comparison (Path, one byte per member,
    // the outermost highest: a parameter n + 1, the result ResultStep; Depth saturates at 8).
    internal readonly record struct ComparisonFailure(ComparisonFailureKind Kind, BoundOrigin? Longer, BoundOrigin? Shorter, bool Equality, CallableInstance Instance)
    {
        internal const byte ResultStep = 0xFF;

        // The failure of a site whose expected Type is a description or whose Types differ in their shape.
        internal static readonly ComparisonFailure Shape = new(ComparisonFailureKind.Shape, null, null, false, default);

        internal int Part { get; init; }

        internal ulong Path { get; init; }

        internal byte Depth { get; init; }

        // For Clause, the clause's syntax.
        internal Koto? Clause { get; init; }

        // Whether it fails a condition of a whole-contract comparison (SPEC 15.6.1): Proof class, one UnprovenOriginContract_Kd at the value.
        internal bool IsCondition => this.Kind is ComparisonFailureKind.Origin or ComparisonFailureKind.WellFormedness or ComparisonFailureKind.Clause or
            ComparisonFailureKind.ResultPremise;

        // Whether `other` is this failure: the same condition at the same position with the same ends (without the record's boxing Equals).
        internal bool Same(in ComparisonFailure other) => this.Kind == other.Kind && ReferenceEquals(this.Longer, other.Longer) &&
            ReferenceEquals(this.Shorter, other.Shorter) && this.Path == other.Path && this.Depth == other.Depth;

        // The failure as the enclosing comparison sees it: within its member `step`.
        internal ComparisonFailure Within(byte step) => this.Depth == 8 ? this :
            this with { Path = this.Path | ((ulong)step << (8 * this.Depth)), Depth = (byte)(this.Depth + 1) };
    }

    // Which Origin bindings a relation walk compares: every one (Judged), none outside Function Types (Structural, SPEC 15.6.1), or
    // none at all (Everywhere, SPEC 10.4 step 2, since Origin bindings never rank candidates).
    private enum OriginPart : byte
    {
        Judged,
        Structural,
        Everywhere,
    }

    // One walk of the relation with its query and its result (see the file contract).
    private struct RelationWalk
    {
        private readonly Binding? binding;
        private readonly Koto? use;
        private readonly List<ComparisonFailure>? facts;
        private readonly int start;
        private OriginPart part;
        private ComparisonFailure failure;
        private bool ended;

        internal RelationWalk(Binding? binding, Koto? use, OriginPart part)
        {
            this.binding = binding;
            this.use = use;
            this.part = part;
        }

        // A recording root's walk: its facts follow `facts`' current end, which the root truncates back once it has read them.
        internal RelationWalk(Binding binding, Koto use, List<ComparisonFailure> facts)
            : this(binding, use, OriginPart.Judged)
        {
            this.facts = facts;
            this.start = facts.Count;
        }

        // The first failure of the walk's last false answer.
        internal readonly ComparisonFailure Failure => this.facts is { } kept && kept.Count > this.start ? kept[this.start] : this.failure;

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
                return this.End(instance);
            }

            var structural = this.part != OriginPart.Judged;
            var (outer, longer, shorter) = (OuterOriginFit.None, BoundOrigin.Static, BoundOrigin.Static);
            if (!skipOrigin)
            {
                outer = FitOuterOrigins(actual, expected, this.binding, this.use, out longer, out shorter);
            }

            var condition = outer == OuterOriginFit.Relation && !structural ? SlotCondition(actual, this.binding, this.use) : 0;
            if (outer == OuterOriginFit.Mismatch)
            {
                return this.End(instance);
            }

            var holds = true;
            if (outer == OuterOriginFit.Relation && !structural &&
                (!this.OriginFits(longer, shorter, instance, condition) || (invariant && !this.OriginFits(shorter, longer, instance, condition))))
            {
                holds = false;
                if (!this.Keep(this.OriginFailure(longer, shorter, invariant, instance), instance))
                {
                    return false;
                }
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
                if (both ? !this.OriginFits(a, b, instance) || !this.OriginFits(b, a, instance) :
                    variance == OriginVariance.Covariant ? !this.OriginFits(a, b, instance) : !this.OriginFits(b, a, instance))
                {
                    holds = false;
                    var failed = variance == OriginVariance.Contravariant && !both ? this.OriginFailure(b, a, false, instance) : this.OriginFailure(a, b, both, instance);
                    if (!this.Keep(failed, instance))
                    {
                        return false;
                    }
                }
            }

            if (actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2)
            {
                // A Function component is one comparison: its first failure is one fact of the walk that reached it.
                if (this.FunctionFits(actual, expected, invariant, Comparison(actual, own, expected, instance.Enclosing)))
                {
                    return holds;
                }

                _ = !this.ended && this.Keep(this.failure, instance);
                return false;
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
                        holds = false;
                        if (!this.Goes(instance))
                        {
                            return false;
                        }
                    }

                    continue;
                }

                if (!this.Fits(a, b, invariant || IsInvariantLayer(actual, this.binding), instance: instance))
                {
                    holds = false;
                    if (!this.Goes(instance))
                    {
                        return false;
                    }
                }
            }

            return holds;
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

        // An Origin position's failure: a member's inside a Callable comparison, a plain position outside every one. A member's that the
        // relation judge does not refute or leave Unknown from the premises alone (it holds, or is Unrepresentable, only through an
        // inferred or body-local region) is the located limit.
        private readonly ComparisonFailure OriginFailure(BoundOrigin longer, BoundOrigin shorter, bool equality, in CallableInstance instance)
        {
            var kind = instance.Expected is null ? ComparisonFailureKind.Position : ComparisonFailureKind.Origin;
            var (a, b) = instance.Actual is null ? (longer, shorter) : (instance.Instantiate(longer), instance.Instantiate(shorter));
            if (kind == ComparisonFailureKind.Origin && this.binding is { } judge && this.use is { } at &&
                judge.JudgeOriginRelation(a, b, at, judge.formingBody) is not (OriginJudgment.Refuted or OriginJudgment.Unknown))
            {
                kind = ComparisonFailureKind.Limit;
            }

            return new(kind, longer, shorter, equality, instance);
        }

        private bool Fail(in ComparisonFailure failure)
        {
            this.failure = failure;
            return false;
        }

        // Whether a recording root's walk goes on past a failure at `instance`'s level: outside every Callable comparison, unless a
        // structural failure ended it.
        private readonly bool Goes(in CallableInstance instance) => this.facts is not null && instance.Expected is null && !this.ended;

        // A failure at a position: outside every comparison a recording root keeps it and goes on (true); otherwise it is the
        // comparison's failure (false).
        private bool Keep(in ComparisonFailure failure, in CallableInstance instance)
        {
            this.failure = failure;
            if (!this.Goes(instance))
            {
                return false;
            }

            this.facts!.Add(failure);
            return true;
        }

        // A structural failure: the walk's last fact, and it ends the walk.
        private bool End(in CallableInstance instance)
        {
            _ = this.Keep(ComparisonFailure.Shape, instance);
            this.ended = true;
            return false;
        }

        private readonly bool OriginFits(BoundOrigin a, BoundOrigin b, in CallableInstance instance, ulong condition = 0)
            => instance.Actual is not null ? InstanceOutlives(a, b, instance, this.binding, this.use, 0) :
                this.binding is null ? OriginOutlives(a, b) : this.binding.FitOrigin(a, b, this.use!, instance, condition);

        // SPEC 10.7, 15.3.7: an implementation fits a required signature when its inputs accept the required inputs and its result fits
        // the required result. Each input the implementation binds per call is instantiated at the required input in the same position,
        // per call or fixed, so only its referent is compared; every other input is compared as written, contravariantly. Inside a Function
        // Type only `Everywhere` treats Origin bindings as equal. Under the owner's (a), the implementation's own well-formedness, its
        // inputs' and result's, is one of its conditions, proven from the required signatures' own well-formedness (the instance's
        // premises) and the premises at the use (FailedSignatureWellFormedness); without a Binding no premise is visible.
        private bool FunctionFits(BoundType actual, BoundType expected, bool invariant, in CallableInstance instance)
        {
            var actualInputs = actual.Components[0];
            var expectedInputs = expected.Components[0];
            if (!ReferenceEquals(actualInputs, expectedInputs) &&
                (actualInputs.Kind != expectedInputs.Kind || actualInputs.Components.Count != expectedInputs.Components.Count))
            {
                return this.Fail(ComparisonFailure.Shape);
            }

            var outer = this.part;
            var everywhere = outer == OriginPart.Everywhere;
            this.part = everywhere ? OriginPart.Everywhere : OriginPart.Judged;
            var fits = this.MembersFit(actual, expected, invariant, instance);
            this.part = outer;
            if (!fits || this.binding is null || everywhere || this.binding.FailedSignatureWellFormedness(actual, instance, this.use!) is not { } formed)
            {
                return fits;
            }

            var part = formed.Part < actualInputs.Components.Count ? formed.Part : -1;
            return this.Fail(new ComparisonFailure(ComparisonFailureKind.WellFormedness, formed.Longer, formed.Shorter, false, instance) { Part = part });
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
