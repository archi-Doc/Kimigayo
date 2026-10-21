// G170 C1 prototype: the request-local identity structure (SPEC 8.2.3): classes of Types closed under replacing a Type by an
// identical one inside any Type (upward congruence only: Option<U> = Option<i32> never yields U = i32).
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed class EGraph
{
    private readonly List<int> parent = new();
    private readonly List<List<int>> nodesOf = new();
    private readonly List<List<int>> usesOf = new();
    private readonly List<HashSet<int>> explain = new();
    private readonly List<Head> heads = new();
    private readonly List<int[]> kids = new();
    private readonly List<int> classOf = new();
    private readonly List<NodeKey> keyOf = new();
    private readonly Dictionary<NodeKey, int> memo = new();
    private readonly List<(int A, int B, int Reason)> pending = new();

    public int Nodes => this.memo.Count;

    /// <summary>Gets the number of nodes ever created (node ids are dense and only grow).</summary>
    public int NodeTotal => this.heads.Count;

    public int Classes { get; private set; }

    public long Work { get; set; }

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

    public IReadOnlyCollection<int> Explain(int c) => this.explain[this.Find(c)];

    public IEnumerable<int> UsesOf(int c) => this.usesOf[this.Find(c)];

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
        this.explain.Add(new());
        this.heads.Add(head);
        this.kids.Add(canonical);
        this.classOf.Add(c);
        this.keyOf.Add(key);
        this.memo.Add(key, node);
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

    public void Union(int a, int b, int reason) => this.pending.Add((a, b, reason));

    public bool HasPending => this.pending.Count != 0;

    /// <summary>Applies the queued unions and restores congruence; returns every class id that took part in a merge.</summary>
    public HashSet<int> Rebuild()
    {
        var merged = new HashSet<int>();
        while (this.pending.Count != 0)
        {
            var (a, b, reason) = this.pending[^1];
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
            this.explain[a].UnionWith(this.explain[b]);
            if (reason >= 0)
            {
                this.explain[a].Add(reason);
            }

            var uses = this.usesOf[b];
            this.usesOf[b] = new();
            this.usesOf[a].AddRange(uses);
            foreach (var node in uses)
            {
                this.Work++;
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
                        foreach (var k in canonical)
                        {
                            this.explain[pc].UnionWith(this.explain[k]);
                        }

                        this.pending.Add((pc, oc, -1));
                    }
                }
                else
                {
                    this.memo[key] = node;
                }
            }
        }

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
