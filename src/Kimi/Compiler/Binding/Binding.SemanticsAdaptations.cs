// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // DECISIONS BU-240 (temporary; removed with (R), the split layer as a published case family, PLAN G886): the combination of split cases a
    // check is being judged in (RestsOnSplitCases): each binding that forms no Type the check has met, with the cases its premises
    // admit (SplitCases), its case (an index of SemanticsOrder), the bindings blamed while it cycled, and the stamp of its last read.
    private readonly List<(BoundType Whole, SemanticsMask Admitted, int Case, ulong Blame, int Stamp)> splitCases = [];

    // SPEC 10.8, 8.3.4: while a call's evidence is judged (EnterEvidence), the scope of its solve's node, whose premises judge an abstract
    // evidence Type (PremiseScope); null otherwise.
    private BindingScope? premiseUse;

    // DECISIONS BU-240 (G812 ruling 141, L2): whether the current candidate trial's Unknown proof read a pair slot that a binding forming no
    // Type sources (SelectsUnformed): its premise reads the split, which no premise of the split's case names.
    private bool premiseReadsSplit;

    // DECISIONS BU-240 (G812 ruling 141, L1): whether the current candidate trial's source comparison refused a binding that forms no Type
    // outside a split's run (IsSource): the candidate is refused only through the split.
    private bool refusedOnSplit;

    // DECISIONS BU-240: the formations so far that formed no Type (Unformed); a check compares it with its mark (UnformedSince).
    private int unformedWrites;

    // DECISIONS BU-240: whether a check is being judged in a combination of split cases (RestsOnSplitCases); the runs so far, and those of
    // the outermost search with every search inside its runs (the bound is per check); the read stamps: the last, the run's and the
    // current part's start; the write marks of the run's and the part's start; the bindings earlier parts hand the current one.
    private bool splitOpen;

    private long splitCaseRuns;

    private int splitRuns;

    private int splitReads;

    private int splitRunStamp;

    private int splitPartStamp;

    private int splitRunMark;

    private int splitPartMark;

    private ulong splitCarried;

    // DECISIONS BU-240 (G812 rulings 134, 143): what OpenForm's run formed.
    private BoundType? openForm;

    // DECISIONS BU-240 (G812 ruling 156, SN2): the formations OpenMarked made so far, which a scaling check reads (OpenMarkedWork).
    private long openMarkedFormations;

    // A finite Type family stores complete case Types in SemanticsOrder, packed by its Length mask.
    // Only the selecting binder is a dependency; no source expression or operation plan is retained.
    private enum FamilyTransform : byte
    {
        Target,
        Origin,
        Apply,
    }

    /// <summary>Gets the pair selector of a layer or finite result-Type family.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="whole">The selector's whole Type.</param>
    /// <returns>Whether a selector remains.</returns>
    internal static bool TryAdaptationSelector(BoundType type, out BoundType whole)
    {
        if (TryPairLayer(type, out whole, out _))
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.SemanticsAdaptation && type.Symbol?.WholeType is { } selected)
        {
            whole = selected;
            return true;
        }

        whole = null!;
        return false;
    }

    /// <summary>Forms the finite family of complete result Types of a short adaptation.</summary>
    /// <param name="pair">The selector's pair target Symbol.</param>
    /// <param name="source">The independently fixed input Type.</param>
    /// <param name="origin">The Origin of a newly produced borrow.</param>
    /// <param name="scope">The adaptation's scope, whose premises give each binder's admitted cases (SPEC 8.3.4).</param>
    /// <returns>An interned family, collapsed when every case is identical.</returns>
    internal BoundType SemanticsAdaptation(BindingSymbol pair, BoundType source, BoundOrigin? origin, BindingScope scope)
        => this.AdaptationFamily(pair, source, origin, scope);

    /// <summary>Gets the possible outer Semantics of a complete Type or finite result-Type family.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="scope">The available premises.</param>
    /// <returns>A conservative set of outer Semantics.</returns>
    internal SemanticsMask ResultSemantics(BoundType type, BindingScope scope)
    {
        if (type.Kind != BoundTypeKind.SemanticsAdaptation)
        {
            if (TryPairLayer(type, out var whole, out var target))
            {
                var admitted = this.AdmittedSemantics(whole, scope);
                // Applying owner is transparent; its result can itself be a borrow or handle.
                return type.Kind == BoundTypeKind.SemanticsApplication && (admitted & SemanticsMask.Owner) != 0
                    ? (admitted & ~SemanticsMask.Owner) | this.ResultSemantics(target, scope) : admitted;
            }

            if (type.IsAbstract)
            {
                return this.TargetCases(type, scope, default) == SemanticsMask.Owner ||
                    this.RequestCapability(type, this.Library.ObjectPayload, scope) == ConstraintProof.Proven ||
                    this.ProveSealed(type, scope) == ConstraintProof.Proven ||
                    this.RequestCapability(type, this.Library.PrimitiveInteger, scope) == ConstraintProof.Proven
                    ? SemanticsMask.Owner : this.AdmittedSemantics(type, scope);
            }

            return type.Semantics.ToMask();
        }

        var result = SemanticsMask.None;
        for (var i = 0; i < type.Components.Count; i++)
        {
            result |= this.ResultSemantics(type.Components[i], scope);
        }

        return result;
    }

    /// <summary>Forms a short adaptation's result after its selected Semantics is known.</summary>
    /// <param name="semantics">The selected Semantics.</param>
    /// <param name="source">The complete input Type.</param>
    /// <param name="origin">The Origin of a newly produced borrow.</param>
    /// <param name="scope">The adaptation's scope, whose premises give each binder's admitted cases (SPEC 8.3.4).</param>
    /// <returns>The normalized ordinary Type or a finite family over an independent source selector.</returns>
    internal BoundType AdaptedType(SemanticsKind semantics, BoundType source, BoundOrigin? origin, BindingScope scope)
    {
        if (ReferenceEquals(source, BoundType.Never) || semantics == SemanticsKind.Owner)
        {
            return source;
        }

        if (IsObjectSemantics(semantics))
        {
            if (TryAdaptationSelector(source, out var whole))
            {
                return this.AdaptationFamily(whole.Symbol!, source, origin, scope, semantics);
            }

            if (semantics == SemanticsKind.ObjRef && source.Semantics == SemanticsKind.ObjRef)
            {
                return source;
            }

            if (IsBorrow(semantics) && source.IsObjectBorrow)
            {
                origin = source.Origin ?? origin;
            }

            if (IsObjectSemantics(source.Semantics) && source.Components.Count == 1)
            {
                source = source.Components[0];
            }
        }

        // Value borrows and addresses wrap the written slot; object targets select its View Target.
        return this.Form(semantics, source, origin);
    }

    private static BoundType? FamilyCase(BoundType family, SemanticsKind mode)
    {
        var mask = (uint)family.Length;
        var bit = (uint)mode.ToMask();
        return (mask & bit) == 0 ? null : family.Components[BitOperations.PopCount(mask & (bit - 1))];
    }

    // The first index of SemanticsOrder after `current` whose Semantics is in `admitted`, or -1.
    private static int NextAdmitted(SemanticsMask admitted, int current)
    {
        for (var i = current + 1; i < SemanticsOrder.Length; i++)
        {
            if ((admitted & SemanticsOrder[i].ToMask()) != 0)
            {
                return i;
            }
        }

        return -1;
    }

    // SPEC 8.1.2: whether an abstract binding is its own selector, the symbol its applications and families over other targets name: a
    // pair's original or application names its pair, a Type parameter itself. A pair target alone or an unresolved projection has none, so
    // no Type over another target is formed under it (ApplySemantics, SubstituteFamily; PLAN G886: its representation is the remainder).
    private static bool HasOwnSelector(BoundType binding) => binding.Kind == BoundTypeKind.Parameter || binding.Semantics == SemanticsKind.Parameter;

    // DECISIONS BU-240 (temporary with BoundTypeKind.Open; G812 ruling 159): whether one of the Types holds the open formed Type's mark,
    // which an inference slot takes from its marked position below a split call's completion (OpenForm), the one former of marks.
    private static bool HoldsMark(ReadOnlySpan<BoundType?> types)
    {
        foreach (var type in types)
        {
            if (type is { ContainsOpen: true })
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 7.5, 8.3.4, 8.15.1, 10.8: the scope whose premises judge an abstract Type, by the Type's one meaning where it is read. A source's
    // outer Semantics at a call (its evidence, while EnterEvidence holds the solve's node) is judged under the premises at that node, a
    // conditional member's leading region and an enclosing `when` condition included, wherever the Type is in scope there. Any other
    // reading, a pair slot's stored binding above all, is the Type it is in every phase (Binding's substitution, MIR's TyCtxt.Signature),
    // so it reads what the Type carries: its root's declaration, a projection's receiver's (`T` of `T.Element`, so the caller's
    // `T.Element is ObjectPayload` and a refinement through its `T is Holder` are read), where the generic declaring it states its
    // Constraints; a receiver without a declaration leaves the projection's own. A binding that only the use's premises make owner is the
    // remainder of PLAN G886's representation (the call's substitution records no scope).
    private BindingScope PremiseScope(BoundType type)
    {
        while (type is { Kind: BoundTypeKind.AssociatedProjection, Components: [{ Symbol: not null } receiver, ..] })
        {
            type = receiver;
        }

        var declaring = type.Symbol!.Scope;
        for (var scope = this.premiseUse; scope is not null; scope = scope.Parent)
        {
            if (ReferenceEquals(scope, declaring))
            {
                return this.premiseUse!;
            }
        }

        return declaring;
    }

    // SPEC 10.8, 8.3.4: opens the judgment of a call's evidence at the solve's node (PremiseScope) and returns the scope it replaces, which
    // the caller restores.
    private BindingScope? EnterEvidence(SlotFrame frame)
    {
        var saved = this.premiseUse;
        this.premiseUse = this.ConstraintScope(frame.Node);
        return saved;
    }

    // SPEC 8.1.2, 8.2.3, 8.7.3: whether a pair slot's binding is abstract, its outer Semantics not yet known: a pair's original or
    // occurrence, an application (Semantics Parameter), or an abstract Type (a Type parameter, a pair target alone, an unresolved
    // projection: its Semantics field is the constructor default, never an outer Semantics) unless its premises (PremiseScope) prove it
    // owner (ResultSemantics; the owner's O-311 (a)), when it binds as an owner Type. ApplySemantics, the substitution of a family and
    // HasOuterSlot read it.
    private bool IsAbstractBinding(BoundType binding) => binding.IsAbstractType
        ? this.ResultSemantics(binding, this.PremiseScope(binding)) != SemanticsMask.Owner
        : HasOwnSelector(binding);

    // SPEC 8.1.1, 8.2.3 (Rd3): the cases a pair target alone takes from its pair, the selected case or else its binder's admitted set; in
    // the owner case it is `W` itself, an owner Type (`t is owner` alone makes U owner). All for any other Type, whose premises alone fix
    // its outer Semantics. ResultSemantics, CanSelectIdentity and TransformFamily's owner case read it.
    private SemanticsMask TargetCases(BoundType type, BindingScope scope, ReadOnlySpan<PairCase> cases)
        => type is { Kind: BoundTypeKind.TargetProjection, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, WholeType: { } whole } pair }
            ? CaseOf(cases, pair)?.ToMask() ?? this.AdmittedSemantics(whole, scope) : SemanticsMask.All;

    // SPEC 8.1.2: whether a Type is formed under this binding, its split represented: a value Type, an owner-proven abstract Type, one
    // with a selector of its own, a family case by case. A selector-less abstract binding (a pair target alone or an unresolved
    // projection no premise proves owner) forms none (Unformed; PLAN G886).
    private bool FormsUnder(BoundType binding) => !this.IsAbstractBinding(binding) || HasOwnSelector(binding);

    // DECISIONS BU-240: whether a call's pair slots take a source that forms no Type (FormsUnder): a binding the split decides.
    private bool SelectsUnformed(ReadOnlySpan<BoundType?> selectors)
    {
        foreach (var selector in selectors)
        {
            if (selector is not null && !this.FormsUnder(selector))
            {
                return true;
            }
        }

        return false;
    }

    // DECISIONS BU-240 (temporary; removed with the representation of a selector-less binding's split, PLAN G886): a formation under a binding
    // that forms none (FormsUnder) forms nothing (null) and counts one write (unformedWrites), the one record of it; its readers compare
    // the count with a mark (UnformedSince). The split is an Unknown in one candidate's fit (rule W, BU-79): a part whose own pattern
    // forms no Type is unknown (UnformedPart), and a candidate trial rests on it only where every other check passed, which its verdict
    // publishes to the selection (TryCandidate: PendingJudgmentKind.UnformedSplit), its call then one located Unsupported_Kd. A value
    // call's callee signature and a reference's candidate have no part to leave unknown (their binder is fixed before any evidence), so
    // they judge the same check in each combination of cases of the split layers it meets (RestsOnSplitCases): one that fails in every
    // combination fails by itself, its Language record kept, and any other rests on the split. The writers:
    // ApplySemantics, SubstituteFamily, DirectTarget, HasOuterSlot (the inline `IsAbstractBinding` and not `HasOwnSelector` there is
    // FormsUnder's body where the first is already decided); IsSource's comparison reads FormsUnder and publishes nothing.
    private BoundType? Unformed()
    {
        this.unformedWrites++;
        return null;
    }

    // DECISIONS BU-240 with rule W (BU-79; SPEC 8.1.1's table, 8.15.2: a split layer is judged case by case; C-359 C1, E2, E3; temporary,
    // removed with (R)): whether a check that formed a Type no binding represents (a write since `mark`) rests on that split, for a
    // check whose binders are fixed before any evidence. `fails` runs the same check once per combination of cases: in a run, each
    // binding that forms no Type forms `s/target` as its case's layer (ApplySemantics) and keeps an outer slot as that layer does
    // (HasOuterSlot), through InSplitCase.
    // A check is a list of parts, which its reader marks (EnterSplitPart; a reader that marks none is one part):
    // - a part during which a formation formed none is unknown in that run, and the reader judges the next (rule W's wildcard);
    // - a run fails by itself where the check fails with no such write since its last part's start; a run with no failing part ends
    //   the search: the check rests on the split;
    // - a failing run blames the bindings its failing part read, with those the part was handed: the reads of an earlier part
    //   whose result it reads (CarrySplitPart) and, for a joint part (EnterJointSplitPart), every read of the run. The next
    //   combination advances the most recent blamed binding, skipping the unblamed ones, which keep their case; a binding whose
    //   cases are exhausted hands its blame to the most recent binding in it (NextSplitCase). With no blamed binding left the check
    //   fails in every combination: it fails by itself, its writes are dropped and its Language record stands.
    // The runs of a search and of every search opened inside its runs count together: beyond PairBinder.CaseBound (the bound a
    // definition's own cases have, SPEC 8.15.2) the check rests on the split. A search inside another's run keeps that run's
    // cases and enumerates only the bindings it meets itself; the enclosing bindings it read are stamped in the enclosing part.
    // Invariant: a failing part's verdict is a function of the bindings it read and was handed. A blame that is too small can
    // only give an invalid program's resting failure a Language record: a valid check passes its first combination.
    private bool RestsOnSplitCases<TState>(int mark, TState state, Func<Binding, TState, bool> fails)
    {
        var first = this.splitCases.Count;
        var enclosing = (this.splitOpen, this.splitRunStamp, this.splitPartStamp, this.splitRunMark, this.splitPartMark, this.splitCarried);
        if (!this.splitOpen)
        {
            this.splitRuns = 0;
        }

        this.splitOpen = true;
        bool rests;
        var blame = 0UL;
        do
        {
            this.splitRunMark = this.unformedWrites;
            this.splitRunStamp = this.splitReads;
            this.splitCarried = 0;
            this.EnterSplitPart();
            this.splitCaseRuns++;
            rests = ++this.splitRuns > PairBinder.CaseBound || !fails(this, state) || this.UnformedSince(this.splitPartMark);
            blame = this.splitCarried | this.SplitReadsSince(this.splitPartStamp);
            this.unformedWrites = this.splitRunMark;
        }
        while (!rests && this.NextSplitCase(first, blame));

        this.splitCases.RemoveRange(first, this.splitCases.Count - first);
        (this.splitOpen, this.splitRunStamp, this.splitPartStamp, this.splitRunMark, this.splitPartMark, this.splitCarried) = enclosing;
        if (!rests)
        {
            this.unformedWrites = mark;
        }

        return rests;
    }

    // DECISIONS BU-240 (temporary with it; G812 rulings 134, 143; the owner's O-428 (a)): the Type `form` gives a declared Type in the
    // canonical case, each binding at its first admitted Semantics (a search's first run, RestsOnSplitCases), marked open
    // (BoundTypeKind.Open) at each position whose formation read a split binding (OpenMarked); null where it formed none. The split
    // call's completion is its one maker.
    private BoundType? OpenForm<TState>(BoundType declared, TState state, Func<Binding, TState, BoundType, BoundType?> form)
    {
        this.openForm = null;
        var query = this.BeginSelectionQuery(); // A query: its formations publish nothing (their obligations, causes and counters).
        try
        {
            _ = this.RestsOnSplitCases(this.unformedWrites, (declared, state, form), static (binding, check) =>
            {
                binding.openForm = binding.OpenMarked(check.declared, check.state, check.form);
                return false;
            });
        }
        finally
        {
            this.EndSelectionQuery(query);
        }

        return this.openForm;
    }

    // DECISIONS BU-240: in a run, `form` of `declared`, marked open where its formation read a split binding: at the innermost declared
    // component whose own formation read one; a part no component accounts for (a pair layer, an Origin, a formation that changed the
    // Type's shape) is marked whole.
    private BoundType? OpenMarked<TState>(BoundType declared, TState state, Func<Binding, TState, BoundType, BoundType?> form)
    {
        this.openMarkedFormations++;
        var type = this.FormReadsSplit(declared, state, form, out var split);
        var count = declared.Components.Count;
        if (!split || type is null)
        {
            return type;
        }

        if (declared.IsPairLayer || count == 0 || type.Kind != declared.Kind || type.Components.Count != count)
        {
            return this.types.Open(type);
        }

        var parts = this.typeScratch.Rent(count);
        try
        {
            var marked = false;
            for (var i = 0; i < count; i++)
            {
                var part = this.OpenMarked(declared.Components[i], state, form);
                parts[i] = part is { ContainsOpen: true } ? part : type.Components[i];
                marked |= part is { ContainsOpen: true };
            }

            return marked ? this.types.Rebuild(type, parts.AsSpan(0, count)!, type.Origin, (BoundOrigin[])type.OriginArguments) : this.types.Open(type);
        }
        finally
        {
            this.typeScratch.Return(parts, clearArray: true);
        }
    }

    // DECISIONS BU-240: `type` with its open marks removed, the canonical case's Type.
    private BoundType Unmarked(BoundType type)
    {
        if (!type.ContainsOpen)
        {
            return type;
        }

        if (type.Kind == BoundTypeKind.Open)
        {
            return this.Unmarked(type.Components[0]);
        }

        var parts = this.typeScratch.Rent(type.Components.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                parts[i] = this.Unmarked(type.Components[i]);
            }

            return this.types.Rebuild(type, parts.AsSpan(0, type.Components.Count)!, type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(parts, clearArray: true);
        }
    }

    // DECISIONS BU-240: `form` of a declared Type in a run, with whether it read a split binding or formed none.
    private BoundType? FormReadsSplit<TState>(BoundType declared, TState state, Func<Binding, TState, BoundType, BoundType?> form, out bool split)
    {
        var (stamp, mark) = (this.splitReads, this.unformedWrites);
        var type = form(this, state, declared);
        split = this.ReadSplitSince(stamp, mark);
        return type;
    }

    // DECISIONS BU-240: whether what was formed since `stamp` and `mark` read a split layer: a binding of the open combination, or a
    // formation that formed none (the one predicate of a part's split read: FormReadsSplit, ItemPartFails).
    private bool ReadSplitSince(int stamp, int mark) => this.splitReads != stamp || this.UnformedSince(mark);

    // DECISIONS BU-240: a part of a check judged in split cases starts (RestsOnSplitCases): its writes and reads count from here.
    private void EnterSplitPart()
    {
        this.splitPartMark = this.unformedWrites;
        this.splitPartStamp = this.splitReads;
    }

    // DECISIONS BU-240: a joint part starts, one that reads what every earlier part of the run gave (a solve over all the parts, a proof
    // of the whole): it is unknown where any part of the run formed none, and it is blamed on every binding the run read.
    private void EnterJointSplitPart()
    {
        this.splitPartMark = this.splitRunMark;
        this.splitPartStamp = this.splitRunStamp;
    }

    // DECISIONS BU-240: the bindings read since `stamp` are handed to every later part of the run, which reads their result (a slot or
    // an Origin an earlier part bound in its case; the Type the enclosing part formed for a comparison's parts).
    private void CarrySplitPart(int stamp) => this.splitCarried |= this.SplitReadsSince(stamp);

    // DECISIONS BU-240: whether the current part's check formed none: the part is unknown in this run (RestsOnSplitCases).
    private bool SplitPartUnknown() => this.UnformedSince(this.splitPartMark);

    // DECISIONS BU-240: the bindings of the open combination read since `stamp`, as bits of their positions.
    private ulong SplitReadsSince(int stamp)
    {
        var read = 0UL;
        for (var i = 0; i < this.splitCases.Count; i++)
        {
            read |= this.splitCases[i].Stamp > stamp ? 1UL << i : 0;
        }

        return read;
    }

    // DECISIONS BU-240: the next combination after a run that failed by itself, for a search opened from `first` (RestsOnSplitCases):
    // the most recent binding of the search in `blame` takes its next admitted case, the bindings met after it are dropped (met
    // again under their first case), and the rest of the blame is kept with it; when its cases are exhausted it is dropped and
    // the blame kept with it goes on to the most recent binding in that. False when no binding of the search is blamed.
    private bool NextSplitCase(int first, ulong blame)
    {
        var own = first >= 64 ? 0 : ulong.MaxValue << first;
        blame &= own;
        while (blame != 0)
        {
            var last = 63 - BitOperations.LeadingZeroCount(blame);
            this.splitCases.RemoveRange(last + 1, this.splitCases.Count - last - 1);
            var entry = this.splitCases[last];
            blame = (blame | entry.Blame) & ~(1UL << last) & own;
            var next = NextAdmitted(entry.Admitted, entry.Case);
            if (next >= 0)
            {
                this.splitCases[last] = (entry.Whole, entry.Admitted, next, blame, entry.Stamp);
                return true;
            }

            this.splitCases.RemoveAt(last);
        }

        return false;
    }

    // DECISIONS BU-240 (SPEC 8.1.1, 3.5.2; E3 (a), G812 ruling 103 D2 (ii)): the cases of a binding that forms no Type: the outer
    // Semantics its premises admit (ResultSemantics, as IsAbstractBinding reads it), less those a capability proven of it refutes
    // (LayerEvidence, the solver's rule: a Copy Type has no exclusive or object outer layer). Read here only: the proofs that
    // read ResultSemantics do not move (PLAN G886).
    private SemanticsMask SplitCases(BoundType whole)
    {
        var scope = this.PremiseScope(whole);
        var cases = this.ResultSemantics(whole, scope);
        if (this.RequestCapability(whole, this.Library.Copy, scope) != ConstraintProof.Proven)
        {
            return cases;
        }

        foreach (var mode in SemanticsOrder)
        {
            if (LayerEvidence(IntrinsicKind.Copy, mode.ToMask()) == ConstraintProof.Refuted)
            {
                cases &= ~mode.ToMask();
            }
        }

        return cases;
    }

    // DECISIONS BU-240: the case of the binding `whole`, which forms no Type, in the combination a check is being judged in
    // (RestsOnSplitCases), stamped as read; a binding the run meets first takes its first admitted case (SplitCases). False while
    // no check is judged, and for a binding that admits no case or is met beyond the 64 a blame holds (it stays Unformed).
    private bool InSplitCase(BoundType whole, out SemanticsKind mode)
    {
        mode = default;
        if (!this.splitOpen)
        {
            return false;
        }

        var index = this.SplitIndex(whole);
        if (index < 0)
        {
            var admitted = this.SplitCases(whole);
            index = this.SplitIndex(whole); // Its premises' proofs may have met it.
            if (index < 0)
            {
                if (NextAdmitted(admitted, -1) is not (>= 0 and var firstCase) || this.splitCases.Count >= 64)
                {
                    return false;
                }

                index = this.splitCases.Count;
                this.splitCases.Add((whole, admitted, firstCase, 0, 0));
            }
        }

        var entry = this.splitCases[index];
        this.splitCases[index] = (entry.Whole, entry.Admitted, entry.Case, entry.Blame, ++this.splitReads);
        mode = SemanticsOrder[entry.Case];
        return true;
    }

    private int SplitIndex(BoundType whole)
    {
        for (var i = 0; i < this.splitCases.Count; i++)
        {
            if (ReferenceEquals(this.splitCases[i].Whole, whole))
            {
                return i;
            }
        }

        return -1;
    }

    // DECISIONS BU-240 with rule W (BU-79): whether a formation since `mark` (an earlier unformedWrites) formed no Type, so that a check that
    // failed since then failed through the split: an Unknown, not a failure of its own.
    private bool UnformedSince(int mark) => this.unformedWrites != mark;

    // DECISIONS BU-240 with rule W (G812 rulings 141, 156): whether a check over `types` read a position a split binding decides: a formation
    // since `mark` that formed none (UnformedSince), or one of the Types holding the open formed Type's mark (HoldsMark), so that a check
    // that failed there failed through the split: an Unknown.
    private bool ReadsSplitDecided(int mark, ReadOnlySpan<BoundType?> types) => HoldsMark(types) || this.UnformedSince(mark);

    // SPEC 8.1.2: whether a pair layer formed under this binding (an annotated original, or an application `s/U`, ApplySemantics) keeps
    // its outer-Origin slot: a borrow binding does, an abstract binding or a family conditionally; a value binding keeps none. For a
    // binding that forms no Type it is unrepresented (Unformed, BU-240): the slot is kept and the candidate rests on the split.
    private bool HasOuterSlot(BoundType binding)
    {
        var isAbstract = this.IsAbstractBinding(binding);
        if (isAbstract && !HasOwnSelector(binding))
        {
            if (this.InSplitCase(binding, out var mode))
            {
                return IsBorrow(mode);
            }

            _ = this.Unformed();
        }

        return isAbstract || IsBorrow(binding.Semantics) || binding.Kind == BoundTypeKind.SemanticsAdaptation;
    }

    // Short adaptations select the output mode with the input case; a fixed mode maps every input case. The cases are the binder's
    // admitted set at the adaptation (SPEC 8.3.4, 8.15.2: a conditional member's or block's premises included), not at its declaration,
    // within a family source's own cases. A family never reaches a scope wider than where it was formed (result and parameter Types are
    // declared, SPEC 7.1, and a nested scope only adds premises), so the cases any reader selects lie within its mask.
    private BoundType AdaptationFamily(BindingSymbol pair, BoundType source, BoundOrigin? origin, BindingScope scope, SemanticsKind? fixedMode = null)
    {
        var admitted = this.AdmittedSemantics(pair.WholeType!, scope) &
            (source.Kind == BoundTypeKind.SemanticsAdaptation && ReferenceEquals(source.Symbol, pair) ? (SemanticsMask)source.Length : SemanticsMask.All);
        var children = this.RentTypes(BitOperations.PopCount((uint)admitted));
        var cases = this.adaptationCaseScratch.Rent(1);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (admitted.Contains(mode))
                {
                    cases[0] = new(pair, mode);
                    children[count++] = this.AdaptedType(fixedMode ?? mode, this.CaseType(source, cases.AsSpan(0, 1)), origin, scope);
                }
            }

            return this.TypeFamily(pair, admitted, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    // A placeholder's Owner tag is not mode evidence. A pair's owner case does establish its target's owner form.
    private bool CanSelectIdentity(BoundType type, BindingScope scope, ReadOnlySpan<PairCase> cases = default)
        => !type.IsAbstract || this.TargetCases(type, scope, cases) == SemanticsMask.Owner || this.ResultSemantics(type, scope) == SemanticsMask.Owner;

    // A family is interned only when no pair application has its cases: SPEC 13.5.1's `p@s` abbreviates `p@s/U`, whose Type is the
    // application `s/U` (8.2.3: one Type, one representation), so a case list that one application's cases (CaseType) give is that
    // application, as a list of identical cases is that case (TyCtxt.Intern).
    private BoundType TypeFamily(BindingSymbol pair, SemanticsMask admitted, ReadOnlySpan<BoundType> children)
        => this.FamilyApplication(pair, admitted, children) ??
            this.types.Intern(BoundTypeKind.SemanticsAdaptation, pair, SemanticsKind.Parameter, children, length: (long)admitted);

    // The pair application `s/U` (with the outer Origin its borrow cases state) whose case Types over the family's mask are `children` in
    // SemanticsOrder, or null. `U` and the Origin are read off a layer case; the owner case is `U` itself. The mask is the binder's
    // admitted set where the family is formed, and no reader of it is in a wider scope (DECISIONS BU-172), so only the cases compare.
    private BoundType? FamilyApplication(BindingSymbol pair, SemanticsMask admitted, ReadOnlySpan<BoundType> children)
    {
        // One case stays that case (TyCtxt.Intern), the concrete Type its application's determined head forms (SPEC 8.2.3).
        if (children.Length < 2 || pair.WholeType is not { } whole)
        {
            return null;
        }

        BoundType? target = null;
        BoundOrigin? origin = null;
        var count = 0;
        foreach (var mode in SemanticsOrder)
        {
            if (admitted.Contains(mode))
            {
                var child = children[count++];
                if (mode == SemanticsKind.Owner)
                {
                    target ??= child;
                }
                else if (child is { Kind: BoundTypeKind.Semantics, Components: [var below] } && child.Semantics == mode)
                {
                    target ??= below;
                    origin ??= IsBorrow(mode) ? child.Origin : null;
                }
            }
        }

        if (target is null)
        {
            return null;
        }

        var application = this.PairApplication(pair, target, origin);
        count = 0;
        foreach (var mode in SemanticsOrder)
        {
            if (admitted.Contains(mode))
            {
                ReadOnlySpan<PairCase> selected = [new(pair, mode)];
                if (!ReferenceEquals(this.CaseType(application, selected), children[count++]))
                {
                    return null;
                }
            }
        }

        return application;
    }

    // Only Apply forms nothing (null), for a case whose binding forms no Type; Target and Origin map every case.
    private BoundType? TransformFamily(BoundType family, FamilyTransform operation, BoundOrigin? origin = null, BoundType? target = null)
    {
        var children = this.RentTypes(family.Components.Count);
        var cases = this.adaptationCaseScratch.Rent(1);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (((SemanticsMask)family.Length).Contains(mode))
                {
                    var child = family.Components[count];
                    cases[0] = new(family.Symbol!, mode);
                    var formed = operation switch
                    {
                        FamilyTransform.Target => this.DirectTarget(child),
                        FamilyTransform.Origin => IsBorrow(child.Semantics) || child.IsAbstract ? this.WithOrigins(child, origin, (BoundOrigin[])child.OriginArguments) : child,
                        // A pair target alone whose case is owner is `W` itself (TargetCases): the case forms the target itself.
                        _ when child.IsAbstractType && this.TargetCases(child, this.PremiseScope(child), cases.AsSpan(0, 1)) == SemanticsMask.Owner
                            => this.CaseType(target!, cases.AsSpan(0, 1)),
                        _ => this.ApplySemantics(child, this.CaseType(target!, cases.AsSpan(0, 1)), origin),
                    };
                    if (formed is null)
                    {
                        return null; // A case whose binding forms no Type (ApplySemantics): the application forms none either.
                    }

                    children[count++] = formed;
                }
            }

            return this.TypeFamily(family.Symbol!, (SemanticsMask)family.Length, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    // SPEC 8.1.2: `s/U`, a pair binder's binding `whole` applied to `target`: a family case by case, an abstract binding's application, and
    // a layer the binding's Semantics forms over the target, read through TyCtxt.PairBinding (DECISIONS BU-46): never its Origin, but a
    // borrow states `origin`, the annotation's, and a raw layer what the binding states (none, or a declaration's Unstated); an owner
    // binding gives the target. An abstract binding without a selector of its own (HasOwnSelector) forms none (Unformed), or the layer
    // of its case in the combination a check is judged in (InSplitCase).
    private BoundType? ApplySemantics(BoundType whole, BoundType target, BoundOrigin? origin) => whole switch
    {
        { Kind: BoundTypeKind.SemanticsAdaptation } => this.TransformFamily(whole, FamilyTransform.Apply, origin, target: target),
        _ when this.IsAbstractBinding(whole) => HasOwnSelector(whole) ? this.PairApplication(whole.Symbol!, target, origin)
            : this.InSplitCase(whole, out var mode) ? this.Form(mode, target, origin) : this.Unformed(),
        { Kind: BoundTypeKind.Semantics } => this.types.Rebuild(this.types.PairBinding(whole), [target], IsBorrow(whole.Semantics) ? origin : null, []),
        _ => this.Form(whole.Semantics, target, origin),
    };

    // SPEC 8.1.2: a pair's `s/U` for an abstract binding: the pair's own target gives its original, with the occurrence's slot where it
    // is not the pair's `o` (AnnotatedOriginal); any other target an application, a plain type parameter's binding (no whole) included.
    // ApplySemantics and FamilyApplication read it.
    private BoundType PairApplication(BindingSymbol pair, BoundType target, BoundOrigin? origin)
        => pair.WholeType is { } original && ReferenceEquals(target, pair.Type)
            ? this.AnnotatedOriginal(original, ReferenceEquals(origin, this.PairOuterOrigin(pair)) ? null : origin)
            : this.types.Intern(BoundTypeKind.SemanticsApplication, pair, SemanticsKind.Parameter, [target], origin: origin);

    // SPEC 8.1.2: an occurrence of a pair's original under its binding, annotated with `origin` (`s/T during b`): the binding with that
    // outer-Origin slot where it keeps one (HasOuterSlot); a value binding keeps none. Substitution and PairApplication read it.
    private BoundType AnnotatedOriginal(BoundType binding, BoundOrigin? origin)
        => origin is not null && this.HasOuterSlot(binding) ? this.WithOrigins(binding, origin, (BoundOrigin[])binding.OriginArguments) : binding;

    private BoundType? SubstituteFamilyArgument(BoundType family, in CallSubstitution substitution, int slot, BoundType whole)
    {
        // A slot the slot owner's column does not bind is the declaring Type's (CallSubstitution.Slot), whose occurrence then takes each case in turn.
        var declaring = substitution.OwnsSlot(ContainerSlot(substitution.SlotOwner, family.Symbol!)) ? null : substitution.Declaring;
        var slots = declaring is null ? substitution.Types : (BoundType[])declaring.Components;
        var children = this.RentTypes(whole.Components.Count);
        var caseArguments = this.typeScratch.Rent(slots.Length);
        var cases = this.adaptationCaseScratch.Rent(1);
        slots.CopyTo(caseArguments);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (((SemanticsMask)whole.Length).Contains(mode))
                {
                    cases[0] = new(whole.Symbol!, mode);
                    caseArguments[slot] = whole.Components[count];
                    var caseSlots = caseArguments.AsSpan(0, slots.Length);
                    var caseSubstitution = declaring is null ? substitution with { Types = caseSlots }
                        : substitution with { Declaring = this.types.Rebuild(declaring, caseSlots!, declaring.Origin, (BoundOrigin[])declaring.OriginArguments) };
                    if (this.Substitute(family, caseSubstitution) is not { } child)
                    {
                        return null;
                    }

                    children[count++] = this.CaseType(child, cases.AsSpan(0, 1));
                }
            }

            return this.TypeFamily(whole.Symbol!, (SemanticsMask)whole.Length, children.AsSpan(0, count));
        }
        finally
        {
            this.adaptationCaseScratch.Return(cases, clearArray: true);
            this.typeScratch.Return(caseArguments, clearArray: true);
            this.typeScratch.Return(children, clearArray: true);
        }
    }

    private BoundType? SubstituteFamily(BoundType family, BoundType whole)
    {
        if (this.IsAbstractBinding(whole))
        {
            if (!HasOwnSelector(whole))
            {
                return this.Unformed();
            }

            if (whole.Symbol is not { } selector)
            {
                return null;
            }

            var admitted = this.ResultSemantics(whole, this.PremiseScope(whole)) & (SemanticsMask)family.Length;
            var children = this.RentTypes(BitOperations.PopCount((uint)admitted));
            try
            {
                var count = 0;
                foreach (var mode in SemanticsOrder)
                {
                    if (admitted.Contains(mode))
                    {
                        children[count++] = FamilyCase(family, mode)!;
                    }
                }

                return this.TypeFamily(selector, admitted, children.AsSpan(0, count));
            }
            finally
            {
                this.typeScratch.Return(children, clearArray: true);
            }
        }

        return FamilyCase(family, whole.Semantics);
    }
}
