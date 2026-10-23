// G170 C1 prototype: request-local shapes (C1-design §1, rule 1). The shape of a term under bindings is the term with every
// subterm that is a Type replaced by its class (a bound variable, or a ground subterm whose Type is interned), every free variable
// replaced by a shape variable numbered by first occurrence (so renaming-equal terms are one shape), and every other subterm a
// shape node (head, kids). Shapes are hash-consed and append-only; a kid or a shape is encoded in one int: k >= 0 a class id,
// k < 0 the shape node -1 - k. A promise (Solver) is identified by (class, shape).
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed class ShapeTable
{
    private readonly EGraph g;
    private readonly List<Head> heads = new();
    private readonly List<int[]> kids = new();
    private readonly List<int> vars = new();
    private readonly Dictionary<Key, int> memo = new();

    public ShapeTable(EGraph g) => this.g = g;

    /// <summary>The shape of t under beta; varOf maps each shape variable to the row variable it stands for.</summary>
    public int Of(Term t, int?[] beta, List<int> varOf)
    {
        if (t.Head is VarHead v)
        {
            if (v.Index < beta.Length && beta[v.Index] is int bound)
            {
                return this.g.Find(bound);
            }

            var n = varOf.IndexOf(v.Index);
            if (n < 0)
            {
                n = varOf.Count;
                varOf.Add(v.Index);
            }

            return this.Node(new VarHead(n), []);
        }

        var ks = new int[t.Kids.Length];
        var ground = true;
        for (var i = 0; i < ks.Length; i++)
        {
            ks[i] = this.Of(t.Kids[i], beta, varOf);
            ground &= ks[i] >= 0;
        }

        if (ground && this.g.Lookup(t.Head, ks) is var c && c >= 0)
        {
            return c;
        }

        return this.Node(t.Head, ks);
    }

    /// <summary>A real node as a shape node over its kid classes.</summary>
    public int OfNode(int node)
        => this.Node(this.g.HeadOf(node), Enumerable.Range(0, this.g.KidCount(node)).Select(i => this.g.KidOf(node, i)).ToArray());

    public bool IsVar(int s) => s < 0 && this.heads[-1 - s] is VarHead;

    public Head HeadOf(int s) => this.heads[-1 - s];

    public int[] KidsOf(int s) => this.kids[-1 - s];

    /// <summary>The number of shape variables in a shape (0 for a class).</summary>
    public int VarCount(int s) => s < 0 ? this.vars[-1 - s] : 0;

    /// <summary>The class of a shape node that denotes an existing Type (all kids classes, the node interned), else -1.</summary>
    public int ClassOf(int s)
    {
        if (s >= 0)
        {
            return this.g.Find(s);
        }

        var ks = this.kids[-1 - s];
        return this.heads[-1 - s] is VarHead || ks.Any(k => k < 0) ? -1 : this.g.Lookup(this.heads[-1 - s], ks);
    }

    /// <summary>The pattern lifted through a constructor position (rule 8 down): head&lt;$n, …, kid at i, …&gt; with fresh shape
    /// variables, numbered after kid's, at every other position.</summary>
    public int Lift(Head head, int arity, int i, int kid)
    {
        var ks = new int[arity];
        var next = this.VarCount(kid);
        for (var j = 0; j < arity; j++)
        {
            ks[j] = j == i ? kid : this.Node(new VarHead(next++), []);
        }

        return this.Node(head, ks);
    }

    /// <summary>The shape with its variables renumbered by first occurrence (renaming-equal shapes are one).</summary>
    public int Canonical(int s) => this.Rename(s, new());

    /// <summary>The bare-variable shape `$0`: a promise of any shape, and the pattern every promise agrees with.</summary>
    public int Fresh() => this.Node(new VarHead(0), []);

    /// <summary>The kid at a constructor position (rule 8 down): a shape node's kid, renumbered (a class as itself); a class or a
    /// shape variable has no kid there, the bare-variable shape.</summary>
    public int Sub(int s, int i)
    {
        if (s >= 0 || this.heads[-1 - s] is VarHead)
        {
            return this.Fresh();
        }

        var k = this.kids[-1 - s][i];
        return k >= 0 ? this.g.Find(k) : this.Canonical(k);
    }

    /// <summary>The kid at a path of constructor positions.</summary>
    public int Sub(int s, int[] path)
    {
        foreach (var i in path)
        {
            s = this.Sub(s, i);
        }

        return s;
    }

    private int Rename(int s, List<int> map)
    {
        if (s >= 0)
        {
            return s;
        }

        var head = this.heads[-1 - s];
        if (head is VarHead v)
        {
            var n = map.IndexOf(v.Index);
            if (n < 0)
            {
                n = map.Count;
                map.Add(v.Index);
            }

            return this.Node(new VarHead(n), []);
        }

        var ks = this.kids[-1 - s];
        var rs = new int[ks.Length];
        for (var i = 0; i < ks.Length; i++)
        {
            rs[i] = this.Rename(ks[i], map);
        }

        return this.Node(head, rs);
    }

    private int Node(Head head, int[] ks)
    {
        var key = new Key(head, ks);
        if (!this.memo.TryGetValue(key, out var s))
        {
            s = this.heads.Count;
            this.heads.Add(head);
            this.kids.Add(ks);
            this.vars.Add(head is VarHead v ? v.Index + 1 : ks.Length == 0 ? 0 : ks.Max(k => k < 0 ? this.vars[-1 - k] : 0));
            this.memo.Add(key, s);
        }

        return -1 - s;
    }

    private readonly struct Key(Head head, int[] kids) : IEquatable<Key>
    {
        private readonly Head head = head;
        private readonly int[] kids = kids;

        public bool Equals(Key other) => this.head.Equals(other.head) && this.kids.AsSpan().SequenceEqual(other.kids);

        public override bool Equals(object? obj) => obj is Key other && this.Equals(other);

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
