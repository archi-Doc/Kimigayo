// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Position rows (SPEC 10.5.1, 14.9.2 step 2, 15.6.4 step 3; DECISIONS BU-94, BU-99, BU-104; the Position Rows proposal §4): what each
// position of a call's argument is to its call, one row per position.
// - Responsibility: the one representation of a position's contribution (F1, R4-M164): a root row per argument of each candidate, the
//   argument's own position, and the child rows of an argument, the positions of its composition (ChildPositions, AddChildPosition),
//   composed once per candidate at its first solve (RetainChildRows, the probe's state) and retained, which every later solve of the
//   candidate (CollectPositionPremises) and its selected call's judgment (JudgePositionRows) read, each leaf at its own sub-Type; both
//   readers stay here until U5's shared relation judge.
// - Inputs: the arguments, the candidate's parameter patterns, solution and contributions (SlotSolution.Contribute, ContributePayloads);
//   the position composition (Binding.ArgumentKinds.Positions.cs) and each part's contribution (PartContribution) for child rows.
// - Outputs: PositionRow; SlotSolution.Rows, the candidate's root rows, a slice of CallEvaluation.Rows (rowScratch); childRows, the
//   retained child rows, each candidate's slice (SlotSolution.ChildSlice: start and count, count -1 until composed); positionRows, a
//   judgment's reading of one argument's rows with their Types.
// - Invariants: a row's Pattern is the parameter pattern at its position as the candidate reads it; a retained row holds only structure
//   (Argument, Parent relative to its slice, Index under its parent, Part), Pattern and Contribution, and a reading derives Types: a solve
//   instantiates Pattern under its own solution, the judgment top-down from the operation's Type by Index. Rows are in pre-order with a
//   row's children right after it (IsLeaf), grouped by argument; a slice is written once, by its candidate's first solve, and a re-run
//   or the judgment composes nothing; a part's failure is read at the reading (rule W, SPEC 23.1.2). A position whose pattern is not
//   shaped like its Type, a slot's (R1), has no child rows, since its solution's parts are its contributor's (the premise visits stop
//   there likewise where no evidence is supplied). A candidate's composition may stop early on a trial whose literal shape does not
//   fit, which the fit rejects; the selected call's reading fails Internal where a child's Type is not derivable. Root rows have the
//   stride and lifetime of the call's candidate evaluation (a construction's, a fixed construction's re-check's and a property update's
//   rows their own), and rowScratch buffers are returned cleared; an evaluation's child rows are removed from childRows when it ends.
// - Prohibitions: no fit or evidence here, and no solve beyond handing each leaf's premise to it (CollectInputPremises,
//   DiscoverPremiseOrigins); no composition after a candidate's first solve, and no bound read from a completed part's Type; the
//   composition stays the positions' (Binding.ArgumentKinds.Positions.cs).
public sealed partial class Binding
{
    private readonly ScratchBuffers<PositionRow> rowScratch = new();
    private readonly List<PositionRow> positionRows = new();
    private readonly List<PositionRow> childRows = new();

    // A position of a call's argument: Argument, the call argument's index; Parent, the index in its slice of the enclosing position's
    // row, -1 for a root row and for a child of one (Rows[Argument]); Index, the child position's index under its parent (a Tuple or
    // array element's, a join's results 0); Part, the position's part (the argument for a root row; a Tuple or array element or a join's
    // result part for a child row); Pattern, the parameter Type there as the candidate reads it (SlotSolution.Pattern; a payload's
    // declared Type; a property update's operation Type), a child row's its parent's sub-pattern; Type, the reading's instantiation of
    // Pattern (null in a retained child row and where the substitution fails: no bound); Contribution, what the position contributes to
    // its call: Evidence where the position has a Type of its own (a typed part, an independently typed join), else Signature.
    private readonly record struct PositionRow(int Argument, int Parent, int Index, Koto Part, BoundType? Pattern, BoundType? Type, Contribution Contribution);

