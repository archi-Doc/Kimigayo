// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Bounds collected while call candidates are tried; only the selected candidate's are kept (CandidateBounds).
    private readonly List<(OriginDeclaration Declaration, BoundOrigin Variable, BoundOrigin Bound, Koto Use, ulong Condition)> candidateBounds = new();

    // Every change to inference, obligation or Origin declaration state; a pure proof request leaves it unchanged.
    private int originStateVersion;

    // The calls whose candidates are being tried, outermost first; bounds wait until the outermost selection completes.
    private int candidateBoundDepth;

    // SPEC 15.6.1, 23.3.6.5: whether an Origin obligation is a fit, reported at the value with the source `fit`, rather than the
    // well-formedness of a Type occurrence (RetainInnerOutlives), whose use is that occurrence and whose Type is the inner one, or of a
    // callee's result at its call (RequireResultPremises), or a Type's clause substituted at its occurrence (AddTypeClauseObligations),
    // a declared relation.
    internal static bool IsFitObligation(in BindingObligation obligation)
        => obligation.Clause is null && !obligation.WellFormed && (obligation.Type is null || ReferenceEquals(obligation.Type.Origin, obligation.Shorter) || obligation.Use is not TypeKoto);

    // SPEC 15.6.5: a fixed Origin is fixed by the body's contract: a signature, parameter or receiver Origin, a projection of one,
    // static, or a meet of them.
    internal static bool FixedOrigin(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!FixedOrigin(origin.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input;
    }

    internal bool IsVerifiedOriginObligation(in BindingObligation obligation)
    {
        if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending)
        {
            // SPEC 15.3.6: an Origin resolved to an open region is that local region; it needs no annotation.
            var resolved = this.OriginAtUse(pending, obligation.Use);
            return resolved.Kind is not (OriginKind.Inference or OriginKind.Unbound) || IsLocalRegion(resolved);
        }

        if (obligation.Kind == BindingObligationKind.OriginOutlives && obligation.Longer is { } longer && obligation.Shorter is { } shorter)
        {
            return this.ProvesOriginOutlives(longer, shorter, obligation.Use, obligation.Condition) &&
                (!obligation.Equality || this.ProvesOriginOutlives(shorter, longer, obligation.Use, obligation.Condition));
        }

        return false;
    }

    // SPEC 15.6.1, 15.6.5: the judgment of an Origin obligation's relation; `==` holds when both directions do, and otherwise takes
    // the worse direction, Refuted before Unknown before Unrepresentable. `reversed` tells that the reverse direction decides it, so
    // a record names that direction's longer end first.
    internal OriginJudgment JudgeOriginObligation(in BindingObligation obligation, out bool reversed)
    {
        reversed = false;
        var forward = this.JudgeOriginRelation(obligation.Longer!, obligation.Shorter!, obligation.Use, obligation.Condition);
        if (!obligation.Equality || forward == OriginJudgment.Refuted)
        {
            return forward;
        }

        var backward = this.JudgeOriginRelation(obligation.Shorter!, obligation.Longer!, obligation.Use, obligation.Condition);
        reversed = Severity(backward) > Severity(forward);
        return reversed ? backward : forward;

        static int Severity(OriginJudgment judgment) => judgment switch
        {
            OriginJudgment.Refuted => 3,
            OriginJudgment.Unknown => 2,
            OriginJudgment.Unrepresentable => 1,
            _ => 0,
        };
    }

    // SPEC 15.6.5: an unproven relation fails only when it is Refuted, a finite Origin outliving a fixed one, or Unknown between two
    // fixed Origins. `condition` is the Semantics case the relation is required in (BindingObligation.Condition).
    internal bool OriginRelationFails(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
        => this.JudgeOriginRelation(longer, shorter, use, condition) is OriginJudgment.Refuted or OriginJudgment.Unknown;

    // SPEC 15.6.5: local storage regions carry their source bounds to ownership; a finite-to-local-to-fixed chain is refuted
    // even in checking code. Fixed contracts still need established premises; finite relations carry their actual Loans
    // through the same reaching-value graph as inferred local regions.
    internal OriginJudgment JudgeOriginRelation(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        return this.HasRegionBounds(longer) ? this.JudgeLocalRegion(longer, shorter, use, condition) : this.JudgeOriginAtoms(longer, shorter, use, condition);
    }

    private static bool IsWithin(Koto use, Koto declaration)
    {
        for (var current = use; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, declaration))
            {
                return true;
            }
        }

        return false;
    }

    private OriginJudgment JudgeOriginAtoms(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        if (IsLocalRegion(shorter))
        {
            return OriginJudgment.Proven;
        }

        if (this.ProvesOriginOutlives(longer, shorter, use, condition))
        {
            return OriginJudgment.Proven;
        }

        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (RefutesOriginRelation(longer, shorter))
        {
            return OriginJudgment.Refuted;
        }

        return longer.Kind == OriginKind.Unbound || shorter.Kind == OriginKind.Unbound ? OriginJudgment.Unrepresentable
            : FixedOrigin(longer) && FixedOrigin(shorter) ? OriginJudgment.Unknown
            : OriginJudgment.Proven;
    }

    // A local's omitted Origins remain open while its initializer acquires values, whether written in its annotation or
    // in a construction qualifier. Published contracts and subsequent uses of the local never open inference again.
    private OriginDeclaration? OpenInitializerInference(BoundOrigin atom, Koto use)
    {
        // A body's stable storage slot is already owned by its declaration. In particular, an enclosing closure initializer
        // must not solve that slot as one of its own omitted Origins.
        if (IsLocalRegion(atom))
        {
            return null;
        }

        for (var node = atom.Binder; node is not null; node = node.Parent)
        {
            if (node is VariableKoto { InitializerKoto: { } initializer } variable && IsWithin(use, initializer) &&
                (IsWithin(atom.Binder!, initializer) || (variable.TypeKoto is { } annotation && IsWithin(atom.Binder!, annotation))))
            {
                if (!this.initializerOrigins.TryGetValue(variable, out var declaration))
                {
                    this.initializerOrigins.Add(variable, declaration = new(variable));
                    this.originStateVersion++;
                }

                return declaration.State < 2 ? declaration : null;
            }
        }

        return null;
    }

    private BoundOrigin OriginAtUse(BoundOrigin origin, Koto use)
    {
        var initializers = this.initializerOrigins.Count != 0;
        for (var node = use; node is not null; node = node.Parent)
        {
            if (this.originDeclarations.TryGetValue(node, out var declaration) && declaration.State >= 2)
            {
                origin = this.ResolveOrigin(origin, declaration);
            }

            if (initializers && this.initializerOrigins.TryGetValue(node, out var inferred) && inferred.State >= 2)
            {
                origin = this.ResolveOrigin(origin, inferred);
            }
        }

        return origin;
    }

    // SPEC 15.3.6 (PLAN G74): a pure question: whether the premises visible at `use` entail `longer outlives shorter`
    // (EntailsOrigin). A request leaves inference, obligation and declaration state unchanged, which every request checks
    // (originStateVersion). `condition` is the Semantics case the relation is required in (SPEC 15.6.5).
    private bool ProvesOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        var version = this.originStateVersion;
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        this.OriginProofMetrics?.Enter(longer, shorter, use, this.originPremiseDepth);
        var proven = this.EntailsOrigin(longer, shorter, use, condition);
        if (version != this.originStateVersion)
        {
            throw new InvalidOperationException($"The Origin proof of `{longer} outlives {shorter}` changed inference or obligation state (PLAN G74).");
        }

        return proven;
    }

    // SPEC 15.4.4 (PLAN G74 U1): the constraint a fit collects. A fit that requires a value to outlive an Origin omitted in a local's
    // annotation or initializer, while that initializer is still bound, bounds the Origin with the value, and the relation is proven
    // again once the Origin is resolved; any other requirement is the pure proof. An open region holds no Loans, so a value fitted
    // into it would lose its Loans: such a fit is never collected here.
    private bool FitOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (!OriginOutlives(longer, shorter) && shorter is { Kind: OriginKind.Inference, Open: false } && this.OpenInitializerInference(shorter, use) is { } pending)
        {
            this.BoundInitializerOrigin(pending, shorter, longer, use, condition);
            return true;
        }

        return this.ProvesOriginOutlives(longer, shorter, use, condition);
    }

    // Records `bound` for a local's omitted Origin: at once, or, while call candidates are tried, for the selected candidate only.
    // A bound required only in some Semantics cases still bounds the Origin in every case.
    private void BoundInitializerOrigin(OriginDeclaration pending, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
    {
        if (this.candidateBoundDepth != 0)
        {
            this.candidateBounds.Add((pending, variable, bound, use, condition));
            return;
        }

        this.ApplyInitializerBound(pending, variable, bound, use, condition);
    }

    private void ApplyInitializerBound(OriginDeclaration pending, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
    {
        pending.Replacements[variable] = pending.Replacements.TryGetValue(variable, out var previous) ? this.Meet(previous, bound) : bound;
        this.originStateVersion++;
        this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, bound, variable, Condition: condition));
    }

    // A call's candidate selection collects the bounds of its fits from the returned mark.
    private int BeginCandidateBounds()
    {
        this.candidateBoundDepth++;
        return this.candidateBounds.Count;
    }

    // SPEC 10.1, 15.4.4: only the selected candidate's fits are values fitted to an omitted Origin; the bounds other candidates
    // collected between `mark` and `trialsEnd` are dropped, the selected one's [`start`, `end`) kept.
    private void KeepCandidateBounds(int mark, int start, int end, int trialsEnd)
    {
        this.candidateBounds.RemoveRange(end, trialsEnd - end);
        this.candidateBounds.RemoveRange(mark, start - mark);
    }

    // Ends a call's selection: without a selected candidate its bounds are dropped (a later attempt collects its own), and the
    // outermost selection applies the bounds that remain.
    private void EndCandidateBounds(int mark, bool selected)
    {
        if (!selected)
        {
            this.candidateBounds.RemoveRange(mark, this.candidateBounds.Count - mark);
        }

        if (--this.candidateBoundDepth == 0)
        {
            for (var i = 0; i < this.candidateBounds.Count; i++)
            {
                var (declaration, variable, bound, use, condition) = this.candidateBounds[i];
                this.ApplyInitializerBound(declaration, variable, bound, use, condition);
            }

            this.candidateBounds.Clear();
        }
    }

    private ConstraintProof CheckTypeOriginRelations(BoundType type, BindingScope scope)
    {
        if (type.Symbol is not { } symbol || !this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) || declaration.State != 3)
        {
            return ConstraintProof.Proven;
        }

        if (declaration.Failure is not null)
        {
            return ConstraintProof.Error;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            if (!this.ProvesOriginOutlives(a, b, scope.Owner) || (relation.Equality && !this.ProvesOriginOutlives(b, a, scope.Owner)))
            {
                return ConstraintProof.Unknown;
            }
        }

        return ConstraintProof.Proven;
    }

    // SPEC 15.3.3, 15.6.1: a Type's own clauses are premises of its definition and obligations of each use, so a local annotation
    // proves them substituted with the Origins its Type binds, also for a Type nested in it. Each is an obligation at the Type
    // occurrence that ownership judges and reports as a declared relation with the Type's clause related; it is never a premise of
    // the body. A Function Type's inputs are its callee's premises, judged at each call's input (RequireInputPremises), so they are not entered.
    private void AddTypeClauseObligations(BoundType type, Koto occurrence, int depth = 0)
    {
        if (depth > 64)
        {
            return;
        }

        if (type.OriginArguments.Count != 0 && type.Symbol is { } symbol &&
            this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                if (!ReferenceEquals(a, b))
                {
                    this.AddObligation(new(BindingObligationKind.OriginOutlives, occurrence, BindingDeadline.BodyOrigins, null, a, b, Equality: relation.Equality, Clause: relation.Syntax));
                }
            }
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.AddTypeClauseObligations(type.Components[i], occurrence, depth + 1);
        }
    }

    private bool CheckCallOriginRelations(Koto function, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use, BoundType? declaringType)
    {
        if (!this.originDeclarations.TryGetValue(function, out var declaration))
        {
            return true;
        }

        if (declaration.Failure is not null)
        {
            return false;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = Substitute(relation.Longer);
            var b = Substitute(relation.Shorter);
            if (!this.ProvesOriginOutlives(a, b, use) || (relation.Equality && !this.ProvesOriginOutlives(b, a, use)))
            {
                return false;
            }
        }

        return true;

        BoundOrigin Substitute(BoundOrigin origin)
        {
            if (declaringType?.Symbol is { } owner)
            {
                origin = this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
            }

            return this.SubstituteStoredOrigin(origin, function, origins.AsSpan(0, function.BoundSymbol?.Schema?.Origins.Count ?? 0), inputs.AsSpan(0, InputOriginCount(function)));
        }
    }

    private void BindTypeOriginContracts()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is DeclarationContainerKoto container && this.scopes.TryGetValue(container, out var scope) &&
                this.BeginOriginDeclaration(container, scope) is { } declaration)
            {
                for (var b = 0; b < container.Bases.Count; b++)
                {
                    this.BindType(container.Bases[b], scope);
                }

                this.CompleteOriginDeclaration(declaration);
            }
            else if (AssociatedHead(this.nodes[i]) is OriginApplicationKoto)
            {
                var node = this.nodes[i];
                var associatedScope = this.scopes[node];
                var associatedOrigins = this.BeginOriginDeclaration(node, associatedScope);
                this.BindAssociatedFormation(node, associatedScope);
                this.CompleteOriginDeclaration(associatedOrigins);
            }
        }
    }

    private void ValidateOriginRelations()
    {
        foreach (var declaration in this.originDeclarations.Values)
        {
            if (declaration.State != 3 || declaration.Owner is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto || IsAssociatedRequirement(declaration.Owner))
            {
                continue;
            }

            foreach (var relation in declaration.Relations)
            {
                if (!this.ProvesOriginOutlives(relation.Longer, relation.Shorter, relation.Syntax) ||
                    (relation.Equality && !this.ProvesOriginOutlives(relation.Shorter, relation.Longer, relation.Syntax)))
                {
                    this.JudgeDeclaredRelation(declaration, relation);
                }
            }
        }
    }

    // SPEC 15.3.3, 15.6.1, 15.6.5: a local's, Field's, Case's or alias's clause is checked against established evidence and assumes no
    // new facts. A Refuted or Unknown relation is an Origin relation record with the source `declared` and the clause related; it is
    // located at the initializer when the initializer supplies one of its ends, an Origin the local's Type omits, as the value that
    // does not fit, and otherwise at the clause. A chain the judge leaves to region inference is an obligation, which ownership
    // reports as a located limit.
    private void JudgeDeclaredRelation(OriginDeclaration declaration, OriginRelation relation)
    {
        var use = relation.Syntax;
        var obligation = new BindingObligation(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, relation.Longer, relation.Shorter, Equality: relation.Equality);
        var resolvedLonger = this.OriginAtUse(relation.Longer, use);
        var resolvedShorter = this.OriginAtUse(relation.Shorter, use);
        if (HasLocalRegion(resolvedLonger) || HasLocalRegion(resolvedShorter) || !FixedOrigin(resolvedShorter))
        {
            this.AddObligation(obligation with { Longer = resolvedLonger, Shorter = resolvedShorter, Clause = use });
            return;
        }

        var judgment = this.JudgeOriginObligation(obligation, out var reversed);
        if (judgment == OriginJudgment.Proven)
        {
            return;
        }

        if (judgment == OriginJudgment.Unrepresentable)
        {
            this.AddObligation(obligation);
            return;
        }

        // The solved lower candidate still fails a bound: no principal candidate is proven. The local needs an
        // annotation, not a feature-limit diagnostic; all clauses depending on the same unsolved local share this cause.
        if (!relation.Equality && InferredEnd(relation.Longer) == OriginVariance.Contravariant)
        {
            this.FailExplained(ref this.principalOriginFailures, declaration.Owner, BindingFailure.MissingOrigin, (resolvedLonger, resolvedShorter, use));
            return;
        }

        var longer = this.OriginAtUse(reversed ? relation.Shorter : relation.Longer, use);
        var shorter = this.OriginAtUse(reversed ? relation.Longer : relation.Shorter, use);
        var supplied = InferredEnd(relation.Longer) is not null || InferredEnd(relation.Shorter) is not null;
        var at = supplied && declaration.Owner is VariableKoto { InitializerKoto: { } initializer } ? initializer : use;
        this.FailExplained(ref this.originRelations, use, BindingFailure.OriginRelation, new OriginRelationFact(at, longer, shorter, relation.Equality, null, RefutesOriginRelation(longer, shorter), use));

        // The variance of an Origin the local's Type omits and its initializer supplies, or null for any other end.
        OriginVariance? InferredEnd(BoundOrigin end)
        {
            if (declaration.Inferred is not { } slots)
            {
                return null;
            }

            if (slots.TryGetValue(end, out var position))
            {
                return position;
            }

            for (var i = 0; i < end.Operands.Count; i++)
            {
                if (slots.TryGetValue(end.Operands[i], out position))
                {
                    return position;
                }
            }

            return null;
        }
    }
}
