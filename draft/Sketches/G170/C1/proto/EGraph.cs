// G170 C1 prototype: the request-local identity structure (SPEC 8.2.3 as revised by the 2026-10-22 proposal "Type Identity by
// Structural Decomposition B", §3.3): classes of Types closed under replacing a Type by an identical one inside any Type (upward
// congruence) and under decomposition of constructed Types (Option<U> = Option<i32> yields U = i32): one constructor (Prim or Con
// node) per class; a class holding constructors of different heads is conflicting (B 3.10, a premise error) and a class that is its
// own proper part through constructor kids is cyclic (B 3.6). Projections and Contract arguments are never decomposed (B 3.5).
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed class EGraph
{
    private readonly List<int> parent = new();
    private readonly List<List<int>> nodesOf = new();
    private readonly List<List<int>> usesOf = new();
    private readonly List<Head> heads = new();
    private readonly List<int[]> kids = new();
    private readonly List<int> classOf = new();
    private readonly List<NodeKey> keyOf = new();
    private readonly Dictionary<NodeKey, int> memo = new();
    private readonly List<(int A, int B)> pending = new();
    private readonly List<int> constructorOf = new();
    private readonly List<bool> conflicting = new();

    public int Nodes => this.memo.Count;

    /// <summary>Gets the number of nodes ever created (node ids are dense and only grow).</summary>
    public int NodeTotal => this.heads.Count;

    public int Classes { get; private set; }

    /// <summary>Gets the congruence work: per rebuild, the classes merged plus the distinct nodes over a merged class (a function of
    /// the partition, so independent of union order).</summary>
    public long Work { get; private set; }

    public int Find(int c)
    {
        while (this.parent[c] != c)
        {
            this.parent[c] = this.parent[this.parent[c]];
            c = this.parent[c];
        }

        return c;
    }

    public IReadOnlyList<int> NodesOf(int c) => this.nodesOf[this.Find(c)];

    public Head HeadOf(int node) => this.heads[node];

    public int KidOf(int node, int i) => this.Find(this.kids[node][i]);

    public int KidCount(int node) => this.kids[node].Length;

    public int ClassOfNode(int node) => this.Find(this.classOf[node]);

    public IEnumerable<int> UsesOf(int c) => this.usesOf[this.Find(c)];

    /// <summary>The class's constructor (its Prim or Con node; -1 when abstract). A conflicting class answers one of its constructors.</summary>
    public int Constructor(int c) => this.constructorOf[this.Find(c)];

    /// <summary>Whether the class holds constructors of different heads (B 3.10: a structural conflict).</summary>
    public bool Conflict(int c) => this.conflicting[this.Find(c)];

    /// <summary>Whether the class is its own proper part through constructor kids (B 3.6).</summary>
    public bool Cyclic(int c) => this.ProperPart(c, c, new());

    /// <summary>Whether b is a proper part of a: reachable from a's constructor's kids through constructor kids only (B 3.6;
    /// projections and Contract arguments are not followed).</summary>
    public bool ProperPart(int a, int b, HashSet<int> visited)
    {
        var n = this.constructorOf[this.Find(a)];
        if (n < 0)
        {
            return false;
        }

        b = this.Find(b);
        foreach (var k in this.kids[n])
        {
            var kc = this.Find(k);
            if (kc == b || (visited.Add(kc) && this.ProperPart(kc, b, visited)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The top-level head test (rule 7's prune): either class is conflicting, or both hold a constructor and the heads
    /// differ, so they can never be one class (B 3.3). Reads materialized constructors only; the full relation (Solver.Refuted) adds
    /// the negated facts, the kid pairs and the proper parts.</summary>
    public bool Clash(int a, int b)
    {
        a = this.Find(a);
        b = this.Find(b);
        var (n, o) = (this.constructorOf[a], this.constructorOf[b]);
        return this.conflicting[a] || this.conflicting[b] || (n >= 0 && o >= 0 && (!this.heads[n].Equals(this.heads[o]) || this.kids[n].Length != this.kids[o].Length));
    }

    public int Add(Head head, int[] kidClasses)
    {
        var canonical = new int[kidClasses.Length];
        for (var i = 0; i < canonical.Length; i++)
        {
            canonical[i] = this.Find(kidClasses[i]);
        }

        var key = new NodeKey(head, canonical);
        if (this.memo.TryGetValue(key, out var node))
        {
            return this.Find(this.classOf[node]);
        }

        node = this.heads.Count;
        var c = this.parent.Count;
        this.parent.Add(c);
        this.nodesOf.Add([node]);
        this.usesOf.Add(new());
        this.heads.Add(head);
        this.kids.Add(canonical);
        this.classOf.Add(c);
        this.keyOf.Add(key);
        this.memo.Add(key, node);
        this.constructorOf.Add(head is SymHead { Kind: SymKind.Prim or SymKind.Con } ? node : -1);
        this.conflicting.Add(false);
        this.Classes++;
        foreach (var k in canonical)
        {
            this.usesOf[k].Add(node);
        }

        return c;
    }

    /// <summary>Looks a node up without adding it (-1 when absent).</summary>
    public int Lookup(Head head, int[] kidClasses)
    {
        var canonical = new int[kidClasses.Length];
        for (var i = 0; i < canonical.Length; i++)
        {
            canonical[i] = this.Find(kidClasses[i]);
        }

        return this.memo.TryGetValue(new NodeKey(head, canonical), out var node) ? this.Find(this.classOf[node]) : -1;
    }

    public int AddTerm(Term t)
    {
        var k = new int[t.Kids.Length];
        for (var i = 0; i < k.Length; i++)
        {
            k[i] = this.AddTerm(t.Kids[i]);
        }

        return this.Add(t.Head, k);
    }

    public int LookupTerm(Term t)
    {
        var k = new int[t.Kids.Length];
        for (var i = 0; i < k.Length; i++)
        {
            k[i] = this.LookupTerm(t.Kids[i]);
            if (k[i] < 0)
            {
                return -1;
            }
        }

        return this.Lookup(t.Head, k);
    }

    public void Union(int a, int b) => this.pending.Add((a, b));

    public bool HasPending => this.pending.Count != 0;

    /// <summary>Applies the queued unions and restores congruence; returns every class id that took part in a merge.</summary>
    public HashSet<int> Rebuild()
    {
        var merged = new HashSet<int>();
        while (this.pending.Count != 0)
        {
            var (a, b) = this.pending[^1];
            this.pending.RemoveAt(this.pending.Count - 1);
            a = this.Find(a);
            b = this.Find(b);
            if (a == b)
            {
                continue;
            }

            if (this.nodesOf[a].Count + this.usesOf[a].Count < this.nodesOf[b].Count + this.usesOf[b].Count)
            {
                (a, b) = (b, a);
            }

            this.Work++;
            merged.Add(a);
            merged.Add(b);
            this.parent[b] = a;
            this.Classes--;
            this.nodesOf[a].AddRange(this.nodesOf[b]);
            this.nodesOf[b].Clear();

            // Decomposition (B 3.3): two constructors of one head union their kids pairwise (queued like congruence; the nodes then
            // become one by congruence); of different heads, the class is conflicting (both nodes stay).
            this.conflicting[a] |= this.conflicting[b];
            var (n, o) = (this.constructorOf[a], this.constructorOf[b]);
            if (n < 0)
            {
                this.constructorOf[a] = o;
            }
            else if (o >= 0)
            {
                if (this.heads[n].Equals(this.heads[o]) && this.kids[n].Length == this.kids[o].Length)
                {
                    for (var i = 0; i < this.kids[n].Length; i++)
                    {
                        this.pending.Add((this.kids[n][i], this.kids[o][i]));
                    }
                }
                else
                {
                    this.conflicting[a] = true;
                }
            }

            var uses = this.usesOf[b];
            this.usesOf[b] = new();
            this.usesOf[a].AddRange(uses);
            foreach (var node in uses)
            {
                var old = this.keyOf[node];
                if (this.memo.TryGetValue(old, out var owner) && owner == node)
                {
                    this.memo.Remove(old);
                }

                var canonical = new int[this.kids[node].Length];
                for (var i = 0; i < canonical.Length; i++)
                {
                    canonical[i] = this.Find(this.kids[node][i]);
                }

                this.kids[node] = canonical;
                var key = new NodeKey(this.heads[node], canonical);
                this.keyOf[node] = key;
                if (this.memo.TryGetValue(key, out var other) && other != node)
                {
                    // Congruence: same head over identical kids. Its explanation is that of the kids.
                    var pc = this.Find(this.classOf[node]);
                    var oc = this.Find(this.classOf[other]);
                    if (pc != oc)
                    {
                        this.pending.Add((pc, oc));
                    }
                }
                else
                {
                    this.memo[key] = node;
                }
            }
        }

        var repaired = new HashSet<int>();
        foreach (var root in merged.Select(this.Find).Distinct())
        {
            repaired.UnionWith(this.usesOf[root]);
        }

        this.Work += repaired.Count;
        return merged;
    }

    /// <summary>Classes whose content can change when one of the given classes changes: the classes themselves and every class
    /// using them as a kid, transitively.</summary>
    public HashSet<int> Upward(IEnumerable<int> classes)
    {
        var result = new HashSet<int>();
        var stack = new Stack<int>(classes.Select(this.Find));
        while (stack.Count != 0)
        {
            var c = this.Find(stack.Pop());
            if (!result.Add(c))
            {
                continue;
            }

            foreach (var node in this.usesOf[c])
            {
                stack.Push(this.Find(this.classOf[node]));
            }
        }

        return result;
    }

    /// <summary>A smallest member of a class, for display.</summary>
    public string Show(int c, int depth = 6)
    {
        c = this.Find(c);
        if (depth == 0)
        {
            return "…";
        }

        string? best = null;
        foreach (var node in this.nodesOf[c])
        {
            var text = this.heads[node].Show(this.kids[node].Select(k => this.Show(k, depth - 1)).ToArray());
            if (best is null || text.Length < best.Length)
            {
                best = text;
            }
        }

        return best ?? "?";
    }

    private readonly struct NodeKey(Head head, int[] kids) : IEquatable<NodeKey>
    {
        private readonly Head head = head;
        private readonly int[] kids = kids;

        public bool Equals(NodeKey other) => this.head.Equals(other.head) && this.kids.AsSpan().SequenceEqual(other.kids);

        public override bool Equals(object? obj) => obj is NodeKey other && this.Equals(other);

        public override int GetHashCode()
        {
            var hash = this.head.GetHashCode();
            foreach (var k in this.kids)
            {
                hash = HashCode.Combine(hash, k);
            }

            return hash;
        }
    }
}
