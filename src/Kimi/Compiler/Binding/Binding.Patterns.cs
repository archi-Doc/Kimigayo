// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly MatchRows matches;

    // A match keeps its plan object across rebinds, so a retained plan never starts describing another match while its own syntax
    // is still selected; a removed match's plan leaves with its row at renumbering, and a live match the pass did not index drops its
    // plan (PruneMatchPlans).
    private readonly MatchPlanRows matchPlans;
    private readonly IndexedPatternRows indexedPatterns;
    private readonly List<PatternWarning> patternWarnings = new();
    private readonly List<BoundType?> payloadReads = new(); // BindPattern's Case payload Types, null where not read: a stack of runs.
    private PatternMarker? patternMarker;
    private UnsupportedMarker? unsupportedMarker;

    /// <summary>Gets warnings without changing Binding validity.</summary>
    public IReadOnlyList<PatternWarning> PatternWarnings => this.patternWarnings;

    /// <summary>Gets the current positional Pattern and coverage plan, including Invalid/Pending results.</summary>
    /// <param name="match">The selected match syntax.</param>
    /// <param name="plan">The current plan, or null if the syntax was replaced.</param>
    /// <returns>Whether the current Binding pass indexed this match.</returns>
    public bool TryGetMatch(MatchKoto match, out BoundMatch? plan) => this.matches.TryGetValue(match, out plan);

    // A pattern's syntax node no check typed has the Unit Type (PatternMarker's rule; also the Case Name a member step settles).
    private static void CompletePatternSyntax(Koto node) => Complete(node, BoundType.Unit);

    private void ResetMatches()
    {
        // The pass counter already left the last pass's matches stale, so every kept plan is reset: the last pass's, the same objects, and
        // a pass's that threw before PruneMatchPlans; Reset(null) sets fixed values, so the extra resets change nothing.
        foreach (var plan in this.matchPlans.Values.In(this.pass))
        {
            plan!.Reset(null);
        }

        // The pattern nodes and guards the pass's previous run indexed, which its prune steps compare with this run's.
        for (var rows = this.indexedPatterns.Values.In(this.pass); rows.MoveNext();)
        {
            if (rows.Current.Guard)
            {
                this.pass.Part.PreviousCandidateScopes.Add(rows.Node);
            }

            if (rows.Current.Pattern)
            {
                this.pass.Part.PreviousPatternNodes.Add(rows.Node);
            }
        }

        this.indexedPatterns.Empty();
        this.patternWarnings.Clear();
    }

    private void PruneCandidateScopes()
    {
        foreach (var guard in this.pass.Part.PreviousCandidateScopes)
        {
            if (!this.indexedPatterns[guard].Guard)
            {
                this.scopes.Remove(guard);
            }
        }

        this.pass.Part.PreviousCandidateScopes.Clear();
    }

    private void PrunePatternScopes()
    {
        foreach (var node in this.pass.Part.PreviousPatternNodes)
        {
            if (this.indexedPatterns[node] is (false, false))
            {
                this.scopes.Remove(node);
                this.symbols.Remove(node);
            }
        }

        this.pass.Part.PreviousPatternNodes.Clear();
    }

    private void IndexMatch(MatchKoto match)
    {
        if (!this.matchPlans.TryGetValue(match, out var plan))
        {
            plan = new();
            this.matchPlans.Add(match, plan);
        }

        plan.Reset(match);
        this.matches.Add(match, plan);
    }

    // A live match of the pass's modules that the pass did not index drops its plan (no corpus program or Binding test has one: PLAN
    // G949 S1-r2's N3); a removed match's plan already left with its row at renumbering.
    private void PruneMatchPlans()
    {
        for (var plans = this.matchPlans.Values.In(this.pass); plans.MoveNext();)
        {
            var match = (MatchKoto)plans.Node;
            if (!this.matches.ContainsKey(match))
            {
                this.matchPlans.Remove(match);
            }
        }
    }

    // A pattern left unbound rests on the failure that left it so (PatternMarker): the failed Subject `failure`, when there is one, or
    // the payload Pattern of its Case whose Type was not read (BindPattern's Case leaf).
    private void MarkPatternTree(Koto pattern, Koto? failure)
        => (this.patternMarker ??= new(this)).Mark(pattern, failure);

    /// <summary>Marks an unbound guard/body so later analyses see unknown Types instead of a fabricated Unit.</summary>
    private void MarkUnsupportedTree(Koto node)
        => (this.unsupportedMarker ??= new()).Visit(node);

    // SPEC 23.1.2: a pattern node no position bound, or no position holds (BindPattern's Case leaf), rests on the failure above it; one
    // that failed on its own keeps that failure and its record. A binding it declares, for the body or as a guard candidate, has no
    // Type, as a local rests on a failed initializer, so a use of it consults this declaration (ConsultTypeSource); its name, if bound,
    // rests with it.
    private void RestOnFailure(Koto node, Koto failure)
    {
        if (node is SyntaxFormKoto { Akind: KotoKind.BindingPattern, BoundSymbol: { } symbol } binding)
        {
            symbol.Type = null;
            if (this.symbols.TryGetValue(binding.Operands[0], out var candidate) && candidate.Kind == BindingSymbolKind.PatternCandidate)
            {
                candidate.Type = null;
            }

            if (binding.Operands[0].BindingState == BindingState.Resolved)
            {
                this.CompleteDependent(binding.Operands[0], binding);
            }
        }

        if (this.RecordedFailure(node) == BindingFailure.None)
        {
            this.CompleteDependent(node, failure);
        }
    }

    private BoundType? BindMatch(MatchKoto match, BindingScope scope, BoundType? expected)
    {
        // An Unresolved match is bound again, such as when a use rebinds the declaration it initializes: the plan is this binding's.
        var plan = this.matches[match];
        plan.Reset(match);
        var subject = this.BindNode(match.Expression, scope);
        if (match is TryKoto propagation)
        {
            if (subject is null)
            {
                // SPEC 23.1.2: a failed operand is the try's prerequisite; its Type selects the try's arms.
                return this.Fail(match, BindingFailure.Unsupported, true);
            }

            if (subject.Kind != BoundTypeKind.Constructed || subject.Semantics != SemanticsKind.Owner || (subject.Symbol != this.Library.Option && subject.Symbol != this.Library.Result))
            {
                return this.Fail(match, BindingFailure.TypeMismatch);
            }

            // SPEC 17.3: the operand is acquired as an owned value (a Copy Place copied, a non-Copy Place moved, a temporary
            // transferred), so the try's Subject is by value and its success payload a part of it (OwnedPart; R7-C6 a, the Middle's
            // C-520 C1: D2's rule for a transferred Subject).
            plan.Mode = SubjectMode.ByValue;
            propagation.SelectOption(subject.Symbol == this.Library.Option);
            if (!propagation.SemanticsIndexed)
            {
                this.IndexMatchArms(propagation, scope);
                propagation.SemanticsIndexed = true;
            }

            expected = subject.Components[0];
        }
        else
        {
            subject = this.AcquireSubject(match.Expression, subject, out var borrow, out var mode);
            plan.SubjectBorrow = borrow;
            plan.Mode = mode;
            if (this.FormsBorrows)
            {
                this.RecordBorrow(this.borrows, borrow); // Its writer forms it, before the arms bind (Binding.RawOrigins.Borrows).
            }
        }

        // SPEC 23.1.2 (rule W, PLAN G1321): the plan judges the Subject Type's formation once, at its root position (the first arm's
        // Pattern; a `try`'s first synthesized Pattern, whose span is the `try`'s: never the `try` or its operand, which keep their own
        // records), and every other arm rests on that record: each arm's Pattern reads the same Subject Type, and a part a Pattern
        // selects is the Subject's under its arguments, so no other position judges a Type.
        var root = match.Arms.Count == 0 ? null : KotoHelper.UnwrapParentheses(match.Arms[0].Pattern);
        var failed = subject is null ? match.Expression : root is not null && !this.ValidatePatternType(plan, root, subject, scope) ? root : null;
        var resultContext = this.BeginResult(match, scope, expected, deferEvidence: true);
        for (var i = 0; i < match.Arms.Count; i++)
        {
            // SPEC 23.1.2: a failed Subject, or Subject Type, is the prerequisite of an arm's positions and bindings, never of its guard
            // or body.
            var arm = match.Arms[i];
            if (subject is not null && failed is null)
            {
                // SPEC 15.1.6: the Subject Place of a ByValue Subject is owned by the match; a bare Place's is a borrow of it.
                var reach = new PatternReach(PatternAccessMode.Owned, null, OwnedPart: plan.Mode == SubjectMode.ByValue);
                plan.ArmStorage.Add(new(arm, this.BindPattern(arm.Pattern, subject, scope, plan, -1, 0, reach)));
            }
            else
            {
                // Its Pattern is a resting leaf, which matched no Type: it selects nothing, and its bindings rest on the failure.
                plan.ArmStorage.Add(new(arm, plan.PositionStorage.Count));
                plan.PositionStorage.Add(new(arm.Pattern, null, BoundPatternKind.Invalid, -1, 0, plan.PositionStorage.Count + 1));
            }

            this.MarkPatternTree(arm.Pattern, failed);
        }

        this.AcquireReferenceBindings(plan); // Before the guards and bodies, which state the bindings' atoms.

        // Pattern bindings must have their Subject Types before collecting result evidence.
        // Otherwise an unfitted literal arm could default to f64 before seeing an f32 binding.
        if (resultContext.Expected is null)
        {
            this.InferResultExpected(match, scope, resultContext);
        }

        this.CalculateMatchCoverage(plan, subject);

        var required = IsResultRequiringSelection(match);
        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            if (plan.Pending)
            {
                // Implicit shared inspection needs candidate/body Loan semantics before
                // the ordinary expression binder may use these Pattern bindings.
                if (arm.Guard is { } pendingGuard)
                {
                    this.MarkUnsupportedTree(pendingGuard);
                    this.Fail(pendingGuard, BindingFailure.Unsupported, true);
                }

                this.MarkUnsupportedTree(arm.Body);
                this.Fail(arm.Body, BindingFailure.Unsupported, true);
                continue;
            }

            if (arm.Guard is { } guard && this.BindNode(guard, this.scopes[guard], BoundType.Boolean) is { } guardType &&
                !this.FitsType(guardType, BoundType.Boolean))
            {
                this.RecordTypeMismatch(guard, guard, guardType, BoundType.Boolean); // SPEC 14.8.3: a guard is one bool expression.
                plan.Invalid = true;
            }

            this.BindNode(arm.Body, this.scopes[arm.Pattern], required ? resultContext.Expected : null);
        }

        if (plan.Coverage.State == MatchCoverageState.NonExhaustive)
        {
            // Keep the match unavailable until late Type validation decides the diagnostic.
            return Complete(match, null);
        }

        // SPEC 14.8, 14.9.2, 23.1.2: the match's own requirements are its coverage and its result join, as an if's, a do's and a loop's
        // are their joins (FinishResult). A failed Subject, a failed Pattern position, a failed guard and a failed arm body are those
        // parts' own failures, each with its record or resting on one: the join reads the arm bodies and the expected Type, never the
        // Subject or the Patterns, and a failed body is one failed source of it. A Pending plan's guards and bodies are not bound.
        if (plan.Pending)
        {
            return this.Fail(match, BindingFailure.Unsupported, true);
        }

        return this.FinishResult(match, resultContext);
    }

    // SPEC 15.1.6 subject rule: the Subject mode is the access that the Subject Place grants to its first layer that
    // is not a safe reference. Reference layers that are all uniq/objuniq give Exclusive, and a ref/objref layer, or a
    // shared layer on the path of a bare Place, bounds the mode to Shared; a qualifying pair layer (LayerBelow) gives the weakest
    // mode of its admitted cases, Shared when ref is admitted (SPEC 13.5.5). A bare Place (borrowed in place) is Shared,
    // and any other acquisition, including @move and a temporary, yields an owned ByValue Subject.
    private SubjectMode SubjectModeOf(Koto expression, BoundType? type)
    {
        var bare = this.IsBarePlace(expression);
        if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq, Components.Count: 1 })
        {
            return bare ? SubjectMode.Shared : SubjectMode.ByValue;
        }

        if (bare && this.ReachedThroughShared(expression))
        {
            return SubjectMode.Shared;
        }

        BindingScope? scope = null;
        for (var layer = type; layer is not null;)
        {
            if (layer is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef, Components.Count: 1 } ||
                (TryPairLayer(layer, out _, out _) && ((this.FollowablePair(layer, scope ??= this.ConstraintScope(expression), out _) ?? SemanticsMask.None) & SemanticsMask.Ref) != 0))
            {
                return SubjectMode.Shared;
            }

            layer = layer is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.ObjUniq, Components: [var view] } ? view
                : TryPairLayer(layer, out _, out _) || layer.Semantics == SemanticsKind.Uniq ? this.LayerBelow(layer, scope ??= this.ConstraintScope(expression)) : null;
        }

        return SubjectMode.Exclusive;
    }

    // SPEC 15.1.6 subject rule: the Subject is acquired as written, except that a bare Place is borrowed in place: a
    // stored uniq/objuniq reference is Reborrowed exclusively, a stored ref/objref reference is Copied, and any other
    // stored value is shared-borrowed.
    private BoundType? AcquireSubject(Koto expression, BoundType? type, out BoundType? borrow, out SubjectMode mode)
    {
        borrow = null;
        mode = this.SubjectModeOf(expression, type);
        if (type is null || ReferenceEquals(type, BoundType.Never) || !this.IsBarePlace(expression))
        {
            return type;
        }

        if (mode == SubjectMode.Exclusive)
        {
            // SPEC 15.1.6, 15.6.3: the stored exclusive reference's Reborrow, contained in it and in the layers reaching the Subject.
            borrow = this.types.Rebuild(type, [type.Components[0]], this.ReferentOrigin(expression, type), []);
            return borrow;
        }

        if (this.PairSubject(expression, type, this.ConstraintScope(expression)) is { } followed)
        {
            // SPEC 15.1.6: a bare pair-layer Place is followed like a borrow value in the weakest admitted mode, as for `for`.
            mode = followed.Semantics == SemanticsKind.Uniq ? SubjectMode.Exclusive : SubjectMode.Shared;
            borrow = followed;
            return followed;
        }

        if (type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq)
        {
            // SPEC 15.1.6, 15.6.2: a stored exclusive reference reached through a shared layer is shared-Reborrowed.
            var origin = this.ReferentOrigin(expression, type);
            var semantics = type.Semantics == SemanticsKind.Uniq ? SemanticsKind.Ref : SemanticsKind.ObjRef;
            borrow = this.types.Intern(BoundTypeKind.Semantics, null, semantics, [type.Components[0]], origin: origin);
            return borrow;
        }

        if (type.Semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc)
        {
            borrow = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [type], origin: this.PlaceOrigin(expression));
            return borrow;
        }

        return type;
    }

    private int BindPattern(Koto syntax, BoundType matched, BindingScope outer, BoundMatch plan, int parent, int element, PatternReach reach)
    {
        if (syntax is ParenthesizedKoto grouping)
        {
            var grouped = this.BindPattern(grouping.Operand, matched, outer, plan, parent, element, reach);
            Complete(grouping, matched);
            return grouped;
        }

        var index = plan.PositionStorage.Count;
        plan.PositionStorage.Add(new(syntax, matched, BoundPatternKind.Invalid, parent, element, index + 1));
        var position = plan.PositionStorage[index];
        if (syntax.BindingState == BindingState.Invalid)
        {
            // A position that failed before the plan, such as a repeated binding name, is a part the match consulted.
            plan.Invalid = true;
            this.Consulted(syntax);
        }

        // SPEC 14.8.1: a Wildcard (and a grouping, bound as its operand) selects nothing.
        var wildcard = syntax is IdentifierNameKoto { IdentifierName: "_" };
        var structural = !wildcard && syntax is not SyntaxFormKoto { Akind: KotoKind.BindingPattern };
        var layers = 0;
        var type = wildcard ? matched : this.SelectLayers(plan, matched, structural, outer, ref reach, out layers);
        var unsupported = false;

        if (structural && type.Semantics != SemanticsKind.Owner)
        {
            plan.Invalid = true;
            this.Record(syntax, BindingFailure.InvalidPattern, []);
            return index;
        }

        switch (syntax)
        {
            case IdentifierNameKoto { IdentifierName: "_" }:
                position = position with { Kind = BoundPatternKind.Wildcard, WholePosition = true };
                break;
            case SyntaxFormKoto { Akind: KotoKind.BindingPattern, BoundSymbol: { } symbol } binding:
                // SPEC 15.1.6: a whole-Subject binding of a reference-valued Subject copies that reference and never selects the
                // internal slot. Exclusive access to an owned part of the Subject lends the Subject Place itself, which a getter result
                // cannot lend exclusively (SPEC 15.1.5, 11.5): the binding is an error, never a shared binding.
                symbol.Type = this.BindingTypeOf(type, reach, binding);
                symbol.BindsReference = reach.Access != PatternAccessMode.Owned || (parent < 0 && plan.Mode != SubjectMode.ByValue);
                if (reach is { Access: PatternAccessMode.Exclusive, OwnedPart: true } && IsGetterResult(plan.Syntax.Expression))
                {
                    this.Record(binding, BindingFailure.InvalidAssignment, []);
                }

                binding.Operands[0].BoundSymbol = symbol;
                Complete(binding.Operands[0], matched);
                position = position with { Kind = BoundPatternKind.Binding, BodySymbol = symbol, WholePosition = true };
                if (this.symbols.TryGetValue(binding.Operands[0], out var candidate) && candidate.Kind == BindingSymbolKind.PatternCandidate)
                {
                    // SPEC 14.8.3: a guard candidate is a shared reference to its Place, the one the body binding takes, whatever the mode
                    // and Copy capability.
                    var candidateOrigin = this.OriginAtom(CandidateOriginBinder(candidate), OriginKind.Projection, candidate.Slot);
                    candidate.Type = parent < 0 && matched?.SafeReferent is { } matchedReferent
                        ? this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [matchedReferent], origin: matched.Origin ?? candidateOrigin)
                        : this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [type], origin: candidateOrigin);
                    position = position with { CandidateSymbol = candidate };
                }

                unsupported |= symbol.Type is null;
                break;
            case UnitLiteralKoto when ReferenceEquals(type, BoundType.Unit):
                position = position with { Kind = BoundPatternKind.Unit, WholePosition = true };
                break;
            case SyntaxFormKoto { Akind: KotoKind.TuplePattern } tuple when type.Kind == BoundTypeKind.Tuple && tuple.Operands.Length == type.Components.Count:
                position = position with { Kind = BoundPatternKind.Tuple, WholePosition = true };
                for (var i = 0; i < tuple.Operands.Length; i++)
                {
                    var child = this.BindPattern(tuple.Operands[i], type.Components[i], outer, plan, index, i, reach);
                    position = position with { WholePosition = position.WholePosition && plan.PositionStorage[child].WholePosition };
                }

                break;
            case SyntaxFormKoto { Akind: KotoKind.CasePattern } enumeration when type.Symbol?.Declaration is EnumKoto && type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed:
                var selected = this.ResolvePatternCase(enumeration.Operands[0], type, outer);
                var children = enumeration.Operands.Length == 2 ? ((SyntaxFormKoto)enumeration.Operands[1]).Operands : default;
                if (selected is null || selected.Symbol.Declaration.BindingState == BindingState.Invalid || selected.Owner.Declaration.BindingState == BindingState.Invalid ||
                    selected.Payload.Length != children.Length || (selected.Payload.Length == 0) != (enumeration.Operands.Length == 1))
                {
                    break;
                }

                syntax.BoundSymbol = selected.Symbol;

                // SPEC 6.5.2, 14.8.1: every payload's Type is read at the position first (payloadReads: a nested Case's run lies above its
                // parent's), then each read payload's Pattern is bound against it, making its own checks.
                var first = this.payloadReads.Count;
                Koto? unread = null;
                for (var i = 0; i < children.Length; i++)
                {
                    var payload = this.PartTypeIn(selected.Payload[i], type, outer) is { } stored ? this.PayloadRead(stored, plan, reach, children[i]) : null;
                    this.payloadReads.Add(payload);
                    if (payload is null)
                    {
                        plan.Invalid = true;
                        this.Record(children[i], BindingFailure.InvalidPattern, []);
                        unread ??= children[i];
                    }
                }

                for (var i = 0; i < children.Length; i++)
                {
                    if (this.payloadReads[first + i] is { } payload)
                    {
                        this.BindPattern(children[i], payload, outer, plan, index, i, reach);
                    }
                }

                this.payloadReads.RemoveRange(first, children.Length);
                if (unread is null)
                {
                    position = position with { Kind = BoundPatternKind.Case, Case = selected };
                    break;
                }

                // SPEC 14.8.1, 23.1.2: a Case position selects its Case's payload layout, so it stands only where every payload's Type is
                // read; else it stays the Invalid leaf opened above, which records nothing of its own. The positions the read payloads'
                // Patterns formed leave it, each resting on the first unread payload Pattern unless it failed on its own (RestOnFailure),
                // and the Case Pattern with what no position bound rests on it too (PatternMarker).
                for (var i = index + 1; i < plan.PositionStorage.Count; i++)
                {
                    this.RestOnFailure(plan.PositionStorage[i].Source, unread);
                }

                plan.PositionStorage.RemoveRange(index + 1, plan.PositionStorage.Count - index - 1);
                this.MarkPatternTree(syntax, unread);
                return index;
            default:
                if (this.TryPatternLiteral(syntax, type, outer, out var literal))
                {
                    position = position with { Kind = BoundPatternKind.Literal, Literal = literal };
                }

                break;
        }

        position = position with
        {
            End = plan.PositionStorage.Count,
            AccessMode = reach.Access,
            ImplicitFollows = layers,
            OwnedPart = reach.OwnedPart,
        };
        plan.PositionStorage[index] = position;
        if (position.Kind == BoundPatternKind.Invalid)
        {
            plan.Invalid = true;
            this.Record(syntax, BindingFailure.InvalidPattern, []);
        }
        else if (unsupported)
        {
            plan.Pending = true;
            this.Fail(syntax, BindingFailure.Unsupported, true);
        }
        else
        {
            Complete(syntax, matched);
        }

        return index;
    }

    private BoundEnumCase? ResolvePatternCase(Koto reference, BoundType expected, BindingScope scope)
    {
        if (reference is SyntaxFormKoto { Akind: KotoKind.InferredCase } inferred)
        {
            return this.InferredCase(inferred, scope, expected)?.EnumCase;
        }

        if (reference is not MemberAccessKoto member || TypeSpelling(member.Right) is not { } name)
        {
            return null;
        }

        var qualifier = member.Left;
        var definition = this.TypeName(qualifier, scope); // A qualifier's row (SPEC 9.6), as its path walk publishes it.
        if (definition is null || !ReferenceEquals(definition.Named, expected.Symbol))
        {
            return null;
        }

        var qualified = this.EnumQualifierType(qualifier, definition, scope, expected);
        if (qualified is null || qualified.Components.Count != expected.Components.Count)
        {
            return null;
        }

        for (var i = 0; i < qualified.Components.Count; i++)
        {
            if (!ReferenceEquals(qualified.Components[i], expected.Components[i]))
            {
                return null;
            }
        }

        var selection = this.LookupMember(expected, name, scope, LookupRole.EnumCase);
        if (selection.Member is not { } symbol)
        {
            return null;
        }

        // SPEC 9.6, 14.8.1: the Case is the member access's selection, published by PublishPath's MemberAccess step (the Type side, the
        // enum qualifier). The step settles the Name, so PatternMarker, which types every pattern node no check typed, would not reach
        // it: the pattern types its Case Name first, by the marker's own rule.
        CompletePatternSyntax(member.Right);
        this.PublishMemberAccess(member, symbol, definition, LookupRole.Qualifier, selection);
        return symbol.EnumCase;
    }

    private bool TryPatternLiteral(Koto syntax, BoundType type, BindingScope scope, out PatternLiteral literal)
    {
        literal = default;
        // A number or a negated number; TryDictionaryLiteralKey unwraps a key's unary + before this.
        var negative = syntax.Akind == KotoKind.PrefixMinus;
        if ((negative ? ((UnaryKoto)syntax).Operand : syntax) is NumberLiteralKoto number)
        {
            if (!this.TryIntegerLiteral(number, negative, type, scope, out var bits))
            {
                return false;
            }

            literal = new(PatternLiteralKind.Integer, bits);
            Complete(number, type);
            return true;
        }

        switch (syntax)
        {
            case BoolLiteralKoto boolean when ReferenceEquals(type, BoundType.Boolean):
                literal = new(PatternLiteralKind.Boolean, boolean.Value ? 1 : 0);
                return true;
            case CharLiteralKoto { Value: { } scalar } when ReferenceEquals(type, BoundType.Char):
                literal = new(PatternLiteralKind.Character, scalar.Value);
                return true;
            case StringLiteralKoto text when ReferenceEquals(type, BoundType.String):
                literal = new(PatternLiteralKind.String, Text: text.Literal);
                return true;
            default:
                return false;
        }
    }

    // The plan's one judgment of its Subject Type's formation, at its root position `syntax` (BindMatch; its recheck,
    // CompletePatternAcquisitions): a failure is recorded there, once, through the constraint family's recorder.
    private bool ValidatePatternType(BoundMatch plan, Koto syntax, BoundType type, BindingScope scope)
    {
        var proof = this.CheckTypeConstraints(type, scope, syntax);
        if (proof == ConstraintProof.Proven)
        {
            return true;
        }

        plan.Invalid |= proof != ConstraintProof.Unknown;
        plan.Pending |= proof == ConstraintProof.Unknown;
        this.RecordConstraint(syntax, proof, [], proof == ConstraintProof.Unknown); // The recheck meets a node its first check failed: the read keeps it.
        return false;
    }

    private void CompletePatternAcquisitions()
    {
        this.PatternContainmentWork = 0;
        // D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id (the walk's order on a cold compile).
        foreach (var row in this.matches.Values.In(this.pass))
        {
            // The root's recheck after late declaration/constraint validation, including discards (BindMatch's judgment, once); a resting
            // root matched no Type to recheck. A failed one leaves each arm's root-level Binding, which reads the Subject Type, unacquired
            // (one reached through a reference layer keeps BindMatch's Borrow, AcquireReferenceBindings); a position below selects a part,
            // acquired without a proof (AcquireBinding).
            var plan = row!;
            var failed = plan.PositionStorage.Count != 0 && plan.PositionStorage[0].MatchedType is { } subject &&
                !this.ValidatePatternType(plan, plan.PositionStorage[0].Source, subject, this.ConstraintScope(plan.Syntax));
            if (failed)
            {
                plan.Coverage = new(plan.Invalid ? MatchCoverageState.Invalid : MatchCoverageState.Pending);
            }

            for (var i = 0; i < plan.PositionStorage.Count; i++)
            {
                if (plan.PositionStorage[i] is { Kind: BoundPatternKind.Binding } position && !(failed && position.Parent < 0))
                {
                    this.AcquireBinding(plan, i);
                }
            }

            if (plan.Coverage.State == MatchCoverageState.NonExhaustive)
            {
                // SPEC 14.8.4: the reason a match lacks as a Reason, its Case's declaration index plus one packed above its byte (0 for
                // none; R7-E3 E3c-2). B1 none: this runs once, after the positions' checks, which fail positions, never the match.
                var coverage = plan.Coverage;
                this.Record(plan.Syntax, BindingFailure.NonExhaustiveMatch, [new(ProblemFactKind.Reason, SourceRef.Of(plan.Syntax), (long)coverage.Requirement | ((long)(coverage.CaseIndex + 1) << 8))]);
            }

            // Publish coverage warnings only after all Pattern Type checks have completed.
            if (plan.Coverage.State is MatchCoverageState.Exhaustive or MatchCoverageState.NonExhaustive)
            {
                this.WarnContainedPatterns(plan);
            }
        }
    }

    // SPEC 15.1.6, 3.5.2: the one acquisition rule of a Pattern binding, a match arm's or a for loop's item (SPEC 14.6.2;
    // CompleteIterationAcquisitions): a binding reached through a reference layer borrows its Place; a part of an owned Subject or
    // iteration item (OwnedPart: `match o@move`, a temporary Subject, every loop item) is transferred with its responsibility ("Payload
    // Copy capability changes no binding Type"), so it Moves and no Copy is asked (R7-C6 a, F's C8 ruling 24: a query is asked only
    // where a judgment needs its answer). The one binding left with Owned access, a whole-Subject binding of a Subject borrowed in place
    // or of a reference-typed temporary, has a reference layer's Type, whose Copy its Semantics decides (SPEC 3.5.2): Copied for `ref/`,
    // `objref/` and raw, Moved (the Reborrow) for `uniq/` and `objuniq/`; its plan's Subject Type passed ValidatePatternType's recheck,
    // so any other verdict there is a broken contract (F's C8 ruling 27, amending R4-M179). Its plan stays buildable and its coverage
    // stays the Patterns' (Invalid means a located Pattern or guard failure; SPEC 14.8.4, 23.1.2).
    private void AcquireBinding(BoundMatch plan, int index)
    {
        var position = plan.PositionStorage[index];
        var acquisition = position.AccessMode != PatternAccessMode.Owned ? PatternAcquisition.Borrow
            : position.OwnedPart ? PatternAcquisition.Move
            : this.ProveCopy(position.MatchedType!, position.Source) switch
            {
                ConstraintProof.Proven => PatternAcquisition.Copy,
                ConstraintProof.Refuted => PatternAcquisition.Move,
                var other => throw new InvalidOperationException($"A whole-Subject binding's reference layer has a {other} Copy proof (F's C8 ruling 27)."),
            };
        plan.PositionStorage[index] = position with { Acquisition = acquisition };
    }

    // SPEC 15.1.6: a binding reached with shared or exclusive access borrows the Place its step selected, which its access alone decides
    // (AcquireBinding reads no Copy proof for it): it is acquired where its plan's Patterns are bound, and its Borrow is formed there, before
    // the guards and bodies that state its atom bind (Binding.RawOrigins.Borrows; G147 ruling 37, D3: a Binding past a qualifying pair layer
    // of an owned Subject depends on the Subject Place itself, whose atom no other Borrow fills). Check acquires every binding again, the
    // owned ones by AcquireBinding's rule.
    private void AcquireReferenceBindings(BoundMatch plan)
    {
        for (var i = 0; i < plan.PositionStorage.Count; i++)
        {
            if (plan.PositionStorage[i] is { Kind: BoundPatternKind.Binding, AccessMode: not PatternAccessMode.Owned })
            {
                this.AcquireBinding(plan, i);
            }
        }

        if (this.FormsBorrows)
        {
            this.BindingBorrows(plan, this.borrows);
        }
    }

    private void CalculateMatchCoverage(BoundMatch plan, BoundType? subject)
    {
        // A failed Subject leaves the coverage Pending, as an unproven position does.
        if (subject is null || plan.Invalid || plan.Pending)
        {
            plan.Coverage = new(plan.Invalid ? MatchCoverageState.Invalid : MatchCoverageState.Pending);
            return;
        }

        // The structural positions selected every safe reference layer and qualifying pair layer (SPEC 14.8.1).
        while (true)
        {
            if (subject.SafeReferent is { } referent)
            {
                subject = referent;
            }
            else if (TryPairLayer(subject, out _, out var target))
            {
                subject = target;
            }
            else
            {
                break;
            }
        }

        var caseCount = subject.Symbol?.Declaration is EnumKoto { BoundSymbol.Adt: { } shape } ? shape.Cases.Length : 0;
        for (var i = 0; i < caseCount; i++)
        {
            plan.CaseCoverage.Add(0);
        }

        var hasTrue = false;
        var hasFalse = false;
        for (var i = 0; i < plan.Arms.Count; i++)
        {
            if (plan.Arms[i].Syntax.Guard is not null)
            {
                continue;
            }

            var root = plan.PositionStorage[plan.Arms[i].Pattern];
            if (root.WholePosition)
            {
                plan.Coverage = new(MatchCoverageState.Exhaustive);
                return;
            }

            if (root.Kind == BoundPatternKind.Case)
            {
                var whole = true;
                for (var child = plan.Arms[i].Pattern + 1; child < root.End; child = plan.PositionStorage[child].End)
                {
                    whole &= plan.PositionStorage[child].WholePosition;
                }

                var ordinal = root.Case!.Ordinal;
                plan.CaseCoverage[ordinal] = Math.Max(plan.CaseCoverage[ordinal], whole ? (byte)2 : (byte)1);
            }
            else if (root.Literal.Kind == PatternLiteralKind.Boolean)
            {
                hasTrue |= root.Literal.Bits != 0;
                hasFalse |= root.Literal.Bits == 0;
            }
        }

        if (caseCount != 0)
        {
            for (var i = 0; i < caseCount; i++)
            {
                if (plan.CaseCoverage[i] != 2)
                {
                    plan.Coverage = new(MatchCoverageState.NonExhaustive, plan.CaseCoverage[i] == 0 ? MatchCoverageReason.MissingCase : MatchCoverageReason.WholePayloadRequired, i);
                    return;
                }
            }

            plan.Coverage = new(MatchCoverageState.Exhaustive);
        }
        else
        {
            plan.Coverage = hasTrue && hasFalse ? new(MatchCoverageState.Exhaustive) :
                new(MatchCoverageState.NonExhaustive, ReferenceEquals(subject, BoundType.Boolean) ? MatchCoverageReason.BooleanValuesRequired : MatchCoverageReason.CatchAllRequired);
        }
    }

    // Completes what BindPattern left unvisited: Unit, or resting on the nearest failure above it, a failed position or the failure it
    // starts from, the failed Subject or a Case leaf's unread payload Pattern (RestOnFailure).
    private sealed class PatternMarker(Binding binding) : KotoVisitor
    {
        private Koto? failure;

        public void Mark(Koto pattern, Koto? subject)
        {
            this.failure = subject;
            this.Visit(pattern);
            this.failure = null;
        }

        public override void Visit(Koto node)
        {
            var outer = this.failure;
            if (node.BindingState == BindingState.Unvisited)
            {
                if (outer is null)
                {
                    CompletePatternSyntax(node);
                }
                else
                {
                    binding.RestOnFailure(node, outer);
                }
            }
            else if (node.BindingFailure != BindingFailure.None)
            {
                this.failure = node;
            }

            node.VisitChildren(this);
            this.failure = outer;
        }
    }

    private sealed class UnsupportedMarker : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node.BindingState == BindingState.Unvisited)
            {
                // Unknown, not failed: the enclosing guard/body reports the single Unsupported diagnostic.
                node.BindingState = BindingState.Unresolved;
            }

            node.VisitChildren(this);
        }
    }

    /// <summary>The `matches` family's one accessor (DECISIONS BU-220): a pass family, the plan of each match the pass indexed (the plan
    /// matchPlans keeps for it), in the match's module's NodeColumns at its SyntaxId; absent after a pass until the column's first write.
    /// Its members keep the names its readers use.</summary>
    private readonly struct MatchRows(Binding binding)
    {
        internal FamilyRows<BoundMatch?> Values => new(binding, static (table, b) => table.Hir.Families.Matches.Enumerate(b));

        internal BoundMatch this[MatchKoto match] => this.TryGetValue(match, out var plan) ? plan : throw new KeyNotFoundException($"`{match}` was not indexed in this pass.");

        internal void Add(MatchKoto match, BoundMatch plan)
        {
            ref var row = ref HirTables.Of(match).Families.Matches.Ref(match.SyntaxId, binding);
            row = row is null ? plan : throw new ArgumentException($"`{match}` was already indexed in this pass.", nameof(match));
        }

        internal bool ContainsKey(MatchKoto match) => this.TryGetValue(match, out _);

        internal bool TryGetValue(MatchKoto match, [NotNullWhen(true)] out BoundMatch? plan)
            => (plan = match.SyntaxOwner is { } table ? table.Hir.Families.Matches.Get(match.SyntaxId, binding) : null) is not null;
    }

    /// <summary>The `matchPlans` family's one accessor (DECISIONS BU-220): a kept family, the plan object a match keeps across rebinds (each
    /// pass resets it, ResetMatches, and a match the pass did not index drops it, PruneMatchPlans), in the match's module's
    /// NodeColumns at its SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct MatchPlanRows(Binding binding)
    {
        internal FamilyRows<BoundMatch?> Values => new(binding, static (table, _) => table.Hir.Families.MatchPlans.Enumerate());

        internal void Add(MatchKoto match, BoundMatch plan)
        {
            ref var row = ref HirTables.Of(match).Families.MatchPlans.Ref(match.SyntaxId);
            row = row is null ? plan : throw new ArgumentException($"`{match}` already keeps a plan.", nameof(match));
        }

        internal void Remove(MatchKoto match) => HirTables.Of(match).Families.MatchPlans.Ref(match.SyntaxId) = null;

        internal bool TryGetValue(MatchKoto match, [NotNullWhen(true)] out BoundMatch? plan)
            => (plan = match.SyntaxOwner is { } table ? table.Hir.Families.MatchPlans.Get(match.SyntaxId) : null) is not null;
    }

    /// <summary>The pattern nodes' and guard scopes' one accessor (DECISIONS BU-220): a kept family, whether the latest run of a node's pass
    /// indexed it as a pattern node (IndexMatchArms' pattern depth) or a guard scope, a `(Pattern, Guard)` row in the node's module's
    /// NodeColumns at its SyntaxId (the two sets the pass's part held, one row by the key's two slots). The pass's reset copies its
    /// modules' rows into the part's previous-run lists, which its prune steps compare with the new run's, and empties them
    /// (ResetMatches); so a pass prunes against its own previous run and never removes a scope another pass indexed.</summary>
    private readonly struct IndexedPatternRows(Binding binding)
    {
        internal FamilyRows<(bool Pattern, bool Guard)> Values => new(binding, static (table, _) => table.Hir.Families.IndexedPatterns.Enumerate());

        internal (bool Pattern, bool Guard) this[Koto node] => node.SyntaxOwner is { } table ? table.Hir.Families.IndexedPatterns.Get(node.SyntaxId) : default;

        internal void AddPattern(Koto node) => HirTables.Of(node).Families.IndexedPatterns.Ref(node.SyntaxId).Pattern = true;

        internal void AddGuard(Koto guard) => HirTables.Of(guard).Families.IndexedPatterns.Ref(guard.SyntaxId).Guard = true;

        // Over the running pass's modules.
        internal void Empty()
        {
            for (var i = 0; i < binding.pass.Count; i++)
            {
                binding.pass[i].Syntax.Hir.Families.IndexedPatterns.Empty();
            }
        }
    }
}
