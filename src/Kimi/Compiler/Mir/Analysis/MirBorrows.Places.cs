// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Place steps (A3 3), which MirSteps classes: the walk buffer every caller reads its steps from, its last borrow or raw Deref
// (LastDeref, the one test of that step class), and Through.
internal sealed partial class MirBorrows
{
    private readonly MirSteps placeSteps = new(cases);
    private MirStep[] walk = []; // A Place's steps.

    // Whether a Place passes a borrow or raw Deref, so writing it uses its root.
    private bool Through(MirPlace place) => this.LastDeref(this.placeSteps.Walk(place, reborrow: false, ref this.walk)) >= 0;

    // The last borrow or raw Deref among the walk buffer's first `count` steps, -1 for none.
    private int LastDeref(int count)
    {
        var last = count - 1;
        while (last >= 0 && this.walk[last].Class is not (MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw))
        {
            last--;
        }

        return last;
    }
}
