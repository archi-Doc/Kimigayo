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
    private readonly TypeMemo<bool> visitedParts = new(); // The parts an existence walk met (Binding.ConstraintOperands, DependentType, ProveTypeLengths).

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
