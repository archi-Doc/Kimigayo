// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Position rows (SPEC 10.5.1, 14.9.2 step 2, 15.6.4 step 3; DECISIONS BU-94, BU-95; the Position Rows proposal §4): what each position of a
// call's argument is to its call, one row per position.
// - Responsibility: the one representation of a position's contribution (F1, R4-M164): a root row per argument of each candidate, the
//   argument's own position, and the child rows of an argument, the positions of its composition (ChildPositions, AddChildPosition),
//   which the candidate's solve (CollectPositionPremises) and its selected call's judgment (JudgePositionRows) read alike, each leaf at
//   its own sub-Type; both readers stay here until U5's shared relation judge.
// - Inputs: the arguments, the candidate's parameter patterns, solution and contributions (SlotSolution.Contribute, ContributePayloads);
//   the position composition (Binding.ArgumentKinds.Positions.cs) and each part's contribution (PartContribution) for child rows.
// - Outputs: PositionRow; SlotSolution.Rows, the candidate's root rows, a slice of CallEvaluation.Rows (rowScratch); positionRows, the
//   child rows of the argument being read.
// - Invariants: a row's Pattern is the parameter pattern at its position as the candidate reads it, and its Type the Type that
//   instantiates it; a position whose pattern is not shaped like its Type, a slot's (R1), has no child rows, since its solution's parts
//   are its contributor's (the premise visits stop there likewise). Root rows have the stride and lifetime of the call's candidate
//   evaluation (a construction's, a fixed construction's re-check's and a property update's rows their own), and rowScratch buffers are
//   returned cleared, so a rented one holds Evidence rows; child rows are appended in the order their readers read them (a row before
//   its children's, positions by index) and removed when the argument's reading ends; a failed part has no row (rule W, SPEC 23.1.2).
// - Prohibitions: no fit, evidence or solve here; the composition stays the positions' (Binding.ArgumentKinds.Positions.cs).
public sealed partial class Binding
{
    private readonly ScratchBuffers<PositionRow> rowScratch = new();
    private readonly List<PositionRow> positionRows = new();

    // A position of a call's argument: Argument, the call argument's index; Parent, the index in positionRows of the enclosing
    // position's row, -1 for a root row and for a child of one (Rows[Argument]); Part, the position's part (the argument for a root
    // row; a Tuple or array element or a join's result part for a child row); Pattern, the parameter Type there as the candidate reads it
    // (SlotSolution.Pattern; a payload's declared Type; a property update's operation Type), a child row's its parent's sub-pattern;
    // Type, the Type that instantiates Pattern under the solution its reader was given (null where the substitution fails: no bound);
    // Contribution, what the position contributes to its call.
    private readonly record struct PositionRow(int Argument, int Parent, Koto Part, BoundType? Pattern, BoundType? Type, Contribution Contribution);

    // R2 (SPEC 15.6.4 step 3; DECISIONS BU-95): a candidate's solve is bounded by the premise of each leaf row of its arguments that is a
    // Signature position, at its own sub-Type: the rows its selected call judges (JudgePositionRows). The first such row begins the
    // solve's inference. A trial whose parameter Type the literal's shape does not fit (ComposePositionRows false) is bounded by the
    // rows before the mismatch; the fit rejects the candidate.
    private void CollectPositionPremises(ref SlotSolution solution)
    {
        for (var i = 0; i < solution.Rows.Length; i++)
        {
            var root = solution.Rows[i];
            if (root is not { Pattern: { } pattern, Type: { } type })
            {
                continue;
            }

            var first = this.positionRows.Count;
            if (this.ComposePositionRows(i, -1, root.Part, pattern, type) && this.ChildPositions(root.Part) == 0)
            {
                this.CollectLeafPremises(ref solution, root);
            }

            for (var j = first; j < this.positionRows.Count; j++)
            {
                if (this.ChildPositions(this.positionRows[j].Part) == 0)
                {
                    this.CollectLeafPremises(ref solution, this.positionRows[j]);
                }
            }

            this.positionRows.RemoveRange(first, this.positionRows.Count - first);
        }
    }