    // A construction's or a re-check's own slice, unset (R3).
    private static Span<int> ResetSlice(int[] slice)
    {
        slice[0] = 0;
        slice[1] = -1;
        return slice.AsSpan(0, 2);
    }

    // Whether retained row `j` of a slice ending at `end` has no child row: the next row is not its child (pre-order).
    private bool IsLeaf(int sliceStart, int j, int end)
        => j + 1 >= end || this.childRows[j + 1].Parent != j - sliceStart;

    // The retained rows of `argument` in the candidate's slice, [start, end); empty when the argument's root has no child rows.
    private void ChildGroup(in SlotSolution solution, int argument, out int start, out int end)
    {
        start = end = 0;
        if (solution.ChildSlice.IsEmpty)
        {
            return; // A property update's rows: typed parts, no child positions.
        }

        var sliceEnd = solution.ChildSlice[0] + Math.Max(0, solution.ChildSlice[1]);
        start = solution.ChildSlice[0];
        while (start < sliceEnd && this.childRows[start].Argument < argument)
        {
            start++;
        }

        end = start;
        while (end < sliceEnd && this.childRows[end].Argument == argument)
        {
            end++;
        }
    }

    // R3 (DECISIONS BU-104): the candidate's child rows, composed once at its first solve, where the probe's facts (a waiting join, its
    // flattened nested joins, each part's recorded kind) are live, and retained in childRows for every later reading.
    private void RetainChildRows(ref SlotSolution solution)
    {
        if (solution.ChildSlice.IsEmpty || solution.ChildSlice[1] >= 0)
        {
            return;
        }

        for (var i = 0; i < solution.Rows.Length; i++)
        {
            this.RetainArgumentRows(ref solution, i);
        }
    }

    // Appends argument `argument`'s child rows to the solution's slice, after those of the arguments before it; a value call composes
    // each argument this way once its part is prepared and before it is completed (BindValueCall).
    private void RetainArgumentRows(ref SlotSolution solution, int argument)
    {
        if (solution.ChildSlice[1] < 0)
        {
            solution.ChildSlice[0] = this.childRows.Count;
            solution.ChildSlice[1] = 0;
        }

        if (solution.Rows[argument] is { Pattern: { } pattern, Type: { } type } root)
        {
            _ = this.ComposePositionRows(solution.ChildSlice[0], argument, -1, root.Part, pattern, type); // A trial's mismatch: the fit's.
        }

        solution.ChildSlice[1] = this.childRows.Count - solution.ChildSlice[0];
    }

