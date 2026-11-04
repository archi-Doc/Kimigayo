// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Position rows (SPEC 10.5.1, 14.9.2 step 2, 15.6.4 step 3; DECISIONS BU-94; the Position Rows proposal §4): what each position of a
// call's argument is to its call, one row per position.
// - Responsibility: the one representation of a position's contribution (F1, R4-M164): a root row per argument of each candidate, the
//   argument's own position, and the child rows of a selected call's argument, the positions of its composition (ChildPositions,
//   AddChildPosition), which its judgment reads (JudgePositionRows).
// - Inputs: the arguments, the candidate's parameter patterns and its contributions (SlotSolution.Contribute, ContributePayloads); the
//   position composition (Binding.ArgumentKinds.Positions.cs) and each part's contribution (PartContribution) for child rows.
// - Outputs: PositionRow; SlotSolution.Rows, the candidate's root rows, a slice of CallEvaluation.Rows (rowScratch); positionRows, the
//   child rows of the argument being judged.
// - Invariants: root rows have the stride and lifetime of the call's candidate evaluation (a construction's, a fixed construction's
//   re-check's and a property update's rows their own), and rowScratch buffers are returned cleared, so a rented one holds Evidence
//   rows; child rows are appended in the order the judgment reads them (a row before its children's, positions by index) and removed
//   when the argument's judgment ends; a failed part has no row (rule W, SPEC 23.1.2).
// - Prohibitions: no fit, evidence or solve here; the composition stays the positions' (Binding.ArgumentKinds.Positions.cs).
public sealed partial class Binding
{
    private readonly ScratchBuffers<PositionRow> rowScratch = new();
    private readonly List<PositionRow> positionRows = new();

    // A position of a call's argument: Argument, the call argument's index; Parent, the index in positionRows of the enclosing
    // position's row, -1 for a root row and for a child of one (Rows[Argument]); Part, the position's part (the argument for a root
    // row; a Tuple or array element or a join's result part for a child row); Kind, the part's kind (KindOf; informational until U2);
    // Pattern, the parameter Type there (a root row's unsubstituted parameter pattern, which the solution's Type instantiates, and a
    // property update's its operation's; a child row's sub-Type of the instantiated Type its walk was given); Contribution, what the
    // position contributes to its call.
    private readonly record struct PositionRow(int Argument, int Parent, Koto Part, PartKind Kind, BoundType? Pattern, Contribution Contribution);

    // C3 (G216 (a) U2.2 U2; SPEC 10.5.1, 15.6.4 step 3; DECISIONS R4-M164): an argument `at` with no adapted Type of its own, an
    // aggregate, is judged by its positions' rows at its Type `type`: each leaf, a child row with no child position, at its own sub-Type
    // by what it contributes, and the argument's own position by its root row when it has no child position (`[]`, a completed waiting
    // part). Every record is at the argument (S1; the sites are U3's). A failed part is a wildcard (rule W, SPEC 23.1.2).
    private void JudgePositionRows(Koto at, in PositionRow root, BoundType type)
    {
        if (this.PartFailed(KotoHelper.UnwrapParentheses(at)))
        {
            return;
        }

        var first = this.positionRows.Count;
        if (!this.ComposePositionRows(root.Argument, -1, at, type))
        {
            this.Fail(at, BindingFailure.Internal);
        }
        else if (this.ChildPositions(at) == 0)
        {
            this.JudgeLeafPosition(at, at, type, root.Contribution);
        }

        for (var i = first; i < this.positionRows.Count; i++)
        {
            var row = this.positionRows[i];
            if (this.ChildPositions(row.Part) == 0)
            {
                this.JudgeLeafPosition(at, row.Part, row.Pattern!, row.Contribution);
            }
        }

        this.positionRows.RemoveRange(first, this.positionRows.Count - first);
    }

    // The rows of the child positions of `part`, whose position's parameter Type is `type`, appended to positionRows (Parent `parent`);
    // false when a child position's parameter Type is not derivable: the fit (FitComposite) checked the literal's shape against `type`,
    // so that is a broken contract, InternalInvariant_Kd at the argument (U2's review N6, R4-M164 U1d).
    private bool ComposePositionRows(int argument, int parent, Koto part, BoundType type)
    {
        var start = this.OpenPosition(part);
        var count = this.ChildPositions(start, start + 1);
        for (var i = 0; i < count; i++)
        {
            var child = this.positionParts.Count;
            var element = this.AddChildPosition(start, start + 1, i, type);
            for (int j = child, end = this.positionParts.Count; element is not null && j < end; j++)
            {
                var node = this.positionParts[j];
                if (!this.PartFailed(KotoHelper.UnwrapParentheses(node)))
                {
                    this.positionRows.Add(new(argument, parent, node, this.KindOf(node), element, this.PartContribution(node)));
                    element = this.ComposePositionRows(argument, this.positionRows.Count - 1, node, element) ? element : null;
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
    private void JudgeLeafPosition(Koto at, Koto part, BoundType type, Contribution contribution)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        if (contribution == Contribution.Signature || node.BoundType is not null)
        {
            this.RequireInputPremises(at, type, contribution == Contribution.Evidence ? node.BoundType : null, contribution);
        }
    }
}
