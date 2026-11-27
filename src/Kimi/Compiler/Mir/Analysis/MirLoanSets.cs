// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns interned immutable Loan sets: Add (an element or an ascending run), Union (of two or of any number), Remove, Range; the only Loan-set
// form in Mir (design A3 §6.1; DECISIONS R4-M53), and each element's tier, decided or rested (DECISIONS R4-M2xx, PLAN G599 (b)).
// Input: the elements, the Loan and marker ids A5 numbers, each below 2^30, and in a second namespace the storing rule's must-set elements (Own,
// Past; MirBorrows.Storing), whose sets are never joined with the first's. Output: each set as a node id, 0 the empty set; Add, Union and Remove
// (a \ b) of sets, a set with an ascending run added (interned in one pass, R4-M132, G263) and the set of a Range of consecutive elements as
// node ids, Contains, Count and a set's tiered elements in ascending order; Bytes, the element bytes of the nodes made (A3 §6.2's ν), and
// Scanned, the elements its operations read and wrote, which A5's work counts.
// Invariants: a node's elements are a strictly ascending run of words, 2·element + rested, in one pooled array, so each element is in a set at
// most once, at one tier: where both tiers of an element meet (Add, Union), the decided one wins, and every other operation matches by element,
// so a removed element removes either tier (Contains, Remove, Range). Nodes are interned by their words, so two sets are equal exactly when
// their ids are; a node never changes once made. Each operation but a run's Add, the n-ary Union and a Remove of two sets that lie apart is
// memoized by its operands, so repeating it costs one lookup; a Remove of sets that lie apart (one's greatest element below the other's least)
// is the set, read from the two runs' ends with no memo entry (PLAN G768); a run's Add makes no node of the run itself, and the n-ary Union
// reads each element once and sorts the marker words it sets, never the range between its least and greatest element (their callers memoize
// them). Storage is pooled: Clear empties it per body view in proportion to the view's entries (MirTable, DECISIONS R4-M146) and keeps its
// capacity, so a warm run of a sized workload allocates nothing.
// Must not number or interpret elements, decide a tier, read MIR, or keep a set in another form.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

/// <summary>A Loan-set element with its tier (DECISIONS R4-M2xx): decided, or rested by the owner of an undecided fact; one word,
/// 2·element + rested, so a set's words ascend by element and an element's decided word precedes its rested one.</summary>
/// <param name="element">The element, below 2^30.</param>
/// <param name="rested">Whether it rests.</param>
internal readonly struct TieredElement(int element, bool rested)
{
    private readonly int word = (uint)element < 1U << 30 ? (element << 1) | (rested ? 1 : 0)
        : throw new InvalidOperationException($"A Loan-set element {element} is not below 2^30 (InternalInvariant).");

    /// <summary>Gets the element.</summary>
    internal int Element => this.word >> 1;

    /// <summary>Gets a value indicating whether the element rests.</summary>
    internal bool Rested => (this.word & 1) != 0;

    /// <summary>Gets the stored word, 2·element + rested, which only <see cref="MirLoanSets"/> reads.</summary>
    internal int Word => this.word;

    /// <summary>Reads the element and its tier.</summary>
    /// <param name="element">The element.</param>
    /// <param name="rested">Whether it rests.</param>
    internal void Deconstruct(out int element, out bool rested) => (element, rested) = (this.Element, this.Rested);
}

