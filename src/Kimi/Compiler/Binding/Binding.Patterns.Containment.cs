// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Pattern containment (SPEC 14.8.4): the unreachable-Pattern warning of a match whose coverage is decided.
// - Responsibility: for each arm, the first earlier unguarded arm whose Pattern contains its Pattern (PatternWarning), in arm order.
// - Inputs: the plan's positions and arms after every Pattern Type check (CompletePatternAcquisitions).
// - Outputs: Binding.PatternWarnings; PatternContainmentWork, the pairwise checks of the pass.
// - Invariants: ContainsPattern is the one containment rule; a guarded arm contains nothing.
// - Prohibitions: no second containment rule, no change of a warning's Pattern, covering arm or order.
public sealed partial class Binding
{
    /// <summary>Gets the containment work of the current pass: the pairwise checks.</summary>
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

    private void WarnContainedPatterns(BoundMatch plan)
    {
        for (var later = 1; later < plan.Arms.Count; later++)
        {
            for (var earlier = 0; earlier < later; earlier++)
            {
                if (plan.Arms[earlier].Syntax.Guard is not null)
                {
                    continue;
                }

                this.PatternContainmentWork++;
                if (ContainsPattern(plan, plan.Arms[earlier].Pattern, plan.Arms[later].Pattern))
                {
                    this.patternWarnings.Add(new(plan.Arms[later].Syntax.Pattern, earlier));
                    break;
                }
            }
        }
    }
}
