// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

// The entries of Layout's placement and their one decision (DECISIONS R5-M37, R5-M52): a placed Type's entry, its answer with its height,
// size and alignment, and the fold of one placement's parts whose decision owns the outcome's precedence; Layout.Placement.cs places the
// parts and memoizes the entries.
internal sealed partial class Layout
{
    // A placed Type's answer, placement height (an exhausted entry's is its budget + 1), size and alignment. Invariants: Alignment is 0
    // exactly when the size is not exact (a lower bound, or Over), and a measure reads it only through Math.Max(Alignment, 1); Size is Over
    // exactly when the answer is Oversized or a CLayout over the bound, the one fact Result.Oversized publishes.
    private readonly record struct Entry(Result Result, int Height, ulong Size, int Alignment)
    {
        internal bool Exact => this.Alignment > 0;

        // The one construction of an entry: Result.Oversized derives from the size.
        internal static Entry Of(Outcome outcome, ValueLowering? value, BoundType? instance, int height, ulong size, int alignment)
            => new(new(outcome, value, instance, size == Over), height, size, alignment);
    }

    // One placement's parts folded in placement order and its one decision, the owner of the precedence (DECISIONS R5-M52, amending
    // R5-M37): the first CLayout in the window, then Oversized (a size over 2^63 - 1, a lower bound sufficing, or an oversized part), then
    // DepthLimit when a part reaches past the budget, then the first limit (the parts', then the Type's own count), then the first Rejected,
    // then the Type's own size.
    private struct Fold
    {
        private readonly BoundType type;
        private BoundType? clayout;
        private BoundType? rejected;
        private Outcome limit;
        private int height;
        private bool exact;
        private bool over;

        internal Fold(BoundType type) => (this.type, this.exact) = (type, true);

        // Folds a part's entry and returns it; a CLayout part names its own instance, and any other exactly zero-sized one, whatever its
        // outcome, makes a C-layout struct CLayout (KC7).
        internal Entry Add(Entry part, bool cField)
        {
            this.height = Math.Max(this.height, part.Height);
            this.exact &= part.Exact;
            this.over |= part.Size == Over;
            var result = part.Result;
            if (result.Outcome == Outcome.CLayout)
            {
                this.clayout ??= result.Instance;
            }
            else if (cField && part.Exact && part.Size == 0)
            {
                this.clayout ??= this.type;
            }
            else if (result.Outcome == Outcome.Rejected)
            {
                this.rejected ??= result.Instance;
            }
            else if (result.Outcome is Outcome.SizeLimit or Outcome.CountLimit && this.limit == Outcome.Ok)
            {
                this.limit = result.Outcome;
            }

            return part;
        }

        // A stored Type Binding did not resolve: nothing placed, no level, no known size.
        internal void Reject() => (this.rejected, this.exact) = (this.rejected ?? this.type, false);

        // SPEC 6.7.1: a struct whose declaration selects C layout but gives none (open, derived or empty) is CLayout itself, before its parts.
        internal void Unavailable() => this.clayout = this.type;

        // The decided entry of parts placed with `budget` levels left and measured at `size`, or null when every part is laid out and the
        // Type's own size admits its record.
        internal readonly Entry? Decide(int budget, UInt128 size, int alignment, Outcome ownCount)
        {
            var stored = this.over || size >= Over ? Over : (ulong)size;
            var known = this.exact && stored < Over ? alignment : 0;
            var height = this.height > budget ? budget + 2 : this.height + 1;
            if (this.clayout is not null)
            {
                return Entry.Of(Outcome.CLayout, null, this.clayout, height, stored, known);
            }

            if (stored == Over)
            {
                return Entry.Of(Outcome.Oversized, null, null, height, Over, 0);
            }

            if (this.height > budget)
            {
                return Entry.Of(Outcome.DepthLimit, null, null, height, stored, 0);
            }

            var limited = this.limit == Outcome.Ok ? ownCount : this.limit;
            if (limited != Outcome.Ok)
            {
                return Entry.Of(limited, null, null, height, stored, known);
            }

            if (this.rejected is not null)
            {
                return Entry.Of(Outcome.Rejected, null, this.rejected, height, stored, 0);
            }

            return size > MaximumSize ? Entry.Of(Outcome.SizeLimit, null, null, height, stored, known) : null;
        }

        // The record the Type's own size admitted.
        internal readonly Entry Laid(ValueLowering record, UInt128 size, int alignment)
            => Entry.Of(Outcome.Ok, record, null, this.height + 1, (ulong)size, alignment);
    }
}
