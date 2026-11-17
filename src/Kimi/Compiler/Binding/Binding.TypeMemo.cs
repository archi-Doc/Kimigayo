// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns the per-query answer table of Binding's walks over a Type's parts (DECISIONS BU-19). An interned Type is a DAG, so a walk reaches
// a shared part along many paths; each walk that uses a table answers a function of the part and of its query's fixed inputs, so it
// decides each distinct part once per query and stays linear in the distinct parts. A walk whose visit of a part also depends on a Type
// beside it keys its entry by the pair (part, beside), so it decides each distinct pair once per query and stays linear in the distinct
// pairs; a walk of the part alone keys it by the part. A key compares by its own equality, which for a Type is identity: a Type is
// interned, and a Closure Type holds its whole context (BU-19, BU-60, BU-71). A walk whose visit depends on a further input stores, as
// the value, the input the key was last met with (VisitWellFormedPremises' substituted part, DECISIONS R4-M164 U1e): a meet is a hit only
// when the caller's comparison with it holds, and otherwise revisits the key and overwrites it.
// Input: the key (a part, or a part and the Type beside it) and the stamp of its query. Output: the answer, or the input, that query
// recorded for the key, if any.
// Invariants: Begin gives every query a stamp no other query of the table has, so an entry of another query, pass or edit is stale and
// never read; a query begun during another (a nested entry call) only evicts the outer query's entries, which it then recomputes; the
// table drops its Types when a new pass begins a query and keeps its capacity, so a warm pass allocates nothing.
// Must not carry an answer past its query, or serve two walks with different inputs under one stamp.
public sealed partial class Binding
{
    private readonly TypeMemo<BoundType, BoundType?> substitutedParts = new(
        KimiLibrarySize.KeyedCapacity(KimiLibrarySize.Stated.SubstitutedParts)); // SubstituteType's images.

    private readonly TypeMemo<BoundType, BoundType?> closedParts = new(); // CloseClosureTypes' images.

    // The images of the leaf rewrites, WithoutRawOrigins, WithUnstatedOrigins, WithoutUnstated, SubstituteStoredOrigins and PublishedResult
    // (DECISIONS BU-58, BU-63): none calls a user of this table while it walks, so a query never begins inside another and evicts nothing
    // live.
    private readonly TypeMemo<BoundType, BoundType?> rewrittenParts = new();

    // The states in which a walk met a part, one bit per state (DECISIONS BU-63): the existence walks OperandContexts,
    // ContainsAssociatedProjection, DependentType, TypeAccessCovers, ProveTypeLengths, HasLocalRegion and HasRegionBounds, and the walks
    // whose revisit only repeats what they add, CollectRequirementEdges, AddStoredPremises, ClosureType's Collect, and AccumulateRequirements
    // and ClosureArgumentVariance's Occurrences (a bit per polarity).
    private readonly TypeMemo<BoundType, byte> visitedParts = new();

    private sealed class TypeMemo<TKey, T>(int capacity = 0)
        where TKey : notnull
    {
        private readonly Dictionary<TKey, (uint Query, T Answer)> answers = new(capacity);
        private ulong pass;
        private uint query;

        internal int Count => this.answers.Count;

        internal int Free => this.answers.Capacity - this.answers.Count;

        // Begins a query of a Binding pass: its unique stamp.
        internal uint Begin(ulong pass)
        {
            if (pass != this.pass || this.query == uint.MaxValue)
            {
                this.answers.Clear();
                this.pass = pass;
                this.query = 0;
            }

            return ++this.query;
        }

        internal bool TryGet(TKey key, uint query, out T answer)
        {
            if (this.answers.TryGetValue(key, out var entry) && entry.Query == query)
            {
                answer = entry.Answer;
                return true;
            }

            answer = default!;
            return false;
        }

        internal void Set(TKey key, uint query, T answer) => this.answers[key] = (query, answer);
    }
}