/// <summary>Interned immutable Loan sets (design A3 §6.1, DECISIONS R4-M53): a set is a node id, 0 the empty set, and equal sets have one
/// id, so a set is stored and compared as an int; <see cref="Add(int, TieredElement)"/>, <see cref="Union(int, int)"/>, <see cref="Remove"/>
/// and <see cref="Range"/> are memoized.</summary>
internal sealed class MirLoanSets
{
    private const ulong Decided = 0x5555_5555_5555_5555UL; // The decided words' bits of a marker word.
    private readonly MirTable<int> nodes; // Each node by its words: an interned run maps to its own id.
    private readonly MirTable<(int Set, int Word)> adds = new();
    private readonly MirTable<(int A, int B)> unions = new(); // Keyed with A < B.
    private readonly MirTable<(int Set, int Removed)> removes = new();
    private readonly MirTable<(int First, int Count)> ranges = new();
    private int[] elements = []; // The runs of the nodes' words, in node order.
    private int[] starts = [0, 0]; // Each node's first word, then the end of the last node's run and of a candidate's.
    private ulong[] marker = []; // The n-ary Union's bit per word, clear between its calls.
    private int[] touched = []; // The marker words an n-ary Union set, in the order it set them.
    private int created = 1; // The nodes made; node 0 is the empty set.

    /// <summary>Initializes a new instance of the <see cref="MirLoanSets"/> class, holding the empty set.</summary>
    internal MirLoanSets()
    {
        this.nodes = new(new Runs(this));
        this.nodes.Add(0, 0);
    }

    /// <summary>Gets the element bytes of the nodes made since the last <see cref="Clear"/>, A3 §6.2's ν.</summary>
    internal long Bytes { get; private set; }

    /// <summary>Gets the elements the operations since the last <see cref="Clear"/> scanned: both runs of a Union or an Add, a Remove's
    /// smaller operand and the run it copies (none for two sets that lie apart), and a Range's run.</summary>
    internal long Scanned { get; private set; }

    /// <summary>Removes every set but the empty one, keeping the storage, in proportion to the view's entries (MirTable).</summary>
    internal void Clear()
    {
        this.nodes.Clear(); // Before the runs are dropped: a node is found by its run.
        this.adds.Clear();
        this.unions.Clear();
        this.removes.Clear();
        this.ranges.Clear();
        this.nodes.Add(0, 0);
        (this.created, this.Bytes, this.Scanned) = (1, 0, 0);
    }

    /// <summary>Gets the elements of a set in ascending order, each at its tier.</summary>
    /// <param name="set">The set.</param>
    /// <returns>Its elements.</returns>
    internal ReadOnlySpan<TieredElement> Elements(int set) => MemoryMarshal.Cast<int, TieredElement>(this.Words(set));

    /// <summary>Gets the number of elements of a set.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The count.</returns>
    internal int Count(int set) => this.starts[set + 1] - this.starts[set];

    /// <summary>Gets whether a set has an element, at either tier.</summary>
    /// <param name="set">The set.</param>
    /// <param name="element">The element.</param>
    /// <returns>Whether the element is in the set.</returns>
    internal bool Contains(int set, int element) => Find(this.Words(set), element) >= 0;

    /// <summary>Gets a set with a decided element added.</summary>
    /// <param name="set">The set.</param>
    /// <param name="element">The element.</param>
    /// <returns>The set and the element.</returns>
    internal int Add(int set, int element) => this.Add(set, new TieredElement(element, false));

    /// <summary>Gets a set with an element added at its tier; where the set has the element, the decided tier wins.</summary>
    /// <param name="set">The set.</param>
    /// <param name="element">The element.</param>
    /// <returns>The set and the element.</returns>
    internal int Add(int set, TieredElement element)
    {
        var words = this.Words(set);
        var word = element.Word;
        if (Find(words, element.Element) is var at && at >= 0 && words[at] <= word)
        {
            return set;
        }

        if (!this.adds.TryGetValue((set, word), out var added))
        {
            added = this.Merge(set, new(in word));
            this.adds.Add((set, word), added);
        }

        return added;
    }

    /// <summary>Gets a set with the elements of a strictly ascending run added, interned in one pass: one merge and one node, none of the
    /// run itself (G263); unmemoized, its caller memoizes it.</summary>
    /// <param name="set">The set.</param>
    /// <param name="run">The elements, strictly ascending.</param>
    /// <returns>The set and the run's elements.</returns>
    internal int Add(int set, ReadOnlySpan<TieredElement> run) => run.IsEmpty ? set : this.Merge(set, MemoryMarshal.Cast<TieredElement, int>(run));

