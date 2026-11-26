// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns the per-query answer table of Binding's walks over a Type's parts (DECISIONS BU-19), and the query number's one step (NextQuery),
// which StructuralCompletion's query memo, the HIR publisher's publication number (DECISIONS D-986) and the prerequisite walks'
// number (R7-N5 E) advance by too.
// An interned Type is a DAG, so a walk reaches
// a shared part along many paths; each walk that uses a table answers a function of the part and of its query's fixed inputs, so it
// decides each distinct part once per query and stays linear in the distinct parts. A walk whose visit of a part also depends on a Type
// beside it keys its entry by the pair (part, beside), so it decides each distinct pair once per query and stays linear in the distinct
// pairs; a walk of the part alone keys it by the part. A key compares by its own equality, which for a Type is identity: a Type is
// interned, and a Closure Type holds its whole context (BU-19, BU-60, BU-71). A walk whose visit depends on a further input stores, as
// the value, the input the key was last met with (WellFormedPremises' substituted part, DECISIONS R4-M164 U1e): a meet is a hit only
// when the caller's comparison with it holds, and otherwise revisits the key and overwrites it.
// Input: the key (a part, or a part and the Type beside it) and the stamp of its query. Output: the answer, or the input, that query
// recorded for the key, if any.
// Invariants: Begin gives every query a stamp no other query of the table has, so an entry of another query, pass or edit is stale and
// never read; a query begun during another (a nested entry call) only evicts the outer query's entries, which it then recomputes; the
// table drops its Types when a new pass begins a query and keeps its capacity, so a warm pass allocates nothing.
// Must not carry an answer past its query, or serve two walks with different inputs under one stamp.
public sealed partial class Binding
{
    // A query-stamped memo's first query number: 0 is no query, the number an absent row or entry carries.
    internal const uint FirstQuery = 1;

    private readonly TypeMemo<BoundType, BoundType?> substitutedParts = new(); // SubstituteType's images.

    private readonly TypeMemo<BoundType, BoundType?> closedParts = new(); // CloseNestedTypes' images.

    // The images of the leaf rewrites, WithoutRawOrigins, WithUnstatedOrigins, WithoutUnstated, SubstituteStoredOrigins, PublishedResult and
    // ResolveLocalResultOrigins (DECISIONS BU-58, BU-63; PLAN G492-a (ii)), RegionValuesAt, and LocalBorrowType's opening of a `let`
    // local, whose answer mints a part's regions the first time the query meets it (a function of the part within its query): none calls
    // a user of this table while it walks, so a query never begins inside another and evicts nothing live.
    private readonly TypeMemo<BoundType, BoundType?> rewrittenParts = new();

    // Viewed's images of a declared Type's parts as its function's body sees them (PLAN G935 c3-2), each keyed by its depth too: the depth
    // bound's cut makes an answer a function of the depth where a premise chain is deeper than StorageBound.
    private readonly TypeMemo<(BoundType Declared, int Depth), BoundType?> viewedParts = new();

    // The states in which a walk met a part, one bit per state (DECISIONS BU-63): the existence walks OperandContexts,
    // ContainsAssociatedProjection, DependentType, TypeAccessCovers, ProveTypeLengths, HasLocalRegion, HasRegionBounds and CarrierOf, and the walks
    // whose revisit only repeats what they add, CollectRequirementEdges, AddStoredPremises, ClosureType's Collect, AbstractParts (a leaf
    // recorded too), LocalBorrowType's MarkMerged (one query per opening, begun at its first part met again), and AccumulateRequirements
    // (a bit per polarity and StoragePosition) and ClosureArgumentVariance's Occurrences (a bit per polarity).
    private readonly TypeMemo<BoundType, byte> visitedParts = new();

    // The parts AddTypeClauseObligations met in one call (DECISIONS BU-19), a walk whose revisit only repeats what it adds. Its own
    // table: each clause's proof builds premise environments, whose walks are queries of visitedParts, and nothing the walk calls begins
    // a query of this one.
    private readonly TypeMemo<BoundType, byte> clauseParts = new();

    // The constructed parts OperandContexts found clear, whose query spans a capability request (R7-C6 a; Binding.ConstraintOperands'
    // ClearQuery): the one table whose query is not one walk's, its fixed inputs the declaration contexts' states during the request.
    private readonly TypeMemo<BoundType, byte> clearParts = new();

    // The query number after a query's: the one step every query-stamped memo advances by (TypeMemo.Begin, StructuralCompletion.Clear;
    // R7-N5 B6b, the Front's C6), the HIR publisher's publication number (FormReads, DECISIONS D-986) and the prerequisite walks' number
    // (PrerequisiteVisitRows, R7-N5 E). It skips 0, the number an absent row or entry carries, restarting at FirstQuery after
    // uint.MaxValue, so a fresh query never reads an absent row as its own. A number repeats only after 2^32 - 1 steps: TypeMemo empties
    // its table there, and a working-state family's rows of an earlier pass read absent by its pass stamp (no pass asks that many
    // queries, publications or walks).
    internal static uint NextQuery(uint query) => query == uint.MaxValue ? FirstQuery : query + 1;

    private sealed class TypeMemo<TKey, T>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, (uint Query, T Answer)> answers = new();
        private ulong pass;
        private uint query;

        // Begins a query of a Binding pass: its unique stamp. A new pass, or the number's wrap (NextQuery), empties the table.
        internal uint Begin(ulong pass)
        {
            this.query = pass == this.pass ? NextQuery(this.query) : FirstQuery;
            if (this.query == FirstQuery)
            {
                this.answers.Clear();
                this.pass = pass;
            }

            return this.query;
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
