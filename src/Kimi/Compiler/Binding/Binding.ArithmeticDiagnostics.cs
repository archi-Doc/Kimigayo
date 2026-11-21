// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Binding.ArithmeticDiagnostics: SPEC 13.3's arithmetic records. Responsibility: the typed facts of a fixed provider route's failed
// selection and of an explicitly declared conformance's ineligibility (13.3.3), and SPEC 13.3.4's direction contradiction
// (ValidateArithmeticDirections). Inputs of the direction check, for a scope with premises of its own: every parameter or projection
// occurring in an available premise of its view's chain (AbstractParts; written, or until C3 one level from a written Contract premise),
// their premises as ContractSolver's rows (FirstRows), the opposite premise likewise (OppositeRow) and the pair by Binding.TypeIdentity's
// judge (IdentityProven). Output: the owner's ArithmeticDirection failure with both premises' uses. Invariant: a function of the view,
// whatever the premises' order, side or spelling, and at every depth the rows reach. Prohibitions: no premise taken from the facts (they
// give the seeds only) and no identity rule of its own. Limits, until R7-C2e2's Closure(view) publishes the view's premises at every depth
// (PLAN dm1's class 1 row; it removes the seed rule): a pair whose two Types occur in no premise of the chain, only inside Contracts'
// clauses below the premises' Types (y3n, y3s; after C3 also y3d), and an identity supplied only at depth, no ground of the judge (x41i).
// The seeds are exact up to identity only with PLAN G881's row (5) (EnumerateMates): until it lands, a clause instance at a class mate's
// projection prefix is no row of a seed.
public sealed partial class Binding
{
    private readonly List<BoundType> directionSeeds = new(); // ValidateArithmeticDirections' seeds, a stack of ranges
    private Dictionary<Koto, ArithmeticSelectionFailure>? arithmeticSelectionFailures;
    private Dictionary<Koto, ArithmeticDirectionFailure>? arithmeticDirectionFailures;

    // SPEC 13.3.4: why the fixed provider route selects no unique requirement, in check order; the adapter explains each (DECISIONS
    // BU-111).
    private enum ArithmeticSelectionCause : byte
    {
        NonNumericLeft,
        Unproven,
        NoFit,
        Ambiguous,
    }

    // The scaling rows' measure: the seeds whose rows this compilation's direction checks asked for (FirstRows), one per seed and view.
    internal int DirectionSeeds { get; private set; }

    private readonly record struct ArithmeticSelectionFailure(BoundArithmetic Plan, BoundType Self, BoundType? Counterpart, KimiDeclarationId Id, ArithmeticSelectionCause Cause);

    private readonly record struct ArithmeticDirectionFailure(BoundType Left, BoundType Right, KimiDeclarationId Id, BindingSymbol Ordinary, BindingSymbol Reverse, Koto OrdinaryUse, Koto ReverseUse);

    private static string ArithmeticOperatorText(KotoKind operation) => operation switch
    {
        KotoKind.Plus => "+",
        KotoKind.Minus or KotoKind.PrefixMinus => "-",
        KotoKind.Asterisk => "*",
        KotoKind.Slash => "/",
        KotoKind.Percent => "%",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // SPEC 13.3.3: an explicitly declared arithmetic conformance whose Self or counterpart is not eligible, at the conformance; the
    // Contract declaration is related.
    private static void ReportArithmeticConformance(Koto use, DiagnosticRequirement requirement, DiagnosticCode code, ArithmeticConformanceFailure failure)
    {
        var note = failure.Cause switch
        {
            ArithmeticConformanceCause.NotUserProvider => "Self must be an ordinary outer-owner struct or enum, not a built-in or compiler-closed Type",
            ArithmeticConformanceCause.LhsNotNumeric => "Every binding must give Lhs a built-in numeric Type",
            ArithmeticConformanceCause.RhsNotOwner => "Every binding must give Rhs outer owner Semantics, excluding string",
            _ => throw new UnreachableException(),
        };
        use.Report(requirement, code, DiagnosticTypeName(failure.Type), failure.Contract.Name, note: note, evidence: [failure.Cause], related: [("contract", failure.Contract.Declaration, "arithmetic Contract declaration")]);
    }

    private Koto ArithmeticFactUse(BoundConstraint fact, BindingScope scope)
    {
        var clauses = scope.Owner is FunctionKoto function ? function.TypeConstraints : scope.Owner is DeclarationContainerKoto container ? container.ConstraintNodes : [];
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is IsKoto clause && Contains(clause.BoundConstraint, fact))
            {
                return ContractUse(clause.Right, fact.Contract) ?? clause;
            }
        }

