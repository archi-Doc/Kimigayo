// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<MatchKoto, BoundMatch> matches = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundMatch> matchPool = new();
    private readonly HashSet<Koto> patternNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Koto> previousPatternNodes = new();
    private readonly HashSet<Koto> candidateScopes = new();
    private readonly List<Koto> previousCandidateScopes = new();
    private readonly List<PatternWarning> patternWarnings = new();
    private PatternMarker? patternMarker;
    private PatternMarker? unsupportedMarker;
    private int reportedPatternWarnings;

    /// <summary>Gets warnings without changing Binding validity.</summary>
    public IReadOnlyList<PatternWarning> PatternWarnings => this.patternWarnings;

    /// <summary>Gets the current positional Pattern and coverage plan, including Invalid/Pending results.</summary>
    /// <param name="match">The selected match syntax.</param>
    /// <param name="plan">The current plan, or null if the syntax was replaced.</param>
    /// <returns>Whether the current Binding pass indexed this match.</returns>
    public bool TryGetMatch(MatchKoto match, out BoundMatch? plan) => this.matches.TryGetValue(match, out plan);

    // SPEC 15.1.6 subject rule: the Subject mode is the access that the Subject Place grants to its first layer that
    // is not a safe reference. Reference layers that are all uniq/objuniq give Exclusive, and a ref/objref layer, or a
    // shared layer on the path of a bare Place, bounds the mode to Shared. A bare Place (borrowed in place) is Shared,
    // and any other acquisition, including @move and a temporary, yields an owned ByValue Subject.
    private static SubjectMode SubjectModeOf(Koto expression, BoundType? type)
    {
        var bare = IsBarePlace(expression);
        if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq, Components.Count: 1 })
        {
            return bare ? SubjectMode.Shared : SubjectMode.ByValue;
        }

        if (bare && ReachedThroughShared(expression))
        {
            return SubjectMode.Shared;
        }

        for (var layer = type; layer is { Kind: BoundTypeKind.Semantics, Components.Count: 1 }; layer = layer.Components[0])
        {
            if (layer.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef)
            {
                return SubjectMode.Shared;
            }

            if (layer.Semantics is not (SemanticsKind.Uniq or SemanticsKind.ObjUniq))
            {
                break;
            }
        }

        return SubjectMode.Exclusive;
    }

    private static bool ContainsPattern(BoundMatch plan, int earlier, int later)
    {
        var a = plan.PositionStorage[earlier];
        var b = plan.PositionStorage[later];
        if (a.Kind is BoundPatternKind.Wildcard or BoundPatternKind.Binding)
        {
            return true;
        }

        if (a.Kind != b.Kind || a.Case != b.Case)
        {
            return false;
        }

        if (a.Kind == BoundPatternKind.Literal)
        {
            return a.Literal == b.Literal;
        }

        var x = earlier + 1;
        var y = later + 1;
        while (x < a.End && y < b.End)
        {
            if (!ContainsPattern(plan, x, y))
            {
                return false;
            }

            x = plan.PositionStorage[x].End;
            y = plan.PositionStorage[y].End;
        }

        return x == a.End && y == b.End;
    }

    private void ResetMatches()
    {
        foreach (var plan in this.matches.Values)
        {
            plan.Reset(null);
        }

        this.matches.Clear();
        foreach (var guard in this.candidateScopes)
        {
            this.previousCandidateScopes.Add(guard);
        }

        this.candidateScopes.Clear();
        foreach (var node in this.patternNodes)
        {
            this.previousPatternNodes.Add(node);
        }

        this.patternNodes.Clear();
        this.patternWarnings.Clear();
        this.reportedPatternWarnings = 0;
    }

    private void PruneCandidateScopes()
    {
        foreach (var guard in this.previousCandidateScopes)
        {
            if (!this.candidateScopes.Contains(guard))
            {
                this.scopes.Remove(guard);
            }
        }

        this.previousCandidateScopes.Clear();
    }

    private void PrunePatternScopes()
    {
        foreach (var node in this.previousPatternNodes)
        {
            if (!this.patternNodes.Contains(node) && !this.candidateScopes.Contains(node))
            {
                this.scopes.Remove(node);
                this.symbols.Remove(node);
            }
        }

        this.previousPatternNodes.Clear();
    }

    private void IndexMatch(MatchKoto match)
    {
        var index = this.matches.Count;
        if (index == this.matchPool.Count)
        {
            this.matchPool.Add(new());
        }

        var plan = this.matchPool[index];
        plan.Reset(match);
        this.matches.Add(match, plan);
    }

    private void MarkPatternTree(Koto pattern)
        => (this.patternMarker ??= new(false)).Visit(pattern);

    /// <summary>Marks an unbound guard/body so later analyses see unknown Types instead of a fabricated Unit.</summary>
    private void MarkUnsupportedTree(Koto node)
        => (this.unsupportedMarker ??= new(true)).Visit(node);

    private BoundType? BindMatch(MatchKoto match, BindingScope scope, BoundType? expected)
    {
        var plan = this.matches[match];
        var subject = this.BindNode(match.Expression, scope);
        if (match is TryKoto propagation)
        {
            if (subject?.Kind != BoundTypeKind.Constructed || subject.Semantics != SemanticsKind.Owner || (subject.Symbol != this.Library.Option && subject.Symbol != this.Library.Result))
            {
                return Fail(match, BindingFailure.TypeMismatch);
            }

            propagation.SelectOption(subject.Symbol == this.Library.Option);
            if (!propagation.SemanticsIndexed)
            {
                this.indexer.IndexMatchArms(propagation, scope);
                propagation.SemanticsIndexed = true;
            }

            expected = subject.Components[0];
        }
        else
        {
            subject = this.AcquireSubject(match.Expression, subject, out var borrow, out var mode);
            plan.SubjectBorrow = borrow;
            plan.Mode = mode;
        }

        var resultContext = this.BeginResult(match, scope, expected, deferEvidence: true);
        if (subject is null)
        {
            plan.Pending = true;
            return Fail(match, BindingFailure.Unsupported, true);
        }

        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            var root = this.BindPattern(arm.Pattern, subject, scope, plan, -1, 0, PatternAccessMode.Owned, null);
            plan.ArmStorage.Add(new(arm, root));
            this.MarkPatternTree(arm.Pattern);
        }

        // Pattern bindings must have their Subject Types before collecting result evidence.
        // Otherwise an unfitted literal arm could default to f64 before seeing an f32 binding.
        if (resultContext.Expected is null)
        {
            this.InferResultExpected(match, scope, resultContext);
        }

        plan.ExpectedType = resultContext.Expected;
        this.CalculateMatchCoverage(plan, subject);

        var pendingBody = false;
        var required = KotoHelper.IsResultRequiringSelection(match);
        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            if (plan.Pending && arm.Guard is null)
            {
                // Implicit shared inspection needs candidate/body Loan semantics before
                // the ordinary expression binder may use these Pattern bindings.
                this.MarkUnsupportedTree(arm.Body);
                Fail(arm.Body, BindingFailure.Unsupported, true);
                pendingBody = true;
                continue;
            }

            if (arm.Guard is { } guard)
            {
                if (plan.Pending || !MatchTypes.SupportsGuard(plan, plan.Arms[i].Pattern))
                {
                    this.MarkUnsupportedTree(guard);
                    this.MarkUnsupportedTree(arm.Body);
                    Fail(guard, BindingFailure.Unsupported, true);
                    Fail(arm.Body, BindingFailure.Unsupported, true);
                    pendingBody = true;
                    continue;
                }

                var guardType = this.BindNode(guard, this.scopes[guard], BoundType.Boolean);
                pendingBody |= guardType is null;
                if (guardType is not null && !Compatible(guardType, BoundType.Boolean))
                {
                    Fail(guard, BindingFailure.TypeMismatch);
                    plan.Invalid = true;
                }
            }

            var bodyType = this.BindNode(arm.Body, this.scopes[arm.Pattern], required ? resultContext.Expected : null);
            pendingBody |= bodyType is null;
        }

        if (plan.Coverage.State == MatchCoverageState.NonExhaustive)
        {
            // Keep the match unavailable until late Type validation decides the diagnostic.
            return Complete(match, null);
        }

        if (plan.Invalid)
        {
            return Fail(match, BindingFailure.InvalidPattern);
        }

        if (plan.Pending || pendingBody)
        {
            return Fail(match, BindingFailure.Unsupported, true);
        }

        plan.ResultType = this.FinishResult(match, resultContext);
        return plan.ResultType;
    }

    // SPEC 15.1.6 subject rule: the Subject is acquired as written, except that a bare Place is borrowed in place: a
    // stored uniq/objuniq reference is Reborrowed exclusively, a stored ref/objref reference is Copied, and any other
    // stored value is shared-borrowed.
    private BoundType? AcquireSubject(Koto expression, BoundType? type, out BoundType? borrow, out SubjectMode mode)
    {
        borrow = null;
        mode = SubjectModeOf(expression, type);
        if (type is null || ReferenceEquals(type, BoundType.Never) || !IsBarePlace(expression))
        {
            return type;
        }

        if (mode == SubjectMode.Exclusive)
        {
            borrow = type;
            return type;
        }

        if (type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq)
        {
            // SPEC 15.1.6, 15.6.2: a stored exclusive reference reached through a shared layer is shared-Reborrowed.
            var origin = type.Origin is { } stored ? this.Meet(this.PlaceOrigin(expression), stored) : this.PlaceOrigin(expression);
            var semantics = type.Semantics == SemanticsKind.Uniq ? SemanticsKind.Ref : SemanticsKind.ObjRef;
            borrow = this.InternType(BoundTypeKind.Semantics, null, semantics, [type.Components[0]], origin: origin);
            return borrow;
        }

        if (type.Semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc)
        {
            borrow = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [type], origin: this.PlaceOrigin(expression));
            return borrow;
        }

        return type;
    }

    private int BindPattern(Koto syntax, BoundType matched, BindingScope outer, BoundMatch plan, int parent, int element, PatternAccessMode access, BoundOrigin? origin)
    {
        if (syntax is ParenthesizedKoto grouping)
        {
            var grouped = this.BindPattern(grouping.Operand, matched, outer, plan, parent, element, access, origin);
            Complete(grouping, matched);
            return grouped;
        }

        var index = plan.PositionStorage.Count;
        plan.PositionStorage.Add(new(syntax, matched, BoundPatternKind.Invalid, parent, element, index + 1));
        var position = plan.PositionStorage[index];
        plan.Invalid |= syntax.BindingState == BindingState.Invalid;
        if (!this.ValidatePatternType(plan, syntax, matched, outer))
        {
            return index;
        }

        var type = matched;
        var structural = syntax is not IdentifierNameKoto { IdentifierName: "_" } and not SyntaxFormKoto { Akind: KotoKind.BindingPattern };
        var unsupported = false;
        var layers = 0;
        if (structural)
        {
            // SPEC 14.8.1, 3.4.1: a structural Pattern selects the referent of every safe value-reference layer
            // that lacks its structure; a shared layer anywhere on the path bounds every descendant to shared access.
            while (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                access = type.Semantics == SemanticsKind.Ref || access == PatternAccessMode.Shared ? PatternAccessMode.Shared : PatternAccessMode.Exclusive;

                // SPEC 10.2: a ref layer is a Copy that restarts the dependency at its own Origin; a uniq layer below it
                // is Reborrowed, so its Origin is met with the dependency reached so far.
                origin = type.Semantics == SemanticsKind.Ref || origin is null ? type.Origin ?? origin
                    : type.Origin is { } layer ? this.Meet(origin, layer) : origin;
                type = type.Components[0];
                layers++;
            }
        }

        if (structural && type.Semantics != SemanticsKind.Owner)
        {
            plan.Invalid = true;
            Fail(syntax, BindingFailure.InvalidPattern);
            return index;
        }

        switch (syntax)
        {
            case IdentifierNameKoto { IdentifierName: "_" }:
                position = position with { Kind = BoundPatternKind.Wildcard, WholePosition = true };
                break;
            case SyntaxFormKoto { Akind: KotoKind.BindingPattern, BoundSymbol: { } symbol } binding:
                // SPEC 15.1.6: a part of the acquired owned Subject is bound as its stored Type; a Place reached
                // with shared or exclusive access binds ref/T or uniq/T, also for a Copy T. A whole-Subject binding
                // of a reference-valued Subject copies that reference and never selects the internal slot.
                symbol.Type = access == PatternAccessMode.Owned ? matched
                    : this.InternType(BoundTypeKind.Semantics, null, access == PatternAccessMode.Shared ? SemanticsKind.Ref : SemanticsKind.Uniq, [matched], origin: origin);
                symbol.BindsReference = access != PatternAccessMode.Owned || (parent < 0 && plan.Mode != SubjectMode.ByValue);
                binding.Operands[0].BoundSymbol = symbol;
                Complete(binding.Operands[0], matched);
                position = position with { Kind = BoundPatternKind.Binding, BodySymbol = symbol, WholePosition = true };
                if (this.symbols.TryGetValue(binding.Operands[0], out var candidate) && candidate.Kind == BindingSymbolKind.PatternCandidate)
                {
                    // SPEC 14.8.3: a guard candidate is a shared reference to its Place whatever the mode and Copy capability.
                    var candidateOrigin = this.OriginAtom(CandidateOriginBinder(candidate), OriginKind.Projection, candidate.Slot);
                    candidate.Type = parent < 0 && matched is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
                        ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [matched.Components[0]], origin: matched.Origin ?? candidateOrigin)
                        : this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [matched], origin: candidateOrigin);
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
                    var child = this.BindPattern(tuple.Operands[i], type.Components[i], outer, plan, index, i, access, origin);
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

                position = position with { Kind = BoundPatternKind.Case, Case = selected };
                syntax.BoundSymbol = selected.Symbol;
                for (var i = 0; i < children.Length; i++)
                {
                    var payload = this.StoredType(selected.Payload[i], type);
                    if (payload is null)
                    {
                        plan.Invalid = true;
                        Fail(children[i], BindingFailure.InvalidPattern);
                    }
                    else
                    {
                        this.BindPattern(children[i], payload, outer, plan, index, i, access, origin);
                    }
                }

                break;
            default:
                if (this.TryPatternLiteral(syntax, type, out var literal))
                {
                    position = position with { Kind = BoundPatternKind.Literal, Literal = literal };
                }

                break;
        }

        position = position with
        {
            End = plan.PositionStorage.Count,
            AccessMode = access,
            ImplicitDerefs = layers,
        };
        plan.PositionStorage[index] = position;
        if (position.Kind == BoundPatternKind.Invalid)
        {
            plan.Invalid = true;
            Fail(syntax, BindingFailure.InvalidPattern);
        }
        else if (unsupported)
        {
            plan.Pending = true;
            Fail(syntax, BindingFailure.Unsupported, true);
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
        var definition = this.TypeName(qualifier, scope, true);
        if (definition is null || !ReferenceEquals(definition, expected.Symbol))
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
            if (!SameType(qualified.Components[i], expected.Components[i]))
            {
                return null;
            }
        }

        var symbol = this.LookupTypeMember(expected, name, scope).Member;
        if (symbol?.EnumCase is null)
        {
            return null;
        }

        member.BoundSymbol = member.Right.BoundSymbol = symbol;
        return symbol.EnumCase;
    }

    private bool TryPatternLiteral(Koto syntax, BoundType type, out PatternLiteral literal)
    {
        literal = default;
        var number = syntax as NumberLiteralKoto ?? (syntax as PrefixMinusKoto)?.Operand as NumberLiteralKoto;
        if (number is not null)
        {
            if (!number.IsInteger || !number.TryGetIntegerMagnitude(out var magnitude) || !FitsIntegerMagnitude(magnitude, type, syntax is PrefixMinusKoto, this.compilation.PointerWidth))
            {
                return false;
            }

            literal = new(PatternLiteralKind.Integer, magnitude, syntax is PrefixMinusKoto && magnitude != 0);
            Complete(number, type);
            return true;
        }

        switch (syntax)
        {
            case BoolLiteralKoto boolean when ReferenceEquals(type, BoundType.Boolean):
                literal = new(PatternLiteralKind.Boolean, boolean.Value ? (UInt128)1 : 0);
                return true;
            case CharLiteralKoto { Value: { } scalar } when ReferenceEquals(type, BoundType.Char):
                literal = new(PatternLiteralKind.Character, (UInt128)scalar.Value);
                return true;
            case StringLiteralKoto text when ReferenceEquals(type, BoundType.String):
                literal = new(PatternLiteralKind.String, Text: text.Literal);
                return true;
            default:
                return false;
        }
    }

    private bool ValidatePatternType(BoundMatch plan, Koto syntax, BoundType type, BindingScope scope)
    {
        var proof = this.CheckTypeConstraints(type, scope);
        if (proof == ConstraintProof.Proven)
        {
            return true;
        }

        plan.Invalid |= proof != ConstraintProof.Unknown;
        plan.Pending |= proof == ConstraintProof.Unknown;
        Fail(syntax, proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
        return false;
    }

    private void CompletePatternAcquisitions()
    {
        foreach (var plan in this.matches.Values)
        {
            var scope = this.ConstraintScope(plan.Syntax);
            for (var i = 0; i < plan.PositionStorage.Count; i++)
            {
                var position = plan.PositionStorage[i];
                // Recheck after late declaration/constraint validation, including discards.
                if (!this.ValidatePatternType(plan, position.Source, position.MatchedType, scope))
                {
                    plan.Coverage = new(plan.Invalid ? MatchCoverageState.Invalid : MatchCoverageState.Pending);
                    continue;
                }

                if (position.Kind != BoundPatternKind.Binding)
                {
                    continue;
                }

                // SPEC 15.1.6: a binding reached through a reference layer borrows its Place; an owned part is
                // copied or transferred by its complete stored Type.
                var proof = position.AccessMode == PatternAccessMode.Owned ? this.ProveCopy(position.MatchedType, position.Source) : ConstraintProof.Refuted;
                var acquisition = position.AccessMode != PatternAccessMode.Owned ? PatternAcquisition.Borrow
                    : proof == ConstraintProof.Proven ? PatternAcquisition.Copy : proof == ConstraintProof.Refuted ? PatternAcquisition.Move
                    : proof == ConstraintProof.Unknown ? PatternAcquisition.CopyOrMove : PatternAcquisition.Deferred;
                plan.PositionStorage[i] = position with { Acquisition = acquisition };
                if (proof == ConstraintProof.Error)
                {
                    plan.Invalid = true;
                    plan.Coverage = new(MatchCoverageState.Invalid);
                    Fail(position.Source, BindingFailure.InvalidConstraint);
                }
            }

            if (plan.Coverage.State == MatchCoverageState.NonExhaustive)
            {
                Fail(plan.Syntax, BindingFailure.NonExhaustiveMatch);
            }

            // Publish coverage warnings only after all Pattern Type checks have completed.
            if (plan.Coverage.State is MatchCoverageState.Exhaustive or MatchCoverageState.NonExhaustive)
            {
                for (var later = 1; later < plan.Arms.Count; later++)
                {
                    for (var earlier = 0; earlier < later; earlier++)
                    {
                        if (plan.Arms[earlier].Syntax.Guard is null && ContainsPattern(plan, plan.Arms[earlier].Pattern, plan.Arms[later].Pattern))
                        {
                            this.patternWarnings.Add(new(plan.Arms[later].Syntax.Pattern, plan.Arms[earlier].Syntax.Pattern, earlier));
                            break;
                        }
                    }
                }
            }
        }
    }

    private void CalculateMatchCoverage(BoundMatch plan, BoundType subject)
    {
        if (plan.Invalid || plan.Pending)
        {
            plan.Coverage = new(plan.Invalid ? MatchCoverageState.Invalid : MatchCoverageState.Pending);
            return;
        }

        while (subject is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            subject = subject.Components[0];
        }

        var caseCount = subject.Symbol?.Declaration is EnumKoto declaration && this.storageShapes.TryGetValue(declaration, out var shape) ? shape.CaseCount : 0;
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
                hasTrue |= root.Literal.Magnitude != 0;
                hasFalse |= root.Literal.Magnitude == 0;
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

    private sealed class PatternMarker(bool unsupported) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node.BindingState == BindingState.Unvisited)
            {
                if (unsupported)
                {
                    // Unknown, not failed: the enclosing guard/body reports the single Unsupported diagnostic.
                    node.BindingState = BindingState.Unresolved;
                }
                else
                {
                    Complete(node, BoundType.Unit);
                }
            }

            node.VisitChildren(this);
        }
    }
}
