// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Type relation's one walk (SPEC 3.8, 10.7, 15.6.1).
// - Responsibility: whether `actual` is a subtype of `expected`, part by part, and a Function Type by one Callable comparison
//   (FunctionFits), two at an invariant position, one each way (SPEC 3.8, 15.3.5), whose implementation conditions it judges at every
//   nesting when it has a Binding.
// - Inputs: the walk's owner, the Binding whose member made it (always the maker), which decides its outer-Origin fits and Origin
//   argument variances; the query: the premises reader, the Binding and use whose premises and Origins the Origin part reads, and in
//   whose view the root's two sides are formed (ClassForm, R7-C2c2 c2-4b's J3; none: the relation as written), which Origin bindings
//   count (OriginPart), the row sink of a reader of a comparison's region conjuncts (CallableInstance.Rows: its root comparisons
//   take it, a nested one inherits its enclosing comparison's; none: such a conjunct is not proven), and a placing fit's raw-fit sink
//   (RawFits), to which FitOuterOrigins appends each raw fit of the parts outside every Callable comparison.
// - Outputs: whether the relation holds and, when it does not, its first failure where the walk found it (ComparisonFailure), which the
//   reader that records the failure takes from the walk that decided, with the components down to the failure that ended it
//   and whether it is that part's own shape (ComponentPath); a recording root's walk also keeps one fact per failing position
//   (comparisonFacts, design-r2 Amendment 1 A2), and CheckTypeUse's root one result per part it walks outside every Callable comparison
//   (FitPart, in walk order), which CheckTypeUse decomposes instead of walking each part again (it walks again only an invariant part
//   that this walk meets reversed, under a contravariant one). A walk with a row sink appends to it each region conjunct its
//   comparisons left as rows (LeavesBodyRows); the reader that set the sink forms or drops them, so "holds" there is conditional on them.
//   Under a comparison's scope (CallableInstance.Scope) a pair with a held side (EvidenceProjection) holds, binds nothing and is counted
//   in the held sink (heldPairs), so "holds" there is Unknown for the comparison's readers, which read the sink against their mark; a side
//   the evidence determines is related as its image.
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

    // The pair those facts' walk walked, as its intake formed it (RelationWalk.Root): published by the first recording walk after the
    // reader that opens the facts clears both, so its parts and facts are parts and facts of this pair (F's C2 ruling 315).
    private (BoundType? Actual, BoundType? Expected) comparisonRoot;

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
        // the plain position's search records it (design-r2 Amendment 1, A1); in a whole-contract comparison (OriginPart.Contract) only
        // an implementation's per-call Origin no bound of which reaches a fixed Origin, even through a representative
        // SolveOriginInference holds (SPEC 15.3.6).
        Limit,
    }

    // SPEC 10.7, 15.6.1, 23.1: the first failure of one walk, where the walk found it: the failing condition (Kind, Part), its ends as
    // written with the comparison that instantiates them, and the members from the nearest Callable comparison (Path, one byte per member,
    // the outermost highest: a parameter n + 1, the result ResultStep; Depth saturates at 8).
    internal readonly record struct ComparisonFailure(
        ComparisonFailureKind Kind, BoundOrigin? Longer, BoundOrigin? Shorter, bool Equality, CallableInstance Instance)
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

    // SPEC 3.8: the components from a walk's root down to its failure, outside Function Types: one byte per component (n + 1), the
    // outermost lowest: a Tuple's element, a constructed Type's argument, an array's element, a layer's target. None (0) where the
    // failure is the root's own; a Function Type's comparison publishes none below it (its members are ComparisonFailure.Path's).
    // The path holds the eight outermost steps (a deeper one is shifted out), and a component beyond the 254th publishes none
    // below it. Shape: the failure is the own shape of the part the steps reach (SameShape: its kind, Semantics, symbol, lengths or
    // arity), not a failure inside it; a path that loses a step loses it (G812 ruling 116).
    internal readonly record struct ComponentPath(ulong Path, bool Shape)
    {
        // A failure at the root's own shape.
        internal static readonly ComponentPath AtShape = new(0, true);

        // The path as the enclosing part sees it: within its component `index`.
        internal ComponentPath Within(int index)
            => index >= 0xFF ? default : new((this.Path << 8) | (uint)(index + 1), this.Shape && (this.Path >> 56) == 0);

        // The path as the part `outer` names sees it: within each of `outer`'s steps, its innermost first.
        internal ComponentPath Within(ComponentPath outer)
        {
            var steps = 0;
            for (var rest = outer.Path; rest != 0; rest >>= 8)
            {
                steps++;
            }

            var path = this;
            for (var k = steps - 1; k >= 0; k--)
            {
                path = path.Within((int)((outer.Path >> (8 * k)) & 0xFF) - 1);
            }

            return path;
        }

        // The part of `type`, a Type whose formation gave the compared shape, that the path names (false: none): its component at
        // each step, as far as formation keeps the components in their positions (a Tuple, a constructed Type, an array, a written
        // layer; not a pair application, whose case decides whether it is a layer). A failure at the shape of a part that formation
        // keeps is that shape's, which no binding changes (G812 ruling 116): the part forms no Type (`member` null).
        internal bool Names(BoundType type, out BoundType? member)
        {
            member = type;
            var path = this.Path;
            for (; path != 0 && KeepsComponents(member); path >>= 8)
            {
                var step = (int)(byte)path;
                if (step == 0 || step > member.Components.Count)
                {
                    member = null;
                    return false;
                }

                member = member.Components[step - 1];
            }

            var shape = path == 0 && this.Shape && KeepsComponents(member);
            var named = shape || !ReferenceEquals(member, type);
            member = shape || !named ? null : member;
            return named;
        }

        private static bool KeepsComponents(BoundType type)
            => type.Kind is BoundTypeKind.Tuple or BoundTypeKind.Constructed or BoundTypeKind.Array or BoundTypeKind.FixedArray or BoundTypeKind.Semantics;
    }

    // SPEC 15.6.1, 23.1.2: one part of CheckTypeUse's walk outside every Callable comparison, as a walk rooted at it would answer it: the
    // pair as walked, whether the relation holds there, whether a position in it held by resting (fitRested), its first failure, whether
    // its own shape held (Shaped: the walk did not end at the pair itself), its facts' range in comparisonFacts and the index after its
    // own parts. A part that identity discharges has none. A pair of composite parts the walk entered before in the same invariance is
    // entered once (PLAN G492-a (ii)): a later meeting is a part that names the earlier one (Same, its index + 1), with its answers and no
    // parts of its own. Fails is CheckTypeUse's: its decomposition of the part failed (CheckTypeUseCore writes it when it reads the part).
    internal readonly record struct FitPart(
        BoundType Actual, BoundType Expected, bool Holds, bool Rested, bool Shaped, ComparisonFailure Failure, int FactStart, int FactEnd, int End,
        int Same = 0, bool Fails = false);

    // The composite parts CheckTypeUse's current root walk entered, by pair and invariance: the part's index in its FitPart list. A
    // meeting of the same two interned Types in the same invariance within one walk relates them as the first did, reading the same
    // premises and Origins, so its verdict, resting and facts are the first's (DECISIONS BU-19's table, one query per root walk; warm 0 B).
    private readonly TypeMemo<(BoundType Actual, BoundType Expected, bool Invariant), int> enteredParts = new();

    // SPEC 8.6.3, 8.3.1, 15.3.6 (R7-C2e2 e2-5b, design R2.2 and R2.10 N1): the held sink, the count of pairs a comparison's relations held
    // because a side is a held projection (Relate's and CollectOriginInference's arms): such a pair neither holds nor fails, so a reader
    // marks the count before its judgments and reads a verdict that may rest on one (`heldPairs != mark`) as Unknown. Only grows.
    private int heldPairs;

    // Which Origin bindings a relation walk compares: every one (Judged), every one as a whole-contract comparison's Origin part, from the
    // premises alone, so a member that only an inferred or body-local region decides is no limit (Contract, SPEC 15.6.1, 15.3.7: a
    // Callable signature fit or a conversion's contract comparison; DECISIONS BU-93), none outside Function Types (Structural, SPEC
    // 15.6.1), or none at all (Everywhere, SPEC 10.4 step 2, since Origin bindings never rank candidates).
    private enum OriginPart : byte
    {
        Judged,
        Contract,
        Structural,
        Everywhere,
    }

    // One walk of the relation with its query and its result (see the file contract).
    private struct RelationWalk
    {
        private readonly Binding owner;
        private readonly Binding? binding;
        private readonly Koto? use;
        private readonly List<ComparisonFailure>? facts;
        private readonly List<FitPart>? parts;
        private readonly uint partQuery;
        private readonly int start;
        private OriginPart part;
        private ComparisonFailure failure;
        private bool ended;
        private int endedAt;
        private ComponentPath components;
        private bool formed; // whether the root's pair was formed (J3: only the root forms; its parts are parts of formed Types)
        private bool metOpenSlot;
        private (BoundType Actual, BoundType Expected) root; // the pair formed at the intake, which the walk walked

        internal RelationWalk(Binding owner, Binding? binding, Koto? use, OriginPart part)
        {
            this.owner = owner;
            this.binding = binding;
            this.use = use;
            this.part = part;
            this.endedAt = -1;
        }

        // A recording root's walk: its facts follow `facts`' current end, which the root truncates back once it has read them.
        internal RelationWalk(Binding owner, Binding binding, Koto use, List<ComparisonFailure> facts)
            : this(owner, binding, use, OriginPart.Judged)
        {
            this.facts = facts;
            this.start = facts.Count;
        }

        // CheckTypeUse's root: also one FitPart per part outside every Callable comparison, appended to `parts`, which the root truncates.
        internal RelationWalk(Binding owner, Binding binding, Koto use, List<ComparisonFailure> facts, List<FitPart> parts)
            : this(owner, binding, use, facts)
        {
            this.parts = parts;
            this.partQuery = binding.enteredParts.Begin(binding.storageVersion);
        }

        // The first failure of the walk's last false answer.
        internal readonly ComparisonFailure Failure => this.facts is { } kept && kept.Count > this.start ? kept[this.start] : this.failure;

        // The components down to the failure that ended the walk's last false answer (ComponentPath): a failure the walk went on
        // past (a recording root's, outside every Callable comparison) adds no step.
        internal readonly ComponentPath FailedComponents => this.components;

        // PLAN G924 AF-3b v3-A (ArgFlow ruling 151 B-a): whether the walk held a part against an open atom (Binding.OpenSlots), which an
        // identity question reads: such a pair holds for a fit and is identical for none (SPEC 10.8 Unbound slots). A reference-equal
        // pair is discharged before the atom arm, so one atom met by itself never sets it.
        internal readonly bool MetOpenSlot => this.metOpenSlot;

        // The pair the walk walked (Intake), whose parts its recorded parts and facts are: every reader of them reads this pair.
        internal readonly (BoundType Actual, BoundType Expected) Root => this.root;

        // The query's row sink, set by the reader that walks (the file contract's Inputs; CallableInstance.Rows); none: not proven.
        internal List<BindingObligation>? Rows { get; init; }

        // The query's raw-fit sink, set by a placing fit's walk (FitsTypeAt's for a placing caller, CheckTypeUse's; G147 ruling 75 Q-A
        // (a)); none: the walk places no value.
        internal List<RawFit>? RawFits { get; init; }

        // The node of its proofs' judgment (QuerySite): its use, else Nested (the context-free relations Fits reads).
        private readonly QuerySite Site => this.use is { } at ? at : QuerySite.Nested;

        // SPEC 3.8: the relation; Never relates only to itself. A Function Type's own syntax binds its per-call inputs (SPEC 8.10).
        // `skipOrigin` leaves the outer Origin to an instantiation that already fixed it; `instance` is the Callable comparison whose
        // per-call Origins the parts are compared under.
        internal bool Fits(
            BoundType actual, BoundType expected, bool invariant = false, bool skipOrigin = false, CallableInstance instance = default)
        {
            if (!this.formed)
            {
                (actual, expected) = this.Intake(actual, expected);
            }

            if (this.parts is not { } parts || instance.Expected is not null || this.Discharged(actual, expected))
            {
                return this.Relate(actual, expected, invariant, skipOrigin, instance);
            }

            // A pair of composite parts entered before in this invariance answers as it did there (FitPart.Same); the comparison of a
            // Function part, an instantiated outer Origin and a root's own inputs are walked where they occur.
            var key = (actual, expected, invariant);
            var once = !skipOrigin && actual.Components.Count != 0 && actual.Kind is not (BoundTypeKind.Function or BoundTypeKind.FunctionItem);
            if (once && this.binding!.enteredParts.TryGet(key, this.partQuery, out var earlier))
            {
                var first = parts[earlier];
                this.binding.fitRested |= first.Rested;
                this.failure = first.Holds ? this.failure : first.Failure;
                parts.Add(first with { End = parts.Count + 1, Same = earlier + 1 });
                return first.Holds;
            }

            // A part's resting is its own (FitOrigin sets fitRested), and the enclosing part's includes it.
            var entry = parts.Count;
            var factStart = this.facts!.Count;
            var outerRested = this.binding!.fitRested;
            parts.Add(default);
            this.binding.fitRested = false;
            var holds = this.Relate(actual, expected, invariant, skipOrigin, instance);
            var failed = holds ? default : this.facts.Count > factStart ? this.facts[factStart] : this.failure;
            parts[entry] = new(actual, expected, holds, this.binding.fitRested, this.endedAt != entry, failed, factStart, this.facts.Count, parts.Count);
            if (once)
            {
                this.binding.enteredParts.Set(key, this.partQuery, entry);
            }

            this.binding.fitRested |= outerRested;
            return holds;
        }

        // The parts of the relation no Origin binding decides: Kind, Semantics, Symbol, Closure context, lengths and arity; an inferred
        // length (`[_ of T]`) admits any, and a length holding an atom (Binding.OpenSlots) holds against any length, which the walk publishes
        // (MetOpenSlot) when the rest of the shape holds.
        private bool SameShape(BoundType actual, BoundType expected)
        {
            var inferredLength = expected.Kind == BoundTypeKind.FixedArray && expected.Length < 0;
            var openLength = actual.LengthExpression is { ContainsOpenSlot: true } || expected.LengthExpression is { ContainsOpenSlot: true };
            var same = actual.Kind == expected.Kind && actual.Semantics == expected.Semantics && ReferenceEquals(actual.Symbol, expected.Symbol) &&
                ReferenceEquals(actual.InstanceContext, expected.InstanceContext) && actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments) &&
                (inferredLength || openLength || (actual.Length == expected.Length && ReferenceEquals(actual.LengthExpression, expected.LengthExpression))) &&
                actual.Components.Count == expected.Components.Count && actual.OriginArguments.Count == expected.OriginArguments.Count;
            this.metOpenSlot |= same && openLength && !ReferenceEquals(actual.LengthExpression, expected.LengthExpression);
            return same;
        }

        // R7-C2c2 c2-4b (design R4.12b, J3), F's C2 ruling 315: the relation's one intake, the one formation of the root's pair: at a use,
        // its two sides in their class's form in the use's view (SPEC 8.2.3: all Type checking in the scope treats identical Types as one
        // Type), the Completion unthreaded (ClassForm's contract; R7-C3: the judge decides its domain); nested parts are parts of formed
        // Types. Without a use (SameStructure, IsSubtype) the walk reads no premise and compares as written. The walk publishes the pair
        // (Root), and the first recording walk after its reader opened the facts publishes it with them (comparisonRoot), so no reader of
        // the parts or facts forms a pair of its own.
        private (BoundType Actual, BoundType Expected) Intake(BoundType actual, BoundType expected)
        {
            this.formed = true;
            this.root = (actual, expected);
            if (this.binding is { } judge && this.use is { } at && (actual.HasAbstractPart || expected.HasAbstractPart))
            {
                // The judge decides its domain (R7-C3, C-b: SPEC 8.2.3's abstract Types) before it reads the use's scope, once for the
                // pair (F's C2 ruling 317 (4)).
                var view = judge.ConstraintScope(at);
                this.root = (judge.ClassForm(actual, view, out _), judge.ClassForm(expected, view, out _));
            }

            if (this.facts is not null && this.owner.comparisonRoot.Actual is null)
            {
                this.owner.comparisonRoot = this.root;
            }

            return this.root;
        }

        private bool Relate(BoundType actual, BoundType expected, bool invariant, bool skipOrigin, in CallableInstance instance)
        {
            if (this.Discharged(actual, expected) || actual.Kind == BoundTypeKind.Open || expected.Kind == BoundTypeKind.Open)
            {
                return true; // DECISIONS BU-240 (rule W): a position a split binding decides holds without a record, judged case by case.
            }

            if (actual.Kind == BoundTypeKind.OpenSlot || expected.Kind == BoundTypeKind.OpenSlot)
            {
                this.metOpenSlot = true; // Binding.OpenSlots: an atom holds against any part and binds nothing.
                return true;
            }

            if (instance.Scope is { } scope)
            {
                if (this.owner.EvidenceProjection(actual, scope, this.Site, out var actualImage) || this.owner.EvidenceProjection(expected, scope, this.Site, out var expectedImage))
                {
                    this.owner.heldPairs++; // A held part holds and binds nothing; the comparison's reader reads the held sink (heldPairs).
                    return true;
                }

                if (actualImage is not null || expectedImage is not null)
                {
                    return this.Relate(actualImage ?? actual, expectedImage ?? expected, invariant, skipOrigin, instance); // A determined side is its image.
                }
            }

            if (actual.Kind == BoundTypeKind.Primitive || expected.Kind == BoundTypeKind.Primitive || !this.SameShape(actual, expected))
            {
                _ = this.End(instance);
                this.components = ComponentPath.AtShape; // The failure is this part's own shape.
                return false;
            }

            var structural = this.part is OriginPart.Structural or OriginPart.Everywhere;
            var (outer, longer, shorter) = (OuterOriginFit.None, BoundOrigin.Static, BoundOrigin.Static);
            if (!skipOrigin)
            {
                (outer, longer, shorter) = this.owner.FitOuterOrigins(actual, expected, this.use, this.Site, instance, this.RawFits, invariant);
            }

            var condition = outer == OuterOriginFit.Relation && !structural ? SlotCondition(actual, this.binding, this.use, instance) : 0;
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

                var variance = this.owner.OriginArgumentVariance(actual, i, this.binding);
                var both = invariant || variance is OriginVariance.Invariant or OriginVariance.Unused;
                if (both ? !this.OriginFits(a, b, instance) || !this.OriginFits(b, a, instance) :
                    variance == OriginVariance.Covariant ? !this.OriginFits(a, b, instance) : !this.OriginFits(b, a, instance))
                {
                    holds = false;
                    var failed = variance == OriginVariance.Contravariant && !both ? this.OriginFailure(b, a, false, instance)
                        : this.OriginFailure(a, b, both, instance);
                    if (!this.Keep(failed, instance))
                    {
                        return false;
                    }
                }
            }

            if (actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2)
            {
                // A Function component is one comparison: its first failure is one fact of the walk that reached it. At an invariant
                // position the two Function Types relate both ways (SPEC 3.8, 15.3.5): a Callable comparison is oriented (it instantiates
                // only the implementation's per-call inputs and judges only its well-formedness, SPEC 10.7), so the comparison as written
                // is followed by the one with the sides exchanged, whose members relate one way since the first related them both ways.
                // A comparison that fails keeps none of the region conjuncts it left as rows: its failure is its one fact (SPEC 15.6.1,
                // 23.1.2); one that holds keeps them for the reader, also when another part of the walk fails.
                var comparison = Comparison(actual, expected, instance, InComparison(instance) ? instance.Rows : this.Rows);
                var rows = comparison.Rows?.Count ?? 0;
                if (this.FunctionFits(actual, expected, invariant, comparison) &&
                    (!invariant || ReferenceEquals(actual, expected) || this.FunctionFits(expected, actual, false, comparison.Exchanged)))
                {
                    return holds;
                }

                comparison.Rows?.RemoveRange(rows, comparison.Rows.Count - rows);
                _ = !this.ended && this.Keep(this.failure, instance);
                this.components = default;
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
                            this.components = this.components.Within(i);
                            return false;
                        }
                    }

                    continue;
                }

                if (!this.Fits(a, b, invariant || IsInvariantLayer(actual, this.binding, this.use, instance), instance: instance))
                {
                    holds = false;
                    if (!this.Goes(instance))
                    {
                        this.components = this.components.Within(i);
                        return false;
                    }
                }
            }

            return holds;
        }

        // Whether identity decides the relation of two parts: always, except in a whole-contract comparison (Contract), where a part that
        // carries an Origin may hold a Function Type whose Callable comparison has its own condition, the implementation's own
        // well-formedness at the use (the owner's (a); DECISIONS BU-93), which no identity discharges.
        private readonly bool Discharged(BoundType actual, BoundType expected)
            => ReferenceEquals(actual, expected) && (this.part != OriginPart.Contract || !actual.CarriesOrigin);

        // An Origin position's failure: a member's inside a Callable comparison, a plain position outside every one. A member's that the
        // relation judge does not refute or leave Unknown from the premises alone (it holds, or is Unrepresentable, only through an
        // inferred or body-local region) is the located limit, except in a whole-contract comparison (Contract), which no region decides
        // but a `let` region the comparison reads as the region (ComparedRegionLimit, O-400 (b)).
        private readonly ComparisonFailure OriginFailure(BoundOrigin longer, BoundOrigin shorter, bool equality, in CallableInstance instance)
        {
            var kind = instance.Expected is null ? ComparisonFailureKind.Position : ComparisonFailureKind.Origin;
            var (a, b) = instance.Actual is null ? (longer, shorter) : (instance.Instantiate(longer), instance.Instantiate(shorter));
            if (kind == ComparisonFailureKind.Origin && this.binding is { } judge && this.use is { } at &&
                ((this.part != OriginPart.Contract && judge.JudgeOriginRelation(a, b, at, judge.formingBody) is not (OriginJudgment.Refuted or OriginJudgment.Unknown)) ||
                judge.ComparedRegionLimit(a, b, equality, at)))
            {
                kind = ComparisonFailureKind.Limit;
            }

            return new(kind, longer, shorter, equality, instance);
        }

        private bool Fail(in ComparisonFailure failure)
        {
            this.failure = failure;
            this.components = default;
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
            this.components = default;
            if (!this.Goes(instance))
            {
                return false;
            }

            this.facts!.Add(failure);
            return true;
        }

        // A structural failure: the walk's last fact, and it ends the walk at the last part entered (inside a Callable comparison, the
        // comparison's part).
        private bool End(in CallableInstance instance)
        {
            _ = this.Keep(ComparisonFailure.Shape, instance);
            this.ended = true;
            this.endedAt = this.parts is { } parts ? parts.Count - 1 : -1;
            return false;
        }

        private readonly bool OriginFits(BoundOrigin a, BoundOrigin b, in CallableInstance instance, ulong condition = 0)
            => instance.Actual is not null ? InstanceOutlives(a, b, instance, this.binding, this.use, condition) :
                this.binding is null ? OriginOutlives(a, b) : this.binding.FitOrigin(a, b, this.use!, instance, condition);

        // SPEC 10.7, 15.3.7: an implementation fits a required signature when its inputs accept the required inputs and its result fits
        // the required result. Each input the implementation binds per call is instantiated at the required input in the same position,
        // per call or fixed, so only its referent is compared; every other input is compared as written, contravariantly. Inside a Function
        // Type only `Everywhere` treats Origin bindings as equal, and `Contract` stays a whole-contract comparison. Under the owner's (a),
        // the implementation's own well-formedness, its inputs' and result's, is one of its conditions, proven from the required
        // signatures' own well-formedness (the instance's premises) and the premises at the use (FailedSignatureWellFormedness); without a
        // Binding no premise is visible.
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
            this.part = outer is OriginPart.Everywhere or OriginPart.Contract ? outer : OriginPart.Judged;
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
            for (var i = 0; !this.Discharged(actualInputs, expectedInputs) && i < actualInputs.Components.Count; i++)
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
