// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The one enumeration of a signature part's well-formedness premises (WellFormedPremises; SPEC 8.1.1, 15.3.3, 15.3.7, 15.6.1, 15.6.4
// step 3; R7-O1 d, which replaced the six-valued visit).
// Responsibility: each relation a well-formed part assumes and each use proves: every Origin a borrow layer stores below it outlives that
// layer's Origin (a pair layer's slot in its binder's borrow cases: WellFormedLayer, StoredSlot), and, for an input or a selected call's
// result, each declared clause of its Types, substituted with the Origins of its occurrence, in the order the walk meets them. A
// signature part is walked as the premise is formed: never into a nested Function Type (AddResultPremises); beside an Evidence
// position's own Type, which is well formed, only where the fit replaced an Origin; a slot of the pattern only beside such a Type, whose
// solution its contributor proves otherwise (R1, DECISIONS BU-99).
// Input: the pattern part, the Type it stands for at the use (the pattern itself or its substitution), the argument's own Type beside it,
// the subject (WellFormedSubject, with the named function for a result), the use, and the Callable comparison whose scope gives a pair
// layer's cases (RelationScope; default outside one).
// Output: WellFormedPremise items, a stored relation or a clause; the entries consume them (Binding.ResultPremises.cs): a whole-contract
// comparison proves each (Prove, Instance), a call's inference bounds by each or drops it again, a selected call obliges each, and
// HasOriginConditions asks whether there is one.
// Invariants: lazy: an item is formed when MoveNext reaches it, so what a consumer adds between items (a bound, an obligation) is seen
// by the holds after it, in the walk's order. A relation is held, and never an item, where it holds by itself, where the argument's
// kept Origin satisfies it, or where the named function's definition proves it without its result premise (resultPremiseExcluded).
// One query of wellFormedParts per enumeration and one of storedParts per borrow layer's stored walk: a pattern part met again beside
// the same argument part and substituted part is skipped (DECISIONS BU-132, R4-M164 U1e), as its first meet, complete in the query,
// gave its premises, each of which the consumers keep once; so a pattern part is walked at most once per distinct argument part beside
// it and change of its substituted part between consecutive meets of that pair, and whether that bounds a form whose argument parts
// alternate beside a shared pattern part is unshown (PLAN G387). One premise session from construction to Dispose (PLAN G778). The
// frames are Binding's scratch (wellFormedFrames), one stack per nesting level, returned at Dispose.
// Must not: judge, record, oblige or bound: a verdict and its consequence are the consumer's.
public sealed partial class Binding
{
    // The pattern parts one enumeration met, and those one borrow layer's stored walk met, each beside its argument part, with the
    // substituted part it last met that pair with (DECISIONS BU-55, BU-132, R4-M164 U1e).
    private readonly TypeMemo<(BoundType Pattern, BoundType? Argument), BoundType> wellFormedParts = new();
    private readonly TypeMemo<(BoundType Pattern, BoundType? Argument), BoundType> storedParts = new();

    // The enumerations' frame stacks: a consumer may begin another enumeration between the items of one.
    private readonly ScratchBuffers<WellFormedFrame> wellFormedFrames = new();

    // The definition (Definition) whose result premise is set aside while a use asks whether it proves a relation from its other premises.
    private Koto? resultPremiseExcluded;

    // Whose well-formedness premises an enumeration reads: an input's, which no other premise of the definition replaces (its Types' own
    // clauses included); a named function's result, which its definition may prove without the premise; or a selected call's result,
    // those premises and its Types' own clauses, which are the call's where they are over its region (RequireResultClause).
    private enum WellFormedSubject : byte
    {
        Input,
        NamedResult,
        CallResult,
    }

    private static bool SameLayer(BoundType actual, BoundType result)
        => actual.Kind == result.Kind && actual.Components.Count == result.Components.Count && actual.OriginArguments.Count == result.OriginArguments.Count;

