// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Pattern containment (SPEC 14.8.4): the unreachable-Pattern warning of a match whose coverage is decided.
// - Responsibility: for each arm, the first earlier unguarded arm whose Pattern contains its Pattern (PatternWarning), in arm order.
// - Inputs: the plan's positions and arms after every Pattern Type check (CompletePatternAcquisitions).
// - Outputs: Binding.PatternWarnings; PatternContainmentWork, the positions keyed and the pairwise checks of the pass.
// - Invariants: ContainsPattern is the one containment rule. A Pattern without a Wildcard or a Binding contains exactly the
//   Patterns of its structure, so such arms are found by key; only an arm with a Wildcard or a Binding is compared pairwise, and
//   no arm after the first unguarded whole-position Wildcard or Binding is a candidate, since that arm contains every later Pattern.
// - Prohibitions: no second containment rule, no change of a warning's Pattern, covering arm or order.
public sealed partial class Binding
{
    private readonly StructureComparer patternStructures = new();
    private readonly List<int> generalArms = new();
    private Dictionary<int, int>? structureArms;

    /// <summary>Gets the containment work of the current pass: the Pattern positions keyed and the pairwise checks.</summary>
    internal long PatternContainmentWork { get; private set; }

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

    // Whether a Pattern has no Wildcard and no Binding: it contains a Pattern only of its own structure.
    private static bool IsStructure(BoundMatch plan, int pattern)
    {
        for (var i = pattern; i < plan.PositionStorage[pattern].End; i++)
        {
            if (plan.PositionStorage[i].Kind is BoundPatternKind.Wildcard or BoundPatternKind.Binding)
            {
                return false;
            }
        }

        return true;
    }

    private void WarnContainedPatterns(BoundMatch plan)
    {
        var structures = this.structureArms ??= new(this.patternStructures);
        var general = this.generalArms;
        this.patternStructures.Plan = plan;
        structures.Clear();
        general.Clear();
        var open = true; // No earlier unguarded arm contains every Pattern.
        for (var later = 0; later < plan.Arms.Count; later++)
        {
            var pattern = plan.Arms[later].Pattern;
            var covering = structures.GetValueOrDefault(pattern, -1);
            this.PatternContainmentWork += plan.PositionStorage[pattern].End - pattern;
            for (var i = 0; i < general.Count && (covering < 0 || general[i] < covering); i++)
            {
                this.PatternContainmentWork++;
                if (ContainsPattern(plan, plan.Arms[general[i]].Pattern, pattern))
                {
                    covering = general[i];
                    break;
                }
            }

            if (covering >= 0)
            {
                this.patternWarnings.Add(new(plan.Arms[later].Syntax.Pattern, covering));
            }

            if (!open || plan.Arms[later].Syntax.Guard is not null)
            {
                continue;
            }

            if (IsStructure(plan, pattern))
            {
                structures.TryAdd(pattern, later);
            }
            else
            {
                general.Add(later);
                open = plan.PositionStorage[pattern].Kind is not (BoundPatternKind.Wildcard or BoundPatternKind.Binding);
            }
        }

        this.patternStructures.Plan = null!;
    }

    // Pattern positions of one plan compared by structure: the kinds, Cases, Literal constants and extents of their preorder ranges,
    // what ContainsPattern compares when the earlier Pattern has no Wildcard and no Binding.
    private sealed class StructureComparer : IEqualityComparer<int>
    {
        public BoundMatch Plan { get; set; } = null!;

        public bool Equals(int x, int y)
        {
            var positions = this.Plan.PositionStorage;
            for (var end = positions[x].End; x < end; x++, y++)
            {
                var a = positions[x];
                var b = positions[y];
                if (a.Kind != b.Kind || a.Case != b.Case || a.End - x != b.End - y || (a.Kind == BoundPatternKind.Literal && a.Literal != b.Literal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(int position)
        {
            var positions = this.Plan.PositionStorage;
            var hash = default(HashCode);
            for (var end = positions[position].End; position < end; position++)
            {
                var p = positions[position];
                hash.Add((int)p.Kind);
                hash.Add(p.Case);
                hash.Add(p.End - position);
                if (p.Kind == BoundPatternKind.Literal)
                {
                    hash.Add(p.Literal);
                }
            }

            return hash.ToHashCode();
        }
    }
}