    /// <summary>Gets the union of two sets.</summary>
    /// <param name="a">A set.</param>
    /// <param name="b">Another set.</param>
    /// <returns>The elements of either.</returns>
    internal int Union(int a, int b)
    {
        if (a == b || b == 0)
        {
            return a;
        }

        if (a == 0)
        {
            return b;
        }

        var key = (Math.Min(a, b), Math.Max(a, b));
        if (!this.unions.TryGetValue(key, out var union))
        {
            union = this.Merge(a, this.Words(b));
            this.unions.Add(key, union);
        }

        return union;
    }

    /// <summary>Gets the union of any number of sets in one pass (R4-M138): each word is set once in a reused bit marker, whose words
    /// set are listed as they are first set, then sorted and read back in ascending order, each cleared, an element's rested bit dropped
    /// beside its decided one; one or no distinct set is returned as it is. Its work is the elements read, the words touched times their
    /// logarithm and the set made, never the range between the least element and the greatest.</summary>
    /// <param name="parts">The sets.</param>
    /// <returns>The elements of any.</returns>
    internal int Union(ReadOnlySpan<int> parts)
    {
        var (first, distinct, total, high) = (0, false, 0, -1);
        foreach (var part in parts)
        {
            if (part != 0)
            {
                distinct |= first != 0 && part != first;
                first = first == 0 ? part : first;
                var run = this.Words(part);
                (total, high) = (total + run.Length, Math.Max(high, run[^1]));
            }
        }

        if (!distinct)
        {
            return first;
        }

        MirDataflow.Grow(ref this.marker, (high >> 6) + 1);
        MirDataflow.Grow(ref this.touched, total);
        var words = 0;
        foreach (var part in parts)
        {
            foreach (var element in this.Words(part))
            {
                ref var word = ref this.marker[element >> 6];
                if (word == 0)
                {
                    this.touched[words++] = element >> 6;
                }

                word |= 1UL << element;
            }
        }

        var listed = this.touched.AsSpan(0, words);
        listed.Sort();
        var merged = this.Candidate(total);
        var length = 0;
        foreach (var index in listed)
        {
            for (var bits = this.marker[index] & ~((this.marker[index] & Decided) << 1); bits != 0; bits &= bits - 1)
            {
                merged[length++] = (index << 6) | BitOperations.TrailingZeroCount(bits);
            }

            this.marker[index] = 0;
        }

        this.Scanned += total + (words * (BitOperations.Log2((uint)words) + 1)) + length;
        return this.Intern(length);
    }

    /// <summary>Gets a set without the elements of another, a \ b, by element at either tier: the Loans a point ends, removed from each
    /// holder's set. Two sets that lie apart (one's greatest element below the other's least) give the set from their runs' ends, with no
    /// scan or memo entry; otherwise it scans the smaller operand and searches the larger, and copies the set's elements only when it removes
    /// one.</summary>
    /// <param name="set">The set.</param>
    /// <param name="removed">The elements to remove.</param>
    /// <returns>The elements of the set not in the removed set.</returns>
    internal int Remove(int set, int removed)
    {
        if (set == 0 || removed == 0)
        {
            return set;
        }

        if (set == removed)
        {
            return 0;
        }

        if (this.Apart(set, removed))
        {
            return set;
        }

        if (!this.removes.TryGetValue((set, removed), out var rest))
        {
            rest = this.Without(set, removed);
            this.removes.Add((set, removed), rest);
        }

        return rest;
    }

    /// <summary>Gets the set of a run of consecutive decided elements, from <paramref name="first"/> on: ids numbered so that the members
    /// of a set are adjacent, as the Loans a Move Path's destruction ends.</summary>
    /// <param name="first">The first element.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>The set of the run.</returns>
    internal int Range(int first, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (!this.ranges.TryGetValue((first, count), out var range))
        {
            var run = this.Candidate(count);
            this.Scanned += count;
            for (var i = 0; i < count; i++)
            {
                run[i] = new TieredElement(first + i, false).Word;
            }

            range = this.Intern(count);
            this.ranges.Add((first, count), range);
        }

        return range;
    }