    // Whether a query of an enumeration meets a pattern part beside these parts for the first time; a leaf, or a part that stores no
    // Origin, is cheaper to walk again than to record.
    private static bool FirstMet(TypeMemo<(BoundType Pattern, BoundType? Argument), BoundType> parts, BoundType pattern, BoundType result, BoundType? actual, uint query)
    {
        if (pattern.Components.Count == 0 || !pattern.StoresOrigin)
        {
            return true;
        }

        if (parts.TryGet((pattern, actual), query, out var met) && ReferenceEquals(met, result))
        {
            return false;
        }

        parts.Set((pattern, actual), query, result);
        return true;
    }

    // A well-formedness premise: a relation a borrow layer stores, `Longer outlives Outer` in the pattern and `Value outlives Substituted`
    // at the use, required in the Semantics cases Condition (0 for every case), stored in Type; or a declared clause of Type (Clause), its
    // substituted ends as both pairs, an equality where Equality.
    private readonly record struct WellFormedPremise(
        BoundOrigin Longer, BoundOrigin Outer, BoundOrigin Value, BoundOrigin Substituted, ulong Condition, BoundType Type, OriginRelationKoto? Clause, bool Equality);

    // A part of an enumeration's walk: a pattern part beside its Type at the use and the argument part beside them, above a borrow layer
    // or below one (Stored), with the step it resumes at and, above a layer, the declaration whose clauses its Type adds.
    private record struct WellFormedFrame(BoundType Pattern, BoundType Type, BoundType? Argument, bool Stored)
    {
        internal int Step { get; set; }

        internal OriginDeclaration? Clauses { get; init; }
    }