    private void CollectLeafPremises(ref SlotSolution solution, in PositionRow row)
    {
        if (row is { Contribution: Contribution.Signature, Pattern: { StoresOrigin: true } pattern, Type: { } type })
        {
            solution.OriginInference ??= this.BeginOriginInference(solution.Call, solution.Binder);
            this.DiscoverPremiseOrigins(solution, solution.OriginInference, type);
            this.CollectInputPremises(pattern, type, solution.OriginInference, solution.Call);
        }
    }

    // C3 (G216 (a) U2.2 U2; SPEC 10.5.1, 15.6.4 step 3; DECISIONS R4-M164, BU-95): an argument `at` with no adapted Type of its own, an
    // aggregate, is judged by its positions' rows, from its root row at the Type `root.Type` that instantiates its pattern: each leaf, a
    // child row with no child position, at its own sub-Type by what it contributes, and the argument's own position by its root row when
    // it has no child position (`[]`, a completed waiting part). Every record is at the argument (S1; the sites are U4's). A failed part
    // is a wildcard (rule W, SPEC 23.1.2). A composition whose Type is underivable is judged by the rows before it, then fails once.
    private void JudgePositionRows(Koto at, in PositionRow root)
    {
        if (this.PartFailed(KotoHelper.UnwrapParentheses(at)))
        {
            return;
        }

        var first = this.positionRows.Count;
        var composed = this.ComposePositionRows(root.Argument, -1, at, root.Pattern!, root.Type!);
        if (composed && this.ChildPositions(at) == 0)
        {
            this.JudgeLeafPosition(at, root);
        }

        for (var i = first; i < this.positionRows.Count; i++)
        {
            if (this.ChildPositions(this.positionRows[i].Part) == 0)
            {
                this.JudgeLeafPosition(at, this.positionRows[i]);
            }
        }

        this.positionRows.RemoveRange(first, this.positionRows.Count - first);
        if (!composed)
        {
            this.Fail(at, BindingFailure.Internal);
        }
    }

    // The rows of the child positions of `part`, whose position's pattern is `pattern` and Type `type`, appended to positionRows (Parent
    // `parent`); none at a pattern not shaped like the Type, a slot's (R1). False when a child position's Type is not derivable: the fit
    // (FitComposite) checked the literal's shape against the selected call's Type, so in its judgment that is a broken contract,
    // InternalInvariant_Kd at the argument (U2's review N6, R4-M164 U1d).
    private bool ComposePositionRows(int argument, int parent, Koto part, BoundType pattern, BoundType type)
    {
        if (pattern.Kind != type.Kind)
        {
            return true;
        }

        var start = this.OpenPosition(part);
        var core = KotoHelper.UnwrapParentheses(part);
        var count = this.ChildPositions(start, start + 1);
        for (var i = 0; i < count; i++)
        {
            var child = this.positionParts.Count;
            var element = this.AddChildPosition(start, start + 1, i, type);
            var elementPattern = PartChildPattern(core, i, pattern);
            for (int j = child, end = this.positionParts.Count; element is not null && j < end; j++)
            {
                var node = this.positionParts[j];
                if (!this.PartFailed(KotoHelper.UnwrapParentheses(node)))
                {
                    this.positionRows.Add(new(argument, parent, node, elementPattern, element, this.PartContribution(node)));
                    element = this.ComposePositionRows(argument, this.positionRows.Count - 1, node, elementPattern!, element) ? element : null;
                }
            }

            if (element is null)
            {
                this.ClosePosition(start);
                return false;
            }

            this.ClosePosition(child);
        }

        this.ClosePosition(start);
        return true;
    }

    // A leaf position's well-formedness at its parameter Type (SPEC 15.6.4 step 3): a typed part only where its fit replaced an Origin;
    // any other part, and an aggregate whose positions no part fills (`[]`), at every position (RequireInputPremises).
    private void JudgeLeafPosition(Koto at, in PositionRow row)
    {
        var node = KotoHelper.UnwrapParentheses(row.Part);
        if (row.Contribution == Contribution.Signature || node.BoundType is not null)
        {
            this.RequireInputPremises(at, row.Pattern!, row.Type!, row.Contribution == Contribution.Evidence ? node.BoundType : null, row.Contribution);
        }
    }
}