    // R2 (SPEC 15.6.4 step 3; DECISIONS BU-99, BU-104): a candidate's solve is bounded by the premise of each leaf row of its arguments
    // that is a Signature position, at its own sub-Type under this solve's solution: the rows its selected call judges (JudgePositionRows),
    // read from the retained composition. The first such row begins the solve's inference, and its callee Origins that no evidence names
    // become premise-solved variables (DiscoverPremiseOrigins).
    private void CollectPositionPremises(ref SlotSolution solution)
    {
        this.RetainChildRows(ref solution);
        for (var i = 0; i < solution.Rows.Length; i++)
        {
            this.ChildGroup(solution, i, out var start, out var end);
            if (start == end)
            {
                this.CollectLeafPremises(ref solution, solution.Rows[i]);
                continue;
            }

            for (var j = start; j < end; j++)
            {
                if (this.IsLeaf(solution.ChildSlice[0], j, end) && this.childRows[j] is { Pattern: { } pattern } row)
                {
                    this.CollectLeafPremises(ref solution, row with { Type = this.SubstituteType(pattern, solution.SlotOwner, solution.Types, solution.Lengths) });
                }
            }
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

    // C3 (G216 (a) U2.2 U2; SPEC 10.5.1, 15.6.4 step 3; DECISIONS R4-M164, BU-99, BU-104): an argument `at` is judged by its positions'
    // rows, the candidate's retained composition read top-down from its root row at the Type `root.Type` of the operation: each leaf at
    // its own sub-Type by what it contributes, and the argument's own position by its root row when it has no child rows (`[]`, a
    // completed waiting part). No aggregate of the leaves is formed. Every record is at the argument (S1; the sites are U4's). A failed
    // part is a wildcard (rule W, SPEC 23.1.2). A child whose Type is not derivable fails the argument once, after the rows before it.
    private void JudgePositionRows(Koto at, in PositionRow root, in SlotSolution solution)
    {
        if (this.PartFailed(KotoHelper.UnwrapParentheses(at)))
        {
            return;
        }

        this.ChildGroup(solution, root.Argument, out var start, out var end);
        if (start == end)
        {
            this.JudgeLeafPosition(at, root);
            return;
        }

        var sliceStart = solution.ChildSlice[0];
        var first = this.positionRows.Count;
        var derived = true;
        for (var j = start; derived && j < end; j++)
        {
            var row = this.childRows[j];
            var parent = row.Parent < 0 ? root : this.positionRows[first + (row.Parent - (start - sliceStart))];
            var type = PartChildPattern(KotoHelper.UnwrapParentheses(parent.Part), row.Index, parent.Type);
            this.positionRows.Add(row with { Type = type });
            derived = type is not null;
        }

        for (var j = start; j < end && first + (j - start) < this.positionRows.Count; j++)
        {
            if (this.IsLeaf(sliceStart, j, end) && this.positionRows[first + (j - start)] is { Type: not null } leaf)
            {
                this.JudgeLeafPosition(at, leaf);
            }
        }

        this.positionRows.RemoveRange(first, this.positionRows.Count - first);
        if (!derived)
        {
            this.Fail(at, BindingFailure.Internal);
        }
    }

    // The rows of the child positions of `part`, whose position's pattern is `pattern` and Type `type`, appended to childRows (Parent
    // `parent`, relative to the slice at `sliceStart`). Beneath a pattern not shaped like the Type, a slot's, the children are composed
    // with no pattern (R5, DECISIONS BU-104): their Types are layers of the slot's solution, not the callee's pattern. False when a child
    // position's Type is not derivable: a trial whose literal shape does not fit its parameter Type, which the fit rejects.
    private bool ComposePositionRows(int sliceStart, int argument, int parent, Koto part, BoundType? pattern, BoundType type)
    {
        pattern = pattern is not null && pattern.Kind == type.Kind ? pattern : null;
        var start = this.OpenPosition(part);
        var core = KotoHelper.UnwrapParentheses(part);
        var count = this.ChildPositions(start, start + 1);
        for (var i = 0; i < count; i++)
        {
            var child = this.positionParts.Count;
            var element = this.AddChildPosition(start, start + 1, i, type);
            var elementPattern = pattern is null ? null : PartChildPattern(core, i, pattern);
            for (int j = child, end = this.positionParts.Count; element is not null && j < end; j++)
            {
                var node = this.positionParts[j];
                if (!this.PartFailed(KotoHelper.UnwrapParentheses(node)))
                {
                    this.childRows.Add(new(argument, parent, i, node, elementPattern, null, this.PartContribution(node)));
                    element = this.ComposePositionRows(sliceStart, argument, this.childRows.Count - 1 - sliceStart, node, elementPattern, element) ? element : null;
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
    // any other part, and an aggregate whose positions no part fills (`[]`), at every position (RequireInputPremises). Beneath a slot (no
    // pattern, R5) only an Evidence leaf is judged, against the solution's layer with its own Type beside it, where its Origin differs;
    // a Signature leaf there is the solution's contributor's (BU-99 R1). A part that failed after the composition is a wildcard (rule W).
    private void JudgeLeafPosition(Koto at, in PositionRow row)
    {
        var node = KotoHelper.UnwrapParentheses(row.Part);
        if (this.PartFailed(node) || (row.Pattern is null && row.Contribution != Contribution.Evidence))
        {
            return;
        }

        if (row.Contribution == Contribution.Signature || node.BoundType is not null)
        {
            this.RequireInputPremises(at, row.Pattern ?? row.Type!, row.Type!, row.Contribution == Contribution.Evidence ? node.BoundType : null, row.Contribution);
        }
    }
}
