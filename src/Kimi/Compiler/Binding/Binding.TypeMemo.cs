// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns the per-query answer table of Binding's walks over a Type's parts (DECISIONS BU-19). An interned Type is a DAG, so a walk reaches
// a shared part along many paths; each walk that uses a table answers a function of the part and of its query's fixed inputs, so it
// decides each distinct part once per query and stays linear in the distinct parts.
// Input: a part and the stamp of its query. Output: the answer that query recorded for the part, if any.
// Invariants: Begin gives every query a stamp no other query of the table has, so an entry of another query, pass or edit is stale and
// never read; a query begun during another (a nested entry call) only evicts the outer query's entries, which it then recomputes; the
// table drops its Types when a new pass begins a query and keeps its capacity, so a warm pass allocates nothing.
// Must not carry an answer past its query, or serve two walks with different inputs under one stamp.
public sealed partial class Binding
{
    private readonly TypeMemo<BoundType?> substitutedParts = new(); // SubstituteType's images.
    private readonly TypeMemo<BoundType?> closedParts = new(); // CloseClosureTypes' images.

    // The images of the leaf rewrites, WithoutRawOrigins, WithUnstatedOrigins, WithoutUnstated, SubstituteStoredOrigins and PublishedResult
    // (DECISIONS BU-58, BU-63): none calls a user of this table while it walks, so a query never begins inside another and evicts nothing
    // live.
    private readonly TypeMemo<BoundType?> rewrittenParts = new();

    // The states in which a walk met a part, one bit per state (DECISIONS BU-63): the existence walks OperandContexts,
    // ContainsAssociatedProjection, DependentType, TypeAccessCovers, ProveTypeLengths, HasLocalRegion and HasRegionBounds, and the walks
    // whose revisit only repeats what they add, CollectRequirementEdges, AddStoredPremises and AccumulateRequirements (a bit per polarity).
    private readonly TypeMemo<byte> visitedParts = new();

    // Whether a walk of contractParts, rewrittenParts or visitedParts answers a part again rather than recording it (DECISIONS BU-63): a
    // leaf, or a part of at most two components that are leaves. Answering one again is a bounded step for each component edge that
    // reaches it, so the walk stays linear in its DAG, and it saves the two table operations a record costs: such parts are most of the
    // parts a program's walks meet, and a query rarely meets one twice.
    private static bool AnsweredAgain(BoundType part)
    {
        var components = part.Components;
        return components.Count == 0 || (components.Count <= 2 && components[0].Components.Count == 0 && components[^1].Components.Count == 0);
    }

    private sealed class TypeMemo<T>
    {
        private readonly Dictionary<BoundType, (uint Query, T Answer)> answers = new(ReferenceEqualityComparer.Instance);
        private ulong pass;
        private uint query;

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

        internal bool TryGet(BoundType part, uint query, out T answer)
        {
            if (this.answers.TryGetValue(part, out var entry) && entry.Query == query)
            {
                answer = entry.Answer;
                return true;
            }

            answer = default!;
            return false;
        }

        internal void Set(BoundType part, uint query, T answer) => this.answers[part] = (query, answer);
    }
}