    private ref struct WellFormedPremises
    {
        private readonly Binding binding;
        private readonly WellFormedSubject subject;
        private readonly FunctionKoto? named;
        private readonly Koto use;
        private readonly CallableInstance comparison;
        private readonly OriginPremiseSession? session;
        private readonly uint query;
        private WellFormedFrame[] frames;
        private int depth;

        // The borrow layer whose stored walk runs: its Origin in the pattern and at the use, the cases it exists in, its query.
        private (BoundOrigin Outer, BoundOrigin Substituted, ulong Condition, uint Query) layer;

        internal WellFormedPremises(
            Binding binding, BoundType pattern, BoundType type, BoundType? argument, WellFormedSubject subject, FunctionKoto? named, Koto use, in CallableInstance comparison)
        {
            this.binding = binding;
            this.subject = subject;
            this.named = named;
            this.use = use;
            this.comparison = comparison;
            this.session = binding.OpenPremiseSession();
            this.query = binding.wellFormedParts.Begin(binding.storageVersion);
            this.frames = binding.wellFormedFrames.Rent(16);
            this.EnterPart(pattern, type, argument);
        }

        public WellFormedPremise Current { readonly get; private set; }

        public readonly WellFormedPremises GetEnumerator() => this;

        public bool MoveNext()
        {
            while (this.depth > 0)
            {
                ref var frame = ref this.frames[this.depth - 1];
                if (frame.Stored ? this.NextStored(ref frame) : this.NextPart(ref frame))
                {
                    return true;
                }
            }

            return false;
        }

        public readonly void Dispose()
        {
            this.binding.wellFormedFrames.Return(this.frames, clearArray: true);
            this.session?.Close();
        }

        private static bool SameOriginArguments(BoundType type, BoundType? argument)
        {
            if (argument is null)
            {
                return false;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(type.OriginArguments[i], argument.OriginArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Shaped(BoundType pattern, BoundType type) => pattern.Kind == type.Kind && pattern.Components.Count == type.Components.Count;

        // A part above a borrow layer, as the walk meets it. A slot of the pattern is its contributor's beside no argument, and beside an
        // Evidence position's own Type the walk goes on over the slot's solution; a part that stores no Origin, a nested Function Type and
        // a Type of another shape add nothing, and an argument of another shape covers every position.
        private void EnterPart(BoundType pattern, BoundType type, BoundType? argument)
        {
            this.binding.OriginProofMetrics?.WellFormedPart();
            if (pattern.Kind == BoundTypeKind.Parameter)
            {
                if (argument is null)
                {
                    return;
                }

                pattern = type;
            }

            if (!pattern.StoresOrigin || pattern.Kind == BoundTypeKind.Function || !Shaped(pattern, type))
            {
                return;
            }

            if (argument is not null && !SameLayer(argument, type))
            {
                argument = null;
            }

            this.Push(new(pattern, type, argument, Stored: false) { Clauses = this.TypeClauses(type, argument) });
        }

        // A part below a borrow layer: a layer stores every Origin its stored Type holds, a slot's solution included (SPEC 8.1.1, 15.3.7;
        // DECISIONS BU-99). A pair layer of the pattern stores its slot whatever its substitution is, any other layer only beside the same
        // shape.
        private void EnterStored(BoundType pattern, BoundType type, BoundType? argument)
        {
            this.binding.OriginProofMetrics?.WellFormedPart();
            pattern = pattern.Kind == BoundTypeKind.Parameter ? type : pattern;
            if (!type.StoresOrigin || pattern.Kind == BoundTypeKind.Function || (!Shaped(pattern, type) && !TryPairLayer(pattern, out _, out _)))
            {
                return;
            }

            if (argument is not null && !SameLayer(argument, type))
            {
                argument = null;
            }

            this.Push(new(pattern, type, argument, Stored: true));
        }

        // The declaration whose clauses a part's Type adds (SPEC 15.3.3): an input's or a selected call's result's, completed, with Origin
        // arguments other than the argument's own Type's, whose well-formedness covers them.
        private readonly OriginDeclaration? TypeClauses(BoundType type, BoundType? argument)
        {
            if (this.subject == WellFormedSubject.NamedResult || type.Symbol is not { } symbol || type.OriginArguments.Count == 0 || SameOriginArguments(type, argument))
            {
                return null;
            }

            return this.binding.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.Completed ? declaration : null;
        }

        // One step of a part above a borrow layer: its Type's next clause, then the stored walk of the layer it forms at the use, then its
        // components; whether the step formed an item. The layer is the one the substituted part forms (WellFormedLayer), its pair layer's
        // slot in the borrow cases its binder has in the enumeration's relation (its comparison's scope, else the use's: RelationScope).
        private bool NextPart(ref WellFormedFrame frame)
        {
            var clauses = frame.Clauses is { } declaration ? declaration.Relations.Length : 0;
            var step = frame.Step++;
            if (step < clauses)
            {
                var relation = frame.Clauses!.Relations[step];
                var arguments = (BoundOrigin[])frame.Type.OriginArguments;
                var longer = this.binding.SubstituteStoredOrigin(relation.Longer, frame.Type.Symbol!.Declaration, arguments);
                var shorter = this.binding.SubstituteStoredOrigin(relation.Shorter, frame.Type.Symbol!.Declaration, arguments);
                this.Current = new(longer, shorter, longer, shorter, 0, frame.Type, relation.Syntax, relation.Equality);
                return true;
            }

            var pattern = frame.Pattern;
            if (step == clauses)
            {
                var at = new UseScope(this.use);
                if (pattern.Components.Count != 0 && pattern.Origin is { } outer && this.binding.WellFormedLayer(frame.Type, ref at, this.comparison) is { } formed)
                {
                    this.layer = (outer, formed.Outer, RequiredCondition(formed.Condition), this.binding.storedParts.Begin(this.binding.storageVersion));
                    this.EnterStored(pattern.Components[0], formed.Target, frame.Argument?.Components[0]); // The frame moves.
                }

                return false;
            }

            var i = step - clauses - 1;
            if (i < pattern.Components.Count)
            {
                var argument = frame.Argument?.Components[i];
                if (FirstMet(this.binding.wellFormedParts, pattern.Components[i], frame.Type.Components[i], argument, this.query))
                {
                    this.EnterPart(pattern.Components[i], frame.Type.Components[i], argument); // The frame moves.
                }

                return false;
            }

            this.depth--;
            return false;
        }

        // One step of a part below a borrow layer: the Origin it stores itself, then, beside the pattern's shape, its Origin arguments and
        // its components; whether the step formed an item. A pair layer's slot, the implicit `o` of an original `s/T` included, is stored
        // only in its binder's borrow cases; at a use it is the slot its substitution stores: a borrow's Origin, a caller's pair slot in
        // that pair's borrow cases, and none for a value binding, which leaves the relation vacuous (SPEC 8.1.1, 8.1.2, 15.6.5).
        private bool NextStored(ref WellFormedFrame frame)
        {
            var pattern = frame.Pattern;
            var type = frame.Type;
            var argument = frame.Argument;
            var shaped = Shaped(pattern, type);
            var arguments = shaped ? Math.Min(pattern.OriginArguments.Count, type.OriginArguments.Count) : 0;
            var step = frame.Step++;
            if (step == 0)
            {
                var at = new UseScope(this.use);
                return this.binding.OuterOrigin(pattern) is { } origin && this.binding.StoredSlot(type, ref at, out var slot, this.comparison) is { } value &&
                    this.Premise(origin, type, value, argument is null ? null : this.binding.OuterOrigin(argument), RequiredCondition(slot));
            }

            if (step <= arguments)
            {
                return this.Premise(pattern.OriginArguments[step - 1], type, type.OriginArguments[step - 1], argument?.OriginArguments[step - 1], 0);
            }

            var i = step - arguments - 1;
            if (shaped && i < pattern.Components.Count)
            {
                var child = argument?.Components[i];
                if (FirstMet(this.binding.storedParts, pattern.Components[i], type.Components[i], child, this.layer.Query))
                {
                    this.EnterStored(pattern.Components[i], type.Components[i], child); // The frame moves.
                }

                return false;
            }

            this.depth--;
            return false;
        }

        // Forms the premise that `longer`, stored in `type`, outlives the layer's Origin (`value`, the layer's at the use), unless it is
        // held: it holds by itself; `kept`, the argument's Origin at the same position, satisfies it, since the argument's Type is well
        // formed and the fit, judged on its own, makes its outer Origin outlive the substituted one (the two compared as the fit reads
        // them at the use: OriginAtUse, a `let` region of the use's body as its value; PLAN G492-a (ii)); or the named function's
        // definition proves it without its result premise.
        private bool Premise(BoundOrigin longer, BoundType type, BoundOrigin value, BoundOrigin? kept, ulong condition)
        {
            condition |= this.layer.Condition;
            if (OriginOutlives(longer, this.layer.Outer) || OriginOutlives(value, this.layer.Substituted) ||
                (kept is not null && ReferenceEquals(this.binding.OriginAtUse(value, this.use, this.comparison), this.binding.OriginAtUse(kept, this.use, this.comparison))) ||
                this.DefinitionProves(longer, condition))
            {
                return false;
            }

            this.Current = new(longer, this.layer.Outer, value, this.layer.Substituted, condition, type, null, Equality: false);
            return true;
        }

        private readonly bool DefinitionProves(BoundOrigin longer, ulong condition)
        {
            if (this.named is not { } function)
            {
                return false;
            }

            var definition = CalleeDefinition(function);
            var excluded = this.binding.resultPremiseExcluded;
            this.binding.resultPremiseExcluded = definition;
            try
            {
                return this.binding.ProvesOriginOutlives(longer, this.layer.Outer, definition, condition);
            }
            finally
            {
                this.binding.resultPremiseExcluded = excluded;
            }
        }

        private void Push(in WellFormedFrame frame)
        {
            if (this.depth == this.frames.Length)
            {
                // The top of the scratch: every enumeration a consumer began between items has returned its frames.
                var frames = this.frames;
                this.binding.wellFormedFrames.Return(frames);
                this.frames = this.binding.wellFormedFrames.Rent(2 * frames.Length);
                frames.AsSpan(0, this.depth).CopyTo(this.frames);
            }

            this.frames[this.depth++] = frame;
        }
    }
}
