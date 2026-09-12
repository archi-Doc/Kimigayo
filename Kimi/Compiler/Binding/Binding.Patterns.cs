// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<MatchKoto, BoundMatch> matches = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundMatch> matchPool = new();
    private readonly HashSet<Koto> patternNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Koto> previousPatternNodes = new();
    private readonly List<PatternWarning> patternWarnings = new();
    private PatternMarker? patternMarker;
    private int reportedPatternWarnings;

    /// <summary>Gets warnings without changing Binding validity.</summary>
    public IReadOnlyList<PatternWarning> PatternWarnings => this.patternWarnings;

    /// <summary>Gets the current positional Pattern and coverage plan, including Invalid/Pending results.</summary>
    /// <param name="match">The selected match syntax.</param>
    /// <param name="plan">The current plan, or null if the syntax was replaced.</param>
    /// <returns>Whether the current Binding pass indexed this match.</returns>
    public bool TryGetMatch(MatchKoto match, out BoundMatch? plan) => this.matches.TryGetValue(match, out plan);

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
        foreach (var node in this.patternNodes)
        {
            this.previousPatternNodes.Add(node);
        }

        this.patternNodes.Clear();
        this.patternWarnings.Clear();
        this.reportedPatternWarnings = 0;
    }

    private void PrunePatternScopes()
    {
        foreach (var node in this.previousPatternNodes)
        {
            if (!this.patternNodes.Contains(node))
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
        => (this.patternMarker ??= new()).Visit(pattern);

    private BoundType? BindMatch(MatchKoto match, BindingScope scope, BoundType? expected)
    {
        var plan = this.matches[match];
        var resultContext = this.BeginResult(match, scope, expected);
        plan.ExpectedType = resultContext.Expected;
        var subject = this.BindNode(match.Expression, scope);
        if (subject is null)
        {
            plan.Pending = true;
            return Fail(match, BindingFailure.Unsupported, true);
        }

        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            var root = this.BindPattern(arm.Pattern, subject, scope, plan, -1, 0, false);
            plan.ArmStorage.Add(new(arm, root));
            this.MarkPatternTree(arm.Pattern);
        }

        this.CalculateMatchCoverage(plan, subject);
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

        var pendingBody = false;
        var required = KotoHelper.IsResultRequiringSelection(match);
        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            if (plan.Pending && arm.Guard is null)
            {
                // Implicit shared inspection needs candidate/body Loan semantics before
                // the ordinary expression binder may use these Pattern bindings.
                this.MarkPatternTree(arm.Body);
                Fail(arm.Body, BindingFailure.Unsupported, true);
                pendingBody = true;
                continue;
            }

            if (arm.Guard is { } guard)
            {
                // Candidate reading needs its own identities and Loan checks. Still retain
                // the validated Pattern and coverage, but do not bind with body-local types.
                this.MarkPatternTree(guard);
                this.MarkPatternTree(arm.Body);
                Fail(guard, BindingFailure.Unsupported, true);
                Fail(arm.Body, BindingFailure.Unsupported, true);
                pendingBody = true;
                continue;
            }

            var bodyType = this.BindNode(arm.Body, this.scopes[arm.Pattern], required ? resultContext.Expected : null);
            pendingBody |= bodyType is null;
        }

        if (plan.Coverage.State == MatchCoverageState.NonExhaustive)
        {
            return Fail(match, BindingFailure.NonExhaustiveMatch);
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

    private int BindPattern(Koto syntax, BoundType matched, BindingScope outer, BoundMatch plan, int parent, int element, bool shared)
    {
        if (syntax is ParenthesizedKoto grouping)
        {
            var grouped = this.BindPattern(grouping.Operand, matched, outer, plan, parent, element, shared);
            Complete(grouping, matched);
            return grouped;
        }

        var index = plan.PositionStorage.Count;
        plan.PositionStorage.Add(new(syntax, matched, BoundPatternKind.Invalid, parent, element, index + 1));
        var position = plan.PositionStorage[index];
        plan.Invalid |= syntax.BindingState == BindingState.Invalid;
        var type = matched;
        var structural = syntax is not IdentifierNameKoto { IdentifierName: "_" } and not SyntaxFormKoto { Akind: KotoKind.BindingPattern };
        var unsupported = false;
        var implicitDeref = PatternImplicitDeref.None;
        if (structural && type.Semantics == SemanticsKind.Ref && type.Kind == BoundTypeKind.Semantics)
        {
            type = type.Components[0];
            shared = true;
            unsupported = true;
            implicitDeref = PatternImplicitDeref.SharedOnce;
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
                symbol.Type = shared ? null : matched;
                binding.Operands[0].BoundSymbol = symbol;
                Complete(binding.Operands[0], matched);
                position = position with { Kind = BoundPatternKind.Binding, BodySymbol = symbol, WholePosition = true };
                unsupported |= shared;
                break;
            case UnitLiteralKoto when ReferenceEquals(type, BoundType.Unit):
                position = position with { Kind = BoundPatternKind.Unit, WholePosition = true };
                break;
            case SyntaxFormKoto { Akind: KotoKind.TuplePattern } tuple when type.Kind == BoundTypeKind.Tuple && tuple.Operands.Length == type.Components.Count:
                position = position with { Kind = BoundPatternKind.Tuple, WholePosition = true };
                for (var i = 0; i < tuple.Operands.Length; i++)
                {
                    var child = this.BindPattern(tuple.Operands[i], type.Components[i], outer, plan, index, i, shared);
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
                        this.BindPattern(children[i], payload, outer, plan, index, i, shared);
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
            AccessMode = shared ? PatternAccessMode.Shared : PatternAccessMode.Owned,
            ImplicitDeref = implicitDeref,
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
        var definition = qualifier is GenericsKoto generic ? this.TypeName(generic.Identifier!, scope, true) : this.TypeName(qualifier, scope, true);
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

    private void CompletePatternAcquisitions()
    {
        foreach (var plan in this.matches.Values)
        {
            for (var i = 0; i < plan.PositionStorage.Count; i++)
            {
                var position = plan.PositionStorage[i];
                if (position.Kind != BoundPatternKind.Binding || position.AccessMode == PatternAccessMode.Shared)
                {
                    continue;
                }

                var proof = this.ProveCopy(position.MatchedType, position.Source);
                var acquisition = proof == ConstraintProof.Proven ? PatternAcquisition.Copy : proof == ConstraintProof.Refuted ? PatternAcquisition.Move : PatternAcquisition.Deferred;
                plan.PositionStorage[i] = position with { Acquisition = acquisition };
                if (proof == ConstraintProof.Error)
                {
                    plan.Invalid = true;
                    plan.Coverage = new(MatchCoverageState.Invalid);
                    Fail(position.Source, BindingFailure.InvalidConstraint);
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

    private sealed class PatternMarker : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node.BindingState == BindingState.Unvisited)
            {
                Complete(node, BoundType.Unit);
            }

            node.VisitChildren(this);
        }
    }
}
