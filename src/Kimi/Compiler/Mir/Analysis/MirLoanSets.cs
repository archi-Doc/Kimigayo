// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns interned immutable Loan sets: Add, Union (of two or of any number), Remove, Range, Of; the only Loan-set form in Mir (design A3 §6.1;
// DECISIONS R4-M53).
// Input: the elements, the Loan and marker ids A5 numbers. Output: each set as a node id, 0 the empty set; Add, Union and Remove (a \ b) of
// sets, the set of a Range of consecutive elements and the set of an ascending run (Of, interned in one pass, R4-M132) as node ids,
// Contains, Count and a set's elements in ascending order; Bytes, the element bytes of the nodes made (A3 §6.2's ν), and Scanned, the
// elements its operations read and wrote, which A5's work counts.
// Invariants: a node's elements are a strictly ascending run in one pooled array, and nodes are interned by their elements, so two sets are
// equal exactly when their ids are; a node never changes once made. Each operation but Of and the n-ary Union is memoized by its operands,
// so repeating it costs one lookup; Of's run is its own key, and the n-ary Union reads each distinct part once (V16, PLAN G342: holders
// that share one interned state are many parts of one set), each of its elements once, and sorts the marker words it sets, never the
// range between its least and greatest element (its caller memoizes it). Storage is pooled: Clear empties it per body view in proportion
// to the view's entries (MirTable, DECISIONS R4-M146) and keeps its capacity, so a warm run of a sized workload allocates nothing.
// Must not number or interpret elements, read MIR, or keep a set in another form.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

/// <summary>Interned immutable Loan sets (design A3 §6.1, DECISIONS R4-M53): a set is a node id, 0 the empty set, and equal sets have one
/// id, so a set is stored and compared as an int; <see cref="Add"/>, <see cref="Union(int, int)"/>, <see cref="Remove"/> and
/// <see cref="Range"/> are memoized.</summary>
internal sealed class MirLoanSets
{
    private readonly MirTable<int> nodes; // Each node by its elements: an interned run maps to its own id.
    private readonly MirTable<(int Set, int Element)> adds = new();
    private readonly MirTable<(int A, int B)> unions = new(); // Keyed with A < B.
    private readonly MirTable<(int Set, int Removed)> removes = new();
    private readonly MirTable<(int First, int Count)> ranges = new();
    private int[] elements = []; // The runs of the nodes, in node order.
    private int[] starts = [0, 0]; // Each node's first element, then the end of the last node's run and of a candidate's.
    private ulong[] marker = []; // The n-ary Union's bit per element, clear between its calls.
    private int[] touched = []; // The marker words an n-ary Union set, in the order it set them.
    private int[] unionBy = []; // By node: the n-ary Union pass that read it, so each distinct part is read once.
    private int unionsMade; // The n-ary Union passes made.
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
    /// smaller operand and the run it copies, and a Range's run.</summary>
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

    /// <summary>Gets the elements of a set in ascending order.</summary>
    /// <param name="set">The set.</param>
    /// <returns>Its elements.</returns>
    internal ReadOnlySpan<int> Elements(int set) => this.elements.AsSpan(this.starts[set], this.Count(set));

    /// <summary>Gets the number of elements of a set.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The count.</returns>
    internal int Count(int set) => this.starts[set + 1] - this.starts[set];

    /// <summary>Gets whether a set has an element.</summary>
    /// <param name="set">The set.</param>
    /// <param name="element">The element.</param>
    /// <returns>Whether the element is in the set.</returns>
    internal bool Contains(int set, int element) => this.Elements(set).BinarySearch(element) >= 0;

