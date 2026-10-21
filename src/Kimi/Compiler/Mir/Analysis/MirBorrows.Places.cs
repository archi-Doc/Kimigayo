// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Place steps (A3 3), which MirSteps classes: the walk buffer every caller reads its steps from, and Through.
internal sealed partial class MirBorrows
{
    private readonly MirSteps placeSteps = new(cases);
    private MirStep[] walk = []; // A Place's steps.

    // Whether a Place passes a borrow or raw Deref, so writing it uses its root.
    private bool Through(MirPlace place)
    {
        var count = this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class is MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw)
            {
                return true;
            }
        }

        return false;
    }
}
