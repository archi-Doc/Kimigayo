// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Comparisons read fixed facts and are asymmetric; they need not be transitive.
internal interface IStrictCandidateOrder
{
    bool IsEligible(int candidate);

    bool Better(int left, int right);
}

internal static class StrictBestCandidate
{
    internal static int Select<T>(int count, in T order)
        where T : IStrictCandidateOrder, allows ref struct
    {
        var selected = -1;
        for (var i = 0; i < count; i++)
        {
            if (order.IsEligible(i) && (selected < 0 || order.Better(i, selected)))
            {
                selected = i;
            }
        }

        if (selected >= 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (i != selected && order.IsEligible(i) && !order.Better(selected, i))
                {
                    return -1;
                }
            }
        }

        return selected;
    }
}