    /// <summary>Gets a set with an element added.</summary>
    /// <param name="set">The set.</param>
    /// <param name="element">The element.</param>
    /// <returns>The set and the element.</returns>
    internal int Add(int set, int element)
    {
        if (this.Contains(set, element))
        {
            return set;
        }

        if (!this.adds.TryGetValue((set, element), out var added))
        {
            added = this.Merge(set, new(in element));
            this.adds.Add((set, element), added);
        }

        return added;
    }

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
            union = this.Merge(a, this.Elements(b));
            this.unions.Add(key, union);
        }

        return union;
    }

    /// <summary>Gets the union of any number of sets in one pass (R4-M138): each distinct part is read once (a node stamp), each element set
    /// once in a reused bit marker, whose words set are listed as they are first set, then sorted and read back in ascending order, each
    /// cleared; one or no distinct set is returned as it is. Its work is the elements read, the words touched times their logarithm and the set made, never the range between
    /// the least element and the greatest.</summary>
    /// <param name="parts">The sets.</param>
    /// <returns>The elements of any.</returns>
    internal int Union(ReadOnlySpan<int> parts)
    {
        var (first, distinct, total, high) = (0, false, 0, -1);
        MirDataflow.Grow(ref this.unionBy, this.created);
        this.unionsMade++;
        foreach (var part in parts)
        {
            if (part != 0 && this.unionBy[part] != this.unionsMade)
            {
                this.unionBy[part] = this.unionsMade;
                distinct |= first != 0;
                first = first == 0 ? part : first;
                var run = this.Elements(part);
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
        this.unionsMade++;
        foreach (var part in parts)
        {
            if (this.unionBy[part] == this.unionsMade)
            {
                continue;
            }

            this.unionBy[part] = this.unionsMade;
            foreach (var element in this.Elements(part))
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
            for (var bits = this.marker[index]; bits != 0; bits &= bits - 1)
            {
                merged[length++] = (index << 6) | BitOperations.TrailingZeroCount(bits);
            }

            this.marker[index] = 0;
        }

        this.Scanned += total + (words * (BitOperations.Log2((uint)words) + 1)) + length;
        return this.Intern(length);
    }

    /// <summary>Gets a set without the elements of another, a \ b: the Loans a point ends, removed from each holder's set. It scans the
    /// smaller operand and searches the larger, and copies the set's elements only when it removes one.</summary>
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

        if (!this.removes.TryGetValue((set, removed), out var rest))
        {
            rest = this.Without(set, removed);
            this.removes.Add((set, removed), rest);
        }

        return rest;
    }

    /// <summary>Gets the set of a run of consecutive elements, from <paramref name="first"/> on: ids numbered so that the members of a set
    /// are adjacent, as the Loans a Move Path's destruction ends.</summary>
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
                run[i] = first + i;
            }

            range = this.Intern(count);
            this.ranges.Add((first, count), range);
        }

        return range;
    }

    /// <summary>Gets the set of a strictly ascending run, interned in one pass (its node, made once, is found by the run itself).</summary>
    /// <param name="run">The elements, strictly ascending.</param>
    /// <returns>The set.</returns>
    internal int Of(ReadOnlySpan<int> run)
    {
        if (run.IsEmpty)
        {
            return 0;
        }

        run.CopyTo(this.Candidate(run.Length));
        this.Scanned += run.Length;
        return this.Intern(run.Length);
    }

    // set \ removed: each element of the smaller operand searched in the larger.
    private int Without(int set, int removed)
    {
        var from = this.Elements(set);
        var without = this.Elements(removed);
        var run = this.Candidate(from.Length);
        var (length, copied) = (0, 0);
        this.Scanned += Math.Min(from.Length, without.Length);
        if (from.Length <= without.Length)
        {
            foreach (var element in from)
            {
                if (without.BinarySearch(element) < 0)
                {
                    run[length++] = element;
                }
            }

            return length == from.Length ? set : this.Intern(length);
        }

        foreach (var element in without)
        {
            if (from.BinarySearch(element) is var at && at >= 0)
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

    // Interns the union of a set and an ascending run.
    private int Merge(int set, ReadOnlySpan<int> other)
    {
        var from = this.Elements(set);
        var run = this.Candidate(from.Length + other.Length);
        this.Scanned += from.Length + other.Length;
        var (i, j, length) = (0, 0, 0);
        while (i < from.Length && j < other.Length)
        {
            var (x, y) = (from[i], other[j]);
            run[length++] = Math.Min(x, y);
            i += x <= y ? 1 : 0;
            j += y <= x ? 1 : 0;
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

    // Interns the candidate run of a length: the id of the node with its elements, or a new node.
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

    // Compares and hashes nodes by their elements, a candidate's included.
    private sealed class Runs(MirLoanSets sets) : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => sets.Elements(x).SequenceEqual(sets.Elements(y));

        public int GetHashCode(int node)
        {
            var hash = default(HashCode);
            hash.AddBytes(MemoryMarshal.AsBytes(sets.Elements(node)));
            return hash.ToHashCode();
        }
    }
}