    // The index of an element's word in a run of words, at either tier, or a negative number where the run lacks the element.
    private static int Find(ReadOnlySpan<int> words, int element)
    {
        var at = words.BinarySearch(element << 1);
        return at >= 0 || ~at == words.Length || words[~at] != ((element << 1) | 1) ? at : ~at;
    }

    // A set's words in ascending order.
    private ReadOnlySpan<int> Words(int set) => this.elements.AsSpan(this.starts[set], this.Count(set));

    // Whether two nonempty sets lie apart: the greatest element of one is below the least of the other, so they share none.
    private bool Apart(int a, int b)
        => (this.elements[this.starts[a + 1] - 1] >> 1) < (this.elements[this.starts[b]] >> 1)
            || (this.elements[this.starts[b + 1] - 1] >> 1) < (this.elements[this.starts[a]] >> 1);

    // set \ removed by element: each element of the smaller operand searched in the larger.
    private int Without(int set, int removed)
    {
        var from = this.Words(set);
        var without = this.Words(removed);
        var run = this.Candidate(from.Length);
        var (length, copied) = (0, 0);
        this.Scanned += Math.Min(from.Length, without.Length);
        if (from.Length <= without.Length)
        {
            foreach (var word in from)
            {
                if (Find(without, word >> 1) < 0)
                {
                    run[length++] = word;
                }
            }

            return length == from.Length ? set : this.Intern(length);
        }

        foreach (var word in without)
        {
            if (Find(from, word >> 1) is var at && at >= 0)
            {
                from[copied..at].CopyTo(run[length..]);
                (length, copied) = (length + at - copied, at + 1);
            }
        }

        if (copied == 0)
        {
            return set;
        }

        from[copied..].CopyTo(run[length..]);
        this.Scanned += from.Length;
        return this.Intern(length + from.Length - copied);
    }

    // Interns the union of a set and an ascending run of words, matched by element: where both tiers of an element meet, the decided word.
    private int Merge(int set, ReadOnlySpan<int> other)
    {
        var from = this.Words(set);
        var run = this.Candidate(from.Length + other.Length);
        this.Scanned += from.Length + other.Length;
        var (i, j, length) = (0, 0, 0);
        while (i < from.Length && j < other.Length)
        {
            var (x, y) = (from[i], other[j]);
            run[length++] = Math.Min(x, y);
            i += (x >> 1) <= (y >> 1) ? 1 : 0;
            j += (y >> 1) <= (x >> 1) ? 1 : 0;
        }

        from[i..].CopyTo(run[length..]);
        length += from.Length - i;
        other[j..].CopyTo(run[length..]);
        return this.Intern(length + other.Length - j);
    }

    // The storage of a candidate run after the last node's, which no node shares; growing the pool leaves the runs read before intact.
    private Span<int> Candidate(int capacity)
    {
        var start = this.starts[this.created];
        MirDataflow.Grow(ref this.elements, start + capacity);
        return this.elements.AsSpan(start, capacity);
    }

    // Interns the candidate run of a length: the id of the node with its words, or a new node.
    private int Intern(int length)
    {
        MirDataflow.Grow(ref this.starts, this.created + 2);
        this.starts[this.created + 1] = this.starts[this.created] + length;
        ref var node = ref this.nodes.Entry(this.created, out var exists);
        if (!exists)
        {
            node = this.created++;
            this.Bytes += length * sizeof(int);
        }

        return node;
    }

    // Compares and hashes nodes by their words, a candidate's included.
    private sealed class Runs(MirLoanSets sets) : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => sets.Words(x).SequenceEqual(sets.Words(y));

        public int GetHashCode(int node)
        {
            var hash = default(HashCode);
            hash.AddBytes(MemoryMarshal.AsBytes(sets.Words(node)));
            return hash.ToHashCode();
        }
    }
}