        return scope.Owner;

        static bool Contains(BoundConstraint? root, BoundConstraint fact)
            => ReferenceEquals(root, fact) || (root?.Kind == ConstraintKind.And && (Contains(root.Left, fact) || Contains(root.Right, fact)));

        // The requirement naming the premise's bound Contract reference (ContractReferenceOf: its Type, else its selection).
        Koto? ContractUse(Koto node, BindingSymbol? contract)
            => ReferenceEquals(this.ContractReferenceOf(node), contract) ? node
            : node is BinaryKoto { Akind: KotoKind.And } conjunction ? ContractUse(conjunction.Left, contract) ?? ContractUse(conjunction.Right, contract)
            : node is ParenthesizedKoto parentheses ? ContractUse(parentheses.Operand, contract) : null;
    }

    // SPEC 13.3.4 under 8.2.3 and 8.3.2 (PLAN G882, x41n): the premises of a pair are rows. A scope with premises of its own takes as
    // seeds every parameter or projection occurring in an available premise of its view's chain (AbstractParts: one walk, each part once,
    // before any request), one per class key; each seed's available arithmetic rows (FirstRows: at every depth, through its identity
    // class) are a pair's first premise (ValidateArithmeticRows). A scope with none examines nothing: its view is its parent's, judged first. The
    // contradiction is reported once, at the first scope where its two premises meet: the validation judges a parent before its members,
    // a contradiction fails its scope's own premises after the step (ValidateConstraintEnvironments, F's C2 ruling 320), and a failed
    // premise is neither read here nor supplies a row, so a member's view never meets a pair whose derivation reads one.
    private bool ValidateArithmeticDirections(BindingScope scope)
    {
        if (scope.Constraints is not { Facts.Count: > 0 })
        {
            return true;
        }

        var classes = this.RequestClasses(scope); // before the query: a build of the view's classes may begin queries of its own
        var start = this.directionSeeds.Count;
        var query = this.visitedParts.Begin(this.storageVersion);
        for (var stating = scope; stating is not null; stating = stating.Parent)
        {
            if (stating.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (this.AvailableConstraintFact(environment, fact, out _))
                {
                    this.AbstractParts(fact, query, this.directionSeeds);
                }
            }
        }

        // One request per class key, the requests' own (RequestKey): mates of one identity class have that class's rows (PLAN G881's
        // row (5)), so the seeds are kept once per key (a second state bit of the same query).
        var unique = start;
        for (var i = start; i < this.directionSeeds.Count; i++)
        {
            var key = this.RequestKey(this.directionSeeds[i], classes);
            this.visitedParts.TryGet(key, query, out var met);
            if ((met & 2) == 0)
            {
                this.visitedParts.Set(key, query, (byte)(met | 2));
                this.directionSeeds[unique++] = this.directionSeeds[i];
            }
        }

        var valid = true;
        for (var i = start; valid && i < unique; i++)
        {
            valid = this.ValidateArithmeticRows(this.directionSeeds[i], scope);
        }

        this.directionSeeds.RemoveRange(start, this.directionSeeds.Count - start);
        return valid;
    }

    // Each available arithmetic row `X is Op<Y>` of `seed` in the view (not Negatable) with its opposite premise (OppositeRow); the
    // first pair found fails the owner. The record names the ordinary premise's Types as its row gives them (its written fact's subject,
    // else the seed it was asked for, and its argument) and each premise's use (ArithmeticRowUse).
    private bool ValidateArithmeticRows(BoundType seed, BindingScope scope)
    {
        this.DirectionSeeds++;
        var start = this.premiseRows.Count;
        this.FirstRows(seed, scope, this.premiseRows);
        var end = this.premiseRows.Count;
        var valid = true;
        for (var r = start; valid && r < end; r++)
        {
            if (this.premiseRows[r] is not { Available: true, Symbol: { Type.Components: [var counterpart] } contract } row ||
                ArithmeticContractSyntax.Identity(contract) is not { } id || id == KimiDeclarationId.Negatable)
            {
                continue;
            }

            var reversed = ArithmeticContracts.IsLeft(id);
            var oppositeId = reversed ? id - (KimiDeclarationId.LeftAddable - KimiDeclarationId.Addable) : id + (KimiDeclarationId.LeftAddable - KimiDeclarationId.Addable);
            if (this.OppositeRow(counterpart, seed, oppositeId, scope) is not { Symbol: { Type.Components: [var argument] } opposite } other)
            {
                continue;
            }

            var here = this.ArithmeticRowUse(row, scope);
            var there = this.ArithmeticRowUse(other, scope);
            this.FailExplained(ref this.arithmeticDirectionFailures, scope.Owner, BindingFailure.ArithmeticDirection, reversed ? new(other.Raw?.Subject ?? counterpart, argument, oppositeId, opposite, contract, there, here) : new(row.Raw?.Subject ?? seed, counterpart, id, contract, opposite, here, there));
            valid = false;
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        return valid;
    }

    // The related location of a premise row: its written fact at the scope stating it (ArithmeticFactUse), else that scope's owner (a
    // clause instance's or an ancestor's: its Contract row's scope), else the view's.
    private Koto ArithmeticRowUse(PremiseRow row, BindingScope view)
        => row is { Raw: { } raw, Stated: { } where } ? this.ArithmeticFactUse(raw, where) : row.Stated?.Owner ?? view.Owner;

    // The opposite premise for the pair of `subject` and `counterpart`: an available Contract row of `counterpart` in the view (FirstRows:
    // every depth, through its identity class) of the Contract `oppositeId` whose argument is `subject` by the identity judge.
    private PremiseRow? OppositeRow(BoundType counterpart, BoundType subject, KimiDeclarationId oppositeId, BindingScope view)
    {
        var start = this.premiseRows.Count;
        this.FirstRows(counterpart, view, this.premiseRows);
        PremiseRow? found = null;
        for (var i = start; found is null && i < this.premiseRows.Count; i++)
        {
            if (this.premiseRows[i] is { Available: true, Symbol: { Type.Components: [var argument] } opposite } row &&
                ArithmeticContractSyntax.Identity(opposite) == oppositeId && this.IdentityProven(argument, subject, view))
            {
                found = row;
            }
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        return found;
    }

    private void ReportArithmeticSelection(Koto use, DiagnosticCollection? store, DiagnosticRequirement requirement, ArithmeticSelectionFailure failure)
    {
        var related = new (string Role, Koto Node, string? Message)[failure.Plan.Candidates.Count];
        for (var i = 0; i < related.Length; i++)
        {
            related[i] = ("conformance", failure.Plan.Sources[i], DiagnosticTypeName(failure.Plan.Candidates[i].Type!));
        }

        var counterpartText = failure.Counterpart is { } counterpart ? DiagnosticTypeName(counterpart)
            : use is UnaryKoto ? "none (unary operation)" : "not independently typed";
        var note = failure.Cause switch
        {
            ArithmeticSelectionCause.NonNumericLeft => "A right provider requires a built-in numeric left operand",
            ArithmeticSelectionCause.Unproven => "A structurally fitting conditional conformance remains unproven",
            ArithmeticSelectionCause.NoFit => "No proven conformance fits the counterpart structure",
            ArithmeticSelectionCause.Ambiguous => "Multiple proven conformances fit the counterpart structure",
            _ => throw new UnreachableException(),
        };
        use.ReportIn(
            store,
            requirement,
            DiagnosticCode.ArithmeticSelection_Kd,
            ArithmeticOperatorText(ArithmeticContractSyntax.Operator(failure.Id)),
            this.Library.GetSymbol(failure.Id)!.Name,
            note: note,
            evidence: [DiagnosticTypeName(failure.Self), counterpartText, failure.Cause],
            related: related);
    }
}
