// G170 C1 prototype: the request-local shape table (C1-design §1, rule 1). A shape is a term under bindings: a bound variable or an
// instantiated subterm whose Type is interned is its identity class; a free variable is a shape variable numbered by first occurrence
// (renaming-equal terms are one shape); any other subterm is a shape node over its kids' encodings. Kid encoding: k >= 0 is a class,
// k < 0 the shape -1 - k. Shapes are hash-consed; after classes merge, Canon re-interns a stored encoding's canonical form (its class
// kids by Find, a closed node that became interned by its class), memoized until the next merge: the one re-keying mechanism.
#pragma warning disable
#nullable enable

namespace G170C1;

internal sealed class ShapeTable
{
    private static readonly Head TupleHead = new SymHead(SymKind.Opaque, "β");
    private readonly List<Head> heads = new();
    private readonly List<int[]> kids = new();
    private readonly List<int> vars = new();
    private readonly List<int> varShapes = new();
    private readonly Dictionary<Key, int> memo = new();
    private readonly Dictionary<int, int> canon = new();

    public int Count => this.heads.Count;

    /// <summary>The shape of t under beta (variables beyond beta's length are free); varOf[n] is the row variable of shape variable n.</summary>
    public int Of(Term t, int?[] beta, List<int> varOf, EGraph g)
    {
        if (t.Head is VarHead v)
        {
            if (v.Index < beta.Length && beta[v.Index] is int bound)
            {
                return g.Find(bound);
            }

            var n = varOf.IndexOf(v.Index);
            if (n < 0)
            {
                n = varOf.Count;
                varOf.Add(v.Index);
            }

            return this.Var(n);
        }

        var ks = new int[t.Kids.Length];
        var closed = true;
        for (var i = 0; i < ks.Length; i++)
        {
            ks[i] = this.Of(t.Kids[i], beta, varOf, g);
            closed &= ks[i] >= 0;
        }

        if (closed && g.Lookup(t.Head, ks) is var c && c >= 0)
        {
            return c;
        }

        return this.Intern(t.Head, ks);
    }

    /// <summary>A real node as a shape node (its head over its kids' classes): what a promise agrees with when it meets the node.</summary>
    public int OfNode(int node, EGraph g)
    {
        var ks = new int[g.KidCount(node)];
        for (var i = 0; i < ks.Length; i++)
        {
            ks[i] = g.KidOf(node, i);
        }

        return this.Intern(g.HeadOf(node), ks);
    }

    /// <summary>A binding tuple as a shape (an unbound slot is the shape variable of its index): the key of an expansion.</summary>
    public int Tuple(int?[] beta, EGraph g)
    {
        var ks = new int[beta.Length];
        for (var i = 0; i < ks.Length; i++)
        {
            ks[i] = beta[i] is int b ? g.Find(b) : this.Var(i);
        }

        return this.Intern(TupleHead, ks);
    }

    /// <summary>The shape variable numbered n.</summary>
    public int Variable(int n) => this.Var(n);

    /// <summary>A shape node over an encoding (hash-consed).</summary>
    public int Node(Head head, int[] kids) => this.Intern(head, kids);

    public bool IsVar(int shape) => this.heads[-1 - shape] is VarHead;

    public int VarIndex(int shape) => ((VarHead)this.heads[-1 - shape]).Index;

    public Head HeadOf(int shape) => this.heads[-1 - shape];

    public ReadOnlySpan<int> KidsOf(int shape) => this.kids[-1 - shape];

    /// <summary>The number of shape variables under an encoding (0 for a class).</summary>
    public int VarCount(int enc) => enc >= 0 ? 0 : this.vars[-1 - enc];

    /// <summary>The canonical encoding after merges: class kids by Find, a closed node that is now interned by its class.</summary>
    public int Canon(int enc, EGraph g)
    {
        if (enc >= 0)
        {
            return g.Find(enc);
        }

        if (this.IsVar(enc))
        {
            return enc;
        }

        if (this.canon.TryGetValue(enc, out var known))
        {
            return known;
        }

        var old = this.kids[-1 - enc];
        var ks = new int[old.Length];
        var closed = true;
        for (var i = 0; i < ks.Length; i++)
        {
            ks[i] = this.Canon(old[i], g);
            closed &= ks[i] >= 0;
        }

        var head = this.heads[-1 - enc];
        var result = closed && !ReferenceEquals(head, TupleHead) && g.Lookup(head, ks) is var c && c >= 0 ? c : this.Intern(head, ks);
        this.canon[enc] = result;
        return result;
    }

    /// <summary>Forgets the canonical forms (after classes merged).</summary>
    public void Forget() => this.canon.Clear();

    private int Var(int n)
    {
        while (this.varShapes.Count <= n)
        {
            this.varShapes.Add(this.Intern(new VarHead(this.varShapes.Count), []));
        }

        return this.varShapes[n];
    }

    private int Intern(Head head, int[] ks)
    {
        var key = new Key(head, ks);
        if (this.memo.TryGetValue(key, out var id))
        {
            return -1 - id;
        }

        id = this.heads.Count;
        var n = head is VarHead v ? v.Index + 1 : 0;
        foreach (var k in ks)
        {
            n = Math.Max(n, this.VarCount(k));
        }

        this.heads.Add(head);
        this.kids.Add(ks);
        this.vars.Add(n);
        this.memo.Add(key, id);
        return -1 - id;
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
