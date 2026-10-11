// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Checking;

#pragma warning disable SA1402, SA1600, SA1649 // Internal policy and its allocation-free recursive guard.

internal static class HoverLimits
{
    internal const int Depth = 64;
    internal const int Work = 1_048_576;
    internal const int Input = 65_536;
    internal const int Output = 65_536;
    internal const int CacheEntries = 1024;
    internal const int CacheCharacters = 1_048_576;
    internal const int Edits = 256;
}

internal sealed class HoverLimitException(string message) : Exception(message);

internal sealed class HoverBudget(int maximumWork = HoverLimits.Work, int maximumDepth = HoverLimits.Depth)
{
    private int remaining = maximumWork;
    private int depth;

    internal void Charge(int cost = 1)
    {
        if (cost < 0 || cost > this.remaining)
        {
            throw new HoverLimitException("Hover work limit exceeded");
        }

        this.remaining -= cost;
    }

    internal Scope Enter()
    {
        this.Charge();
        if (this.depth >= maximumDepth)
        {
            throw new HoverLimitException("Hover depth limit exceeded");
        }

        this.depth++;
        return new(this);
    }

    internal readonly struct Scope(HoverBudget owner) : IDisposable
    {
        public void Dispose() => owner.depth--;
    }
}
