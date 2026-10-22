// G170 C1 Solver Matching concern: request-local matching over the previous round's classes and rows.
// Inputs: immutable declaration indexes and the current round snapshot. Outputs: candidate matches and staged needs/effects.
// Invariants: no row fires here; EGraph identity is never decomposed; semantic changes are published by the round owner.
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed partial class Solver
{
    // ---- Matching ----
    private void Match(Term p, int c, int?[] beta, List<int?[]> results, MatchCtx ctx)
    {
        this.work++;
        c = this.g.Find(c);
        ctx.Visit(c);
        if (p.Head is VarHead v)
        {
            if (beta[v.Index] is int bound)
            {
                if (this.g.Find(bound) == c)
                {
                    results.Add(beta);
                }
                else if (ctx.Mode == MatchMode.Expand)
                {
                    // A bound variable needs its class's members in c: the two classes meet, as an identity goal's do.
                    this.Meet(c, this.g.Find(bound), ctx);
                }
            }
            else
            {
                var nb = (int?[])beta.Clone();
                nb[v.Index] = c;
                results.Add(nb);
            }

            return;
        }

        if (!p.HasVar)
        {
            var gc = this.g.LookupTerm(p);
            if (gc >= 0 && this.g.Find(gc) == c)
            {
                results.Add(beta);
            }
            else
            {
                // A ground Type joins c if a virtual node of c has its shape (the expansion is fully bound by the ground Type), or
                // by upward congruence if a node of c with its head has kids that each join its kids: their needs go down.
                this.ThroughVirtuals(p, c, beta, ctx);
                if (ctx.Mode != MatchMode.None)
                {
                    foreach (var node in this.g.NodesOf(c).ToArray())
                    {
                        if (this.g.KidCount(node) == p.Kids.Length && p.Kids.Length != 0 && this.g.HeadOf(node).Equals(p.Head))
                        {
                            this.work++;
                            for (var i = 0; i < p.Kids.Length; i++)
                            {
                                this.Match(p.Kids[i], this.g.KidOf(node, i), beta, new List<int?[]>(), ctx);
                            }
                        }
                    }
                }
            }

            return;
        }

        foreach (var node in this.g.NodesOf(c).ToArray())
        {
            this.work++;
            if (this.g.KidCount(node) != p.Kids.Length || !this.g.HeadOf(node).Equals(p.Head))
            {
                continue;
            }

            var partial = new List<int?[]> { beta };
            for (var i = 0; i < p.Kids.Length && partial.Count != 0; i++)
            {
                var next = new List<int?[]>();
                foreach (var b in partial)
                {
                    this.Match(p.Kids[i], this.g.KidOf(node, i), b, next, ctx);
                }

                partial = next;
            }

            results.AddRange(partial);
        }

        this.ThroughVirtuals(p, c, beta, ctx);
    }

    // The pattern needs nodes of its head in c that only a virtual node of c can provide: a requester expands them; a chain
    // probe records them.
    private void ThroughVirtuals(Term p, int c, int?[] beta, MatchCtx ctx)
    {
        if (ctx.Mode == MatchMode.None || this.Shapes(c) is var shapes && !shapes.Contains(HeadKey.Any) && !shapes.Contains(HeadKey.Of(p)))
        {
            return; // no row can give c a virtual node of p's head (a static index fact)
        }

        // A chain probe asks the same (class, head, kid filters) for many candidates of one lookup: answered once per lookup.
        var kid0 = p.Kids.Length > 0 ? this.KidHeads(p.Kids[0], beta) : null;
        var kid1 = p.Kids.Length > 1 ? this.KidHeads(p.Kids[1], beta) : null;
        var key = (this.g.Find(c), HeadKey.Of(p), this.SetKey(kid0) + "|" + this.SetKey(kid1));
        var virtuals = ctx.Memo is { } memo ? (memo.TryGetValue(key, out var known) ? known : memo[key] = this.VirtualsAt(c, key.Item2, ctx, kid0, kid1)) : this.Virtuals(c, key.Item2, ctx, kid0, kid1);
        foreach (var vs in virtuals)
        {
            if (ctx.Mode == MatchMode.Chain)
            {
                // An alias (a variable other side its side binds) is an existing class, not a node to meet: expanding it where a
                // need meets it merges the classes, after which the side matches directly.
                if (this.MayUnify(vs.Other, p) && !(vs.Via is null && vs.Other.Head is VarHead a && vs.Beta[a.Index] is not null))
                {
                    ctx.Chain!.Add((vs, p, beta));
                }
            }
            else
            {
                this.Expand(vs, p, beta, ctx);
            }
        }
    }

    // The heads a pattern's first kid can meet: those of its class and that class's virtual nodes when it resolves to a class;
    // its own head when it names an absent Type; anything when it is an unbound variable (null).
    private HashSet<HeadKey>? KidHeads(Term kid, int?[] beta)
    {
        var c = kid.Head is VarHead v ? beta[v.Index] ?? -1 : this.ClassOf(kid, beta);
        if (c >= 0)
        {
            var heads = this.HeadsOf(c);
            heads.UnionWith(this.VirtualHeads(heads));
            return heads;
        }

        return kid.Head is VarHead ? null : new() { HeadKey.Of(kid) };
    }

    // Whether two sides can denote one Type: variables match anything; ground Types compare by class when both are present.
    private bool MayUnify(Term a, Term b)
    {
        if (a.Head is VarHead || b.Head is VarHead || ReferenceEquals(a, b))
        {
            return true;
        }

        if (!a.HasVar && !b.HasVar && this.g.LookupTerm(a) is var x and >= 0 && this.g.LookupTerm(b) is var y and >= 0)
        {
            return this.g.Find(x) == this.g.Find(y);
        }

        return a.Head.Equals(b.Head) && a.Kids.Length == b.Kids.Length && a.Kids.Zip(b.Kids).All(k => this.MayUnify(k.First, k.Second));
    }

    // A pattern needs nodes of a virtual node's head in this class (or a meeting pairs two virtual nodes): the virtual node agrees with
    // the pattern as a node (§1), its variables bound as far as the pattern resolves; agreement requests the deferred row on behalf of
    // the requester, kid classes that differ meet first (their merge wakes the requester, which matches again).
    private void Expand(Virtual vs, Term p, int?[] beta, MatchCtx ctx)
    {
        this.work++;
        if (vs.Via is { } via)
        {
            this.Expand(via, vs.ViaPattern!, vs.ViaBeta!, ctx);
            return;
        }

        var bind = (int?[])vs.Beta.Clone();
        var shapes = new Dictionary<int, Head>();
        var meets = new List<(int, int)>();
        if (IsAlias(vs) || this.Agree(vs.Other, bind, p, beta, shapes, meets))
        {
            this.Settle(vs, bind, shapes, meets, ctx);
        }
    }

    // Matching modulo classes (§1): a virtual node's term o (variables bound in into) and a pattern p (bound in bp) can be one Type.
    // A subterm instantiated to a class matches by its class (AgreeClass); a free variable on either side matches anything (a free
    // variable of o records the head p needs there); otherwise heads and kids, a compound kid with a free variable by shape only.
    // False only when heads differ.
    private bool Agree(Term o, int?[] into, Term p, int?[] bp, Dictionary<int, Head> shapes, List<(int, int)> meets)
    {
        var cp = p.Head is VarHead pv ? bp[pv.Index] ?? -1 : this.ClassOf(p, bp);
        if (cp >= 0)
        {
            return this.AgreeClass(o, into, cp, meets);
        }

        if (p.Head is VarHead)
        {
            return true;
        }

        if (o.Head is VarHead ov)
        {
            if (into[ov.Index] is null)
            {
                shapes[ov.Index] = p.Head;
            }

            return true;
        }

        if (this.ClassOf(o, into) >= 0)
        {
            return true; // an existing Type and one not built yet: decided by matching once the row is built
        }

        if (!o.Head.Equals(p.Head) || o.Kids.Length != p.Kids.Length)
        {
            return false;
        }

        // A compound kid with a free variable is not one Type yet (its row's antecedent binds the variable): it agrees by shape and
        // is decided once built; other kids agree as above.
        for (var i = 0; i < o.Kids.Length; i++)
        {
            var (ko, kp) = (o.Kids[i], p.Kids[i]);
            if ((ko.Head is not VarHead && HasFree(ko, into)) || (kp.Head is not VarHead && HasFree(kp, bp)) ? !SameShape(ko, kp) : !this.Agree(ko, into, kp, bp, shapes, meets))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasFree(Term t, int?[] beta) => t.Head is VarHead v ? beta[v.Index] is null : t.HasVar && t.Kids.Any(k => HasFree(k, beta));

    private static bool SameShape(Term a, Term b)
        => a.Head is VarHead || b.Head is VarHead || (a.Head.Equals(b.Head) && a.Kids.Length == b.Kids.Length && a.Kids.Zip(b.Kids).All(k => SameShape(k.First, k.Second)));

    // o and a class c: a bound variable or a subterm instantiated to a class matches by class (different classes meet there); a free
    // variable takes c; a subterm not built yet is decided once built.
    private bool AgreeClass(Term o, int?[] into, int c, List<(int, int)> meets)
    {
        c = this.g.Find(c);
        var co = o.Head is VarHead ov ? (into[ov.Index] is int bound ? this.g.Find(bound) : -1) : this.ClassOf(o, into);
        if (o.Head is VarHead fv && into[fv.Index] is null)
        {
            into[fv.Index] = c;
        }
        else if (co >= 0 && co != c)
        {
            meets.Add((co, c));
        }

        return true;
    }

    // An agreed virtual node: kid classes that differ meet first; otherwise its row is requested on behalf of the requester (unless no
    // fact of its antecedent Contract can have the shape the requester needs at a free variable's position).
    private void Settle(Virtual vs, int?[] bind, Dictionary<int, Head> shapes, List<(int, int)> meets, MatchCtx ctx)
    {
        if (meets.Count != 0)
        {
            foreach (var (a, b) in meets)
            {
                this.Meet(a, b, ctx);
            }

            return;
        }

        if (vs.T.Antecedent is { } k)
        {
            foreach (var (v, shape) in shapes)
            {
                if (bind[v] is null && v <= k.Arity && !this.anyHead.Contains((k, v)) && !this.positionHeads[(k, v)].Contains(shape))
                {
                    return;
                }
            }
        }

        var key = (vs.T.Id, this.g.Find(vs.Class), BindKey(bind), ctx.Owner!, ctx.Sign);
        if (this.expansions.Add(key))
        {
            this.Request(vs.T, bind, ctx.Tags, ctx.Sign, ctx.Owner!, ctx.Fx!);
        }
    }

}
