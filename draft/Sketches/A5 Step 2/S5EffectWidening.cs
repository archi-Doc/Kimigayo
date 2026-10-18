#:property Nullable=disable
// Model of S5 (draft/Proposals/2026-10-19 Effect Atoms Widen.md): effect summaries in terms of a declaration's own Type parameters
// (SPEC 15.6.4), composed through each call's substitution with normalization (SPEC 8.2.3, 8.5) and destruction resolution (SPEC 16.3.3).
// A summary keeps destructions of abstract Types (Type parameters and associated-Type projections) as atoms; a concrete part is resolved.
// Rules:
//   EXACT  the current letter: no widening; a projection-depth cap marks an infinite fixed point (the cap is the model's, not a rule).
//   S5     the proposal: an atom on a projection with a base proven to conform to a recursive Contract (SPEC 8.3.3) that passes
//          through a recursive call and is not mapped to itself is recorded as an unclassifiable effect.
//   LIT    the open-key transcription: any projection atom that passes through a recursive call and is not mapped to itself.
//   ANY    every atom on a projection with a recursive-Contract base, recursion or not.
//   DEPTH  alternative (b): exact up to projection depth 4; a deeper atom is a Resource failure.
// A use's verdict: Unknown (an unclassifiable effect, or an abstract atom its premises do not prove Copy), Env (a concrete destruction
// with an environment effect), Resource, or None.
// Run (outside the repository): dotnet run S5EffectWidening.cs [-- -v] [-- -random N]
using System.Text;

var verbose = args.Contains("-v");
var randomIndex = Array.IndexOf(args, "-random");
var randomCount = randomIndex >= 0 ? int.Parse(args[randomIndex + 1]) : 50000;
var rules = new[] { Rule.Exact, Rule.S5, Rule.Lit, Rule.Any, Rule.Depth };
var failures = 0;

Console.WriteLine("case | use | instantiable | expected | EXACT | S5 | LIT | ANY | DEPTH | S5 atoms (max depth)");
foreach (var c in Cases.All())
{
    var results = rules.ToDictionary(r => r, r => new Solver(c.Program, r).Run());
    foreach (var (use, expected) in c.Uses)
    {
        var row = new StringBuilder($"{c.Name} | {use} | {(c.Instantiable ? "yes" : "no")} | {expected}");
        foreach (var r in rules)
        {
            row.Append(" | ").Append(results[r].Verdict(use));
        }

        var s5 = results[Rule.S5];
        row.Append(" | ").Append(s5.AtomCount).Append(" (").Append(s5.MaxDepth).Append(')');
        Console.WriteLine(row);
        var s5Verdict = s5.Verdict(use);
        if (s5.Infinite || (c.Instantiable && s5Verdict != expected))
        {
            failures++;
            Console.WriteLine($"  FAIL: S5 {(s5.Infinite ? "did not terminate" : "differs from the exact verdict on an instantiable program")}");
        }
    }

    if (verbose)
    {
        foreach (var r in rules)
        {
            Console.WriteLine($"  {r}: {results[r].Describe()}");
        }
    }
}

failures += RandomSearch.Run(randomCount);
Console.WriteLine(failures == 0 ? "OK" : $"FAILURES {failures}");
return failures;

enum Rule { Exact, S5, Lit, Any, Depth }

abstract record Ty
{
    public abstract int Depth { get; }

    public abstract bool Abstract { get; }
}

sealed record Par(string Decl, string Name) : Ty
{
    public override int Depth => 0;

    public override bool Abstract => true;

    public override string ToString() => this.Name;
}

sealed record Proj(Ty Base, string Assoc) : Ty
{
    public override int Depth => this.Base.Depth + 1;

    public override bool Abstract => this.Base.Abstract;

    public override string ToString() => $"{this.Base}.{this.Assoc}";
}

sealed record Con(string Name, Ty Arg) : Ty
{
    public override int Depth => this.Arg?.Depth ?? 0;

    public override bool Abstract => false;

    public override string ToString() => this.Arg is null ? this.Name : $"{this.Name}<{this.Arg}>";
}

// A Contract: parents, declared associated Types with their Constraints, and Constraints on inherited ones (SPEC 8.5.3).
sealed record Contract(string Name, string[] Parents, (string Assoc, string[] Constraints)[] Assocs, (string Assoc, string Constraint)[] Refines);

// A conformance of a concrete constructor with at most one argument X, under conditions on X, with associated-Type rules over X.
// Destroying a concrete value destroys its argument; Prog.EnvCons lists the constructors whose destruction has an environment effect.
sealed record Conformance(string Con, string Contract, string[] Conditions, (string Assoc, Ty Rule)[] Rules);

sealed record Decl(string Name, string[] Params, (Ty Type, string Contract)[] Premises, (Ty From, Ty To)[] Identities, Ty[] Drops, (string Callee, (string Param, Ty Type)[] Subst)[] Calls);

sealed class Prog
{
    public Dictionary<string, Contract> Contracts { get; } = new() { ["Copy"] = new("Copy", [], [], []) };

    public List<Conformance> Conformances { get; } = [];

    public HashSet<string> EnvCons { get; } = [];

    public Dictionary<string, Decl> Decls { get; } = [];

    public static Par X { get; } = new("$", "X"); // A conformance rule's argument.

    public bool Refines(string k, string c) => k == c || this.Contracts[k].Parents.Any(p => this.Refines(p, c));

    // The Constraints a Contract places on an associated Type it declares or inherits (SPEC 8.5.1, 8.5.3); null when it has none.
    public string[] Constraints(string k, string assoc)
    {
        var contract = this.Contracts[k];
        var own = contract.Assocs.Where(a => a.Assoc == assoc).Select(a => a.Constraints).FirstOrDefault();
        var inherited = contract.Parents.Select(p => this.Constraints(p, assoc)).Where(x => x is not null).SelectMany(x => x);
        var refined = contract.Refines.Where(r => r.Assoc == assoc).Select(r => r.Constraint);
        if (own is null && !contract.Parents.Any(p => this.Constraints(p, assoc) is not null))
        {
            return null;
        }

        return [.. (own ?? []).Concat(inherited).Concat(refined).Distinct()];
    }

    // SPEC 8.3.3: a Contract whose associated-Type Constraints, its own or those on inherited associated Types, lead back to it.
    private readonly Dictionary<string, bool> recursive = [];

    public bool Recursive(string k)
    {
        if (!this.recursive.TryGetValue(k, out var known))
        {
            this.recursive[k] = known = this.FindRecursion(k);
        }

        return known;
    }

    private bool FindRecursion(string k)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>(this.Next(k));
        while (stack.Count > 0)
        {
            var m = stack.Pop();
            if (this.Refines(m, k))
            {
                return true;
            }

            if (seen.Add(m))
            {
                foreach (var n in this.Next(m))
                {
                    stack.Push(n);
                }
            }
        }

        return false;
    }

    private IEnumerable<string> Next(string k) => this.AllAssocs(k).SelectMany(a => this.Constraints(k, a) ?? []).Concat(this.Contracts[k].Parents);

    public IEnumerable<string> AllAssocs(string k) => this.Contracts[k].Assocs.Select(a => a.Assoc).Concat(this.Contracts[k].Parents.SelectMany(this.AllAssocs)).Distinct();
}

// Proofs, normalization and destruction resolution in one declaration's scope; conformances in progress supply no evidence (SPEC 8.3.3).
sealed class Scope(Prog program, Decl decl, HashSet<Conformance> valid)
{
    public Decl Decl => decl;

    private readonly Dictionary<(Ty, string), bool> cache = [];

    public bool Proves(Ty x, string c, int depth = 0)
    {
        if (depth > 24)
        {
            return false;
        }

        x = this.Norm(x);
        if (this.cache.TryGetValue((x, c), out var known))
        {
            return known;
        }

        this.cache[(x, c)] = false; // In progress: no evidence from itself.
        return this.cache[(x, c)] = this.Prove(x, c, depth);
    }

    private bool Prove(Ty x, string c, int depth)
    {
        if (x.Abstract)
        {
            if (decl.Premises.Any(p => this.Norm(p.Type) == x && program.Refines(p.Contract, c)))
            {
                return true;
            }

            return x is Proj(var b, var a) && program.Contracts.Keys.Any(k => program.Constraints(k, a) is { } cons
                && cons.Any(m => program.Refines(m, c)) && this.Proves(b, k, depth + 1));
        }

        if (x is not Con con)
        {
            return false; // A projection of a concrete Type that no conformance reduces.
        }

        return valid.Any(f => f.Con == con.Name && program.Refines(f.Contract, c)
            && f.Conditions.All(cond => con.Arg is not null && this.Proves(con.Arg, cond, depth + 1)));
    }

    public Ty Norm(Ty x)
    {
        x = x switch
        {
            Proj(var b, var a) => this.Reduce(this.Norm(b), a),
            Con(var n, var arg) => new Con(n, arg is null ? null : this.Norm(arg)),
            _ => x,
        };
        foreach (var (from, to) in decl.Identities)
        {
            if (x == from)
            {
                return this.Norm(to);
            }
        }

        return x;
    }

    private Ty Reduce(Ty b, string a)
    {
        if (b is Con con)
        {
            foreach (var f in valid.Where(f => f.Con == con.Name))
            {
                if (f.Rules.FirstOrDefault(r => r.Assoc == a).Rule is { } rule && f.Conditions.All(cond => con.Arg is not null && this.Proves(con.Arg, cond)))
                {
                    return this.Norm(Subst.Apply(rule, p => p == Prog.X ? con.Arg : p));
                }
            }
        }

        return new Proj(b, a);
    }

    // The atoms and the environment effect of destroying a value of a Type (SPEC 16.3.3): an abstract Type is an atom; a concrete one
    // destroys its argument and has an environment effect when its constructor is marked.
    public void Resolve(Ty x, List<Ty> atoms, ref bool env)
    {
        x = this.Norm(x);
        if (x.Abstract)
        {
            atoms.Add(x);
            return;
        }

        if (x is not Con con)
        {
            atoms.Add(x); // A projection of a concrete Type that no conformance reduces (not well-formed).
            return;
        }

        env |= program.EnvCons.Contains(con.Name);
        if (con.Arg is not null)
        {
            this.Resolve(con.Arg, atoms, ref env);
        }
    }

    // A projection with a base (at any depth) proven to conform to a recursive Contract.
    public bool RecursiveBase(Ty x)
    {
        for (var b = (x as Proj)?.Base; b is not null; b = (b as Proj)?.Base)
        {
            if (program.Contracts.Keys.Any(k => program.Recursive(k) && this.Proves(b, k)))
            {
                return true;
            }
        }

        return false;
    }

    public bool WellFormed(Ty x) => x switch
    {
        Par => true,
        Proj(var b, var a) => this.WellFormed(b) && (this.Norm(b).Abstract || this.Norm(new Proj(b, a)) is not Proj { Base: Con })
            && program.Contracts.Keys.Any(k => program.AllAssocs(k).Contains(a) && this.Proves(b, k)),
        Con(var n, var arg) => arg is null || this.WellFormed(arg),
        _ => false,
    };
}

static class Subst
{
    public static Ty Apply(Ty x, Func<Par, Ty> map) => x switch
    {
        Par p => map(p),
        Proj(var b, var a) => new Proj(Apply(b, map), a),
        Con(var n, var arg) => new Con(n, arg is null ? null : Apply(arg, map)),
        _ => x,
    };
}

sealed class Summary
{
    public HashSet<Ty> Atoms { get; } = [];

    public bool Unknown { get; set; }

    public bool Env { get; set; }

    public bool Resource { get; set; }
}

sealed class Result(Dictionary<string, Summary> summaries, Dictionary<string, Scope> scopes, bool infinite)
{
    public bool Infinite => infinite;

    public int AtomCount => summaries.Values.Sum(s => s.Atoms.Count);

    public int MaxDepth => summaries.Values.SelectMany(s => s.Atoms).Select(a => a.Depth).DefaultIfEmpty(0).Max();

    public Dictionary<string, Summary> Summaries => summaries;

    public string Verdict(string use)
    {
        var s = summaries[use];
        if (infinite)
        {
            return "Infinite";
        }

        if (s.Resource)
        {
            return "Resource";
        }

        if (s.Unknown || s.Atoms.Any(a => !scopes[use].Proves(a, "Copy")))
        {
            return "Unknown";
        }

        return s.Env ? "Env" : "None";
    }

    public string Describe() => string.Join("; ", summaries.Select(kv => $"{kv.Key}: {{{string.Join(", ", kv.Value.Atoms)}}}{(kv.Value.Unknown ? " +unknown" : string.Empty)}{(kv.Value.Env ? " +env" : string.Empty)}{(kv.Value.Resource ? " +resource" : string.Empty)}")) + (infinite ? " (cap reached)" : string.Empty);
}

sealed class Solver(Prog program, Rule rule, int cap = 8)
{
    public static HashSet<Conformance> Validate(Prog program)
    {
        // A conformance is valid when its rules meet the Contract's associated-Type Constraints under its conditions, with only
        // conformances validated before it as evidence (no coinduction, SPEC 8.3.3); repeated until no more become valid.
        var valid = new HashSet<Conformance>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var f in program.Conformances.Where(f => !valid.Contains(f)))
            {
                var scope = new Scope(program, new("$", ["X"], [.. f.Conditions.Select(c => ((Ty)Prog.X, c))], [], [], []), valid);
                var ok = f.Rules.All(r => scope.WellFormed(r.Rule)) && program.AllAssocs(f.Contract).All(a => f.Rules.Any(r => r.Assoc == a)
                    && (program.Constraints(f.Contract, a) ?? []).All(m => scope.Proves(f.Rules.First(r => r.Assoc == a).Rule, m)));
                if (ok)
                {
                    valid.Add(f);
                    changed = true;
                }
            }
        }

        return valid;
    }

    public Result Run()
    {
        var valid = Validate(program);
        var scopes = program.Decls.ToDictionary(d => d.Key, d => new Scope(program, d.Value, valid));
        var summaries = program.Decls.ToDictionary(d => d.Key, _ => new Summary());
        var infinite = false;
        foreach (var (name, decl) in program.Decls)
        {
            foreach (var drop in decl.Drops)
            {
                this.Add(scopes[name], summaries[name], null, drop, false, ref infinite);
            }
        }

        for (var changed = true; changed && !infinite;)
        {
            changed = false;
            foreach (var (name, decl) in program.Decls)
            {
                var caller = summaries[name];
                foreach (var (callee, subst) in decl.Calls)
                {
                    var from = summaries[callee];
                    var recursive = this.Reaches(callee, name);
                    var before = (caller.Atoms.Count, caller.Unknown, caller.Env, caller.Resource);
                    caller.Unknown |= from.Unknown;
                    caller.Env |= from.Env;
                    caller.Resource |= from.Resource;
                    foreach (var atom in from.Atoms.ToArray())
                    {
                        var image = Subst.Apply(atom, p => subst.FirstOrDefault(s => s.Param == p.Name && p.Decl == callee).Type ?? p);
                        var widen = recursive && atom is Proj && (rule == Rule.Lit || (rule == Rule.S5 && scopes[callee].RecursiveBase(atom)));
                        this.Add(scopes[name], caller, widen ? atom : null, image, name == callee, ref infinite);
                    }

                    changed |= before != (caller.Atoms.Count, caller.Unknown, caller.Env, caller.Resource);
                }
            }
        }

        return new(summaries, scopes, infinite);
    }

    // Adds what destroying a value of the Type contributes; under S5 and LIT a widening source widens each image that is not itself.
    private void Add(Scope scope, Summary s, Ty widening, Ty type, bool sameDecl, ref bool infinite)
    {
        var atoms = new List<Ty>();
        var env = false;
        scope.Resolve(type, atoms, ref env);
        s.Env |= env;
        foreach (var atom in atoms)
        {
            if ((widening is not null && !(sameDecl && atom == widening)) || (rule == Rule.Any && scope.RecursiveBase(atom)))
            {
                s.Unknown = true;
            }
            else if (rule == Rule.Depth && atom.Depth > 4)
            {
                s.Resource = true;
            }
            else if (s.Atoms.Add(atom) && (atom.Depth > cap || !atom.Abstract))
            {
                infinite = true;
            }
        }
    }

    private bool Reaches(string from, string to)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>([from]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d == to)
            {
                return true;
            }

            if (seen.Add(d))
            {
                foreach (var (callee, _) in program.Decls[d].Calls)
                {
                    stack.Push(callee);
                }
            }
        }

        return false;
    }
}

sealed record Case(string Name, bool Instantiable, Prog Program, (string Use, string Expected)[] Uses);

static class Cases
{
    private static Par P(string decl, string name) => new(decl, name);

    private static Proj J(Ty b, params string[] assocs) => (Proj)assocs.Aggregate(b, (t, a) => new Proj(t, a));

    private static Prog Tree(Prog p, string name = "Tree", string child = "Child")
    {
        p.Contracts[name] = new(name, [], [(child, [name])], []);
        return p;
    }

    public static IEnumerable<Case> All()
    {
        // 1. walk<T: Tree> destroys T and recurses on T.Child (the proposal's first example). Uses: a generic caller under Tree, and one
        //    under CopyTree (Tree, Copy, Child is CopyTree), whose premises prove every T.Child... Copy (exact None; uninstantiable).
        {
            var p = Tree(new Prog());
            p.Contracts["CopyTree"] = new("CopyTree", ["Tree", "Copy"], [], [("Child", "CopyTree")]);
            p.Decls["walk"] = new("walk", ["T"], [(P("walk", "T"), "Tree")], [], [P("walk", "T")], [("walk", [("T", J(P("walk", "T"), "Child"))])]);
            p.Decls["generic"] = new("generic", ["U"], [(P("generic", "U"), "Tree")], [], [], [("walk", [("T", P("generic", "U"))])]);
            p.Decls["copied"] = new("copied", ["U"], [(P("copied", "U"), "CopyTree")], [], [], [("walk", [("T", P("copied", "U"))])]);
            yield return new("1 walk", false, p, [("generic", "Unknown"), ("copied", "None")]);
        }

        // 2. The identity premise T.Child is T: the recursive call substitutes T itself; no growth.
        {
            var p = Tree(new Prog());
            var t = P("walk", "T");
            p.Decls["walk"] = new("walk", ["T"], [(t, "Tree"), (t, "Copy")], [(J(t, "Child"), t)], [t, J(t, "Child")], [("walk", [("T", J(t, "Child"))])]);
            yield return new("2 identity", false, p, [("walk", "None")]);
        }

        // 3. Mutual recursion through two Contracts that lead back to each other (A.X is B, B.Y is A).
        {
            var p = new Prog();
            p.Contracts["A"] = new("A", [], [("X", ["B"])], []);
            p.Contracts["B"] = new("B", [], [("Y", ["A"])], []);
            p.Decls["f"] = new("f", ["T"], [(P("f", "T"), "A")], [], [P("f", "T")], [("g", [("U", J(P("f", "T"), "X"))])]);
            p.Decls["g"] = new("g", ["U"], [(P("g", "U"), "B")], [], [P("g", "U")], [("f", [("T", J(P("g", "U"), "Y"))])]);
            yield return new("3 mutual", false, p, [("f", "Unknown"), ("g", "Unknown")]);
        }

        // 4. ping<L: Source, R: Source> destroys an L.Item and recurses with its arguments swapped; main calls it with Ints and Texts,
        //    whose Items are Str (no environment effect). Instantiable: S5 and the exact rule agree; LIT widens.
        {
            var p = new Prog();
            p.Contracts["Source"] = new("Source", [], [("Item", [])], []);
            p.Conformances.Add(new("Ints", "Source", [], [("Item", new Con("Str", null))]));
            p.Conformances.Add(new("Texts", "Source", [], [("Item", new Con("Str", null))]));
            var (l, r) = (P("ping", "L"), P("ping", "R"));
            p.Decls["ping"] = new("ping", ["L", "R"], [(l, "Source"), (r, "Source")], [], [J(l, "Item")], [("ping", [("L", r), ("R", l)])]);
            p.Decls["main"] = new("main", [], [], [], [], [("ping", [("L", new Con("Ints", null)), ("R", new Con("Texts", null))])]);
            yield return new("4 swap", true, p, [("main", "None")]);
        }

        // 5. Unwrapping: f<T: C, T.A: C> destroys T and recurses on Wrap<T.A>, whose A is X again: T, then T.A, then T.A again.
        {
            var p = new Prog();
            p.Contracts["C"] = new("C", [], [("A", [])], []);
            p.Conformances.Add(new("Wrap", "C", ["C"], [("A", Prog.X)]));
            p.Conformances.Add(new("Leaf", "C", [], [("A", new Con("Leaf2", null))]));
            p.Conformances.Add(new("Leaf2", "C", [], [("A", new Con("Leaf2", null))]));
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "C"), (J(t, "A"), "C")], [], [t], [("f", [("T", new Con("Wrap", J(t, "A")))])]);
            p.Decls["main"] = new("main", [], [], [], [], [("f", [("T", new Con("Leaf", null))])]);
            yield return new("5 unwrap", true, p, [("main", "None")]);
        }

        // 6. Growth by a conformance's rule: Wrap2<X>.I is X.I.I, which needs C2's I to lead back to C2.
        {
            var p = new Prog();
            p.Contracts["C2"] = new("C2", [], [("I", ["C2"])], []);
            p.Conformances.Add(new("Wrap2", "C2", ["C2"], [("I", J(Prog.X, "I", "I"))]));
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "C2")], [], [J(t, "I")], [("f", [("T", new Con("Wrap2", t))])]);
            yield return new("6 rule growth", false, p, [("f", "Unknown")]);
        }

        // 7. A non-recursive call over a recursive Contract: h<T: Tree> calls k<T.Child>, k<U: Tree> destroys U. Exact and S5 agree.
        {
            var p = Tree(new Prog());
            p.Contracts["CopyTree"] = new("CopyTree", ["Tree", "Copy"], [], [("Child", "CopyTree")]);
            p.Decls["k"] = new("k", ["U"], [(P("k", "U"), "Tree")], [], [P("k", "U")], []);
            p.Decls["h"] = new("h", ["T"], [(P("h", "T"), "CopyTree")], [], [], [("k", [("U", J(P("h", "T"), "Child"))])]);
            yield return new("7 non-recursive", false, p, [("h", "None")]);
        }

        // 8. Recursion without growth over a recursive Contract: f<T: CopyTree> recurses on T itself, destroying T.Child.
        {
            var p = Tree(new Prog());
            p.Contracts["CopyTree"] = new("CopyTree", ["Tree", "Copy"], [], [("Child", "CopyTree")]);
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "CopyTree")], [], [J(t, "Child")], [("f", [("T", t)])]);
            yield return new("8 self", false, p, [("f", "None")]);
        }

        // 9. A non-recursive associated Type under a recursive base: f<T: Tree2> destroys T.Item and recurses on T.Child.
        {
            var p = new Prog();
            p.Contracts["Tree2"] = new("Tree2", [], [("Child", ["Tree2"]), ("Item", [])], []);
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "Tree2")], [], [J(t, "Item")], [("f", [("T", J(t, "Child"))])]);
            yield return new("9 item", false, p, [("f", "Unknown")]);
        }

        // 10. Recursion added by a refinement: Node's Child is unconstrained, Rec: Node constrains it to Rec (SPEC 8.5.3).
        {
            var p = new Prog();
            p.Contracts["Node"] = new("Node", [], [("Child", [])], []);
            p.Contracts["Rec"] = new("Rec", ["Node"], [], [("Child", "Rec")]);
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "Rec")], [], [t], [("f", [("T", J(t, "Child"))])]);
            yield return new("10 refinement", false, p, [("f", "Unknown")]);
        }

        // 11. A concrete environment effect through recursion: g<T: Source> destroys Noisy<T.Item> (Noisy's destruction writes a static)
        //     and recurses on Pair<T>, whose Item is X.Item again: finite; main's exact verdict is Env, and S5 agrees.
        {
            var p = new Prog();
            p.Contracts["Source"] = new("Source", [], [("Item", [])], []);
            p.Conformances.Add(new("Ints", "Source", [], [("Item", new Con("Str", null))]));
            p.Conformances.Add(new("Pair", "Source", ["Source"], [("Item", J(Prog.X, "Item"))]));
            p.EnvCons.Add("Noisy");
            var t = P("g", "T");
            p.Decls["g"] = new("g", ["T"], [(t, "Source")], [], [new Con("Noisy", J(t, "Item"))], [("g", [("T", new Con("Pair", t))])]);
            p.Decls["main"] = new("main", [], [], [], [], [("g", [("T", new Con("Ints", null))])]);
            yield return new("11 env", true, p, [("main", "Env")]);
        }

        // 12. A finite chain deeper than the DEPTH limit: C1.A1 is C2, ..., C5.A5 unconstrained; f<T: C1> destroys T.A1.A2.A3.A4.A5 and
        //     recurses on T itself; main calls it with L1, whose chain ends at Str. Instantiable: S5 is exact, DEPTH a Resource failure.
        {
            var p = new Prog();
            for (var i = 1; i <= 5; i++)
            {
                p.Contracts[$"C{i}"] = new($"C{i}", [], [($"A{i}", i < 5 ? [$"C{i + 1}"] : [])], []);
                p.Conformances.Add(new($"L{i}", $"C{i}", [], [($"A{i}", i < 5 ? new Con($"L{i + 1}", null) : new Con("Str", null))]));
            }

            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "C1")], [], [J(t, "A1", "A2", "A3", "A4", "A5")], [("f", [("T", t)])]);
            p.Decls["main"] = new("main", [], [], [], [], [("f", [("T", new Con("L1", null))])]);
            yield return new("12 deep finite", true, p, [("main", "None")]);
        }
    }
}

// Random small programs: two Contracts and an optional refinement, the conformances of two concrete constructors, one or two
// declarations with premises, drops and calls whose substitutions come from a small grammar and prove the callee's premises. Each
// well-formed program (every Type well-formed, every call proving the callee's premises) is solved under EXACT and S5. Checked: S5
// always terminates; a program whose exact fixed point reaches the cap has an atom on a projection with a recursive-Contract base
// (the proposal's lemma); and where no exact atom has such a base, S5 equals EXACT (same atoms, no widening).
static class RandomSearch
{
    public static int Run(int count)
    {
        var random = new Random(20261019);
        var (wellFormed, infinite, widened, recursiveContracts, failures) = (0, 0, 0, 0, 0);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            if (i % 1000 == 999)
            {
                Console.Error.WriteLine($"  random {i + 1}: {wellFormed} well-formed, {infinite} infinite, {clock.Elapsed.TotalSeconds:F0} s");
            }

            var p = Generate(random);
            if (!WellFormed(p))
            {
                continue;
            }

            wellFormed++;
            if (Environment.GetEnvironmentVariable("S5_TRACE") is not null)
            {
                Console.Error.WriteLine($"  [{i}] {clock.Elapsed.TotalSeconds:F1} s {Show(p)}");
            }

            recursiveContracts += p.Contracts.Keys.Any(p.Recursive) ? 1 : 0;
            var exact = new Solver(p, Rule.Exact).Run();
            var s5 = new Solver(p, Rule.S5).Run();
            var valid = Solver.Validate(p);
            var deepRecursive = exact.Summaries.Any(kv => kv.Value.Atoms.Any(a => new Scope(p, p.Decls[kv.Key], valid).RecursiveBase(a)));
            infinite += exact.Infinite ? 1 : 0;
            widened += s5.Summaries.Values.Any(s => s.Unknown) ? 1 : 0;
            var problem = s5.Infinite ? "S5 did not terminate"
                : exact.Infinite && !deepRecursive ? "infinite without a recursive-Contract base (lemma)"
                : !deepRecursive && !Same(exact, s5) ? "S5 differs from EXACT without a recursive-Contract base"
                : null;
            if (problem is not null)
            {
                failures++;
                if (failures <= 5)
                {
                    Console.WriteLine($"  RANDOM FAIL {i}: {problem}: {Show(p)}");
                }
            }
        }

        Console.WriteLine($"random: {count} generated, {wellFormed} well-formed, {recursiveContracts} with a recursive Contract, {infinite} with an infinite exact fixed point, {widened} widened by S5, {failures} failures");
        return failures;
    }

    private static bool Same(Result a, Result b) => a.Summaries.All(kv => b.Summaries[kv.Key].Atoms.SetEquals(kv.Value.Atoms) && b.Summaries[kv.Key].Unknown == kv.Value.Unknown);

    private static T Pick<T>(Random r, params T[] items) => items[r.Next(items.Length)];

    private static Prog Generate(Random r)
    {
        var p = new Prog();
        string[] constraint() => Pick<string[]>(r, [], ["K1"], ["K2"], ["Copy"], ["K1", "Copy"], ["K2", "Copy"]);
        p.Contracts["K1"] = new("K1", [], [("A", constraint())], []);
        p.Contracts["K2"] = new("K2", [], [("B", constraint())], []);
        if (r.Next(3) == 0)
        {
            p.Contracts["R"] = new("R", ["K1"], [], [("A", Pick(r, "R", "K2", "Copy"))]);
        }

        Ty arg(Ty x) => Pick<Ty>(r, x, new Proj(x, "A"), new Proj(new Proj(x, "A"), "A"), new Con("Wrap", x), new Con("Wrap", new Proj(x, "A")), new Con("Leaf", null));
        p.Conformances.Add(new("Wrap", "K1", Pick<string[]>(r, ["K1"], [], ["K1", "K2"]), [("A", arg(Prog.X))]));
        if (r.Next(2) == 0)
        {
            p.Conformances.Add(new("Wrap", "K2", Pick<string[]>(r, ["K1"], ["K2"], []), [("B", arg(Prog.X) is Proj { Assoc: "A" } ? new Proj(Prog.X, "B") : arg(Prog.X))]));
        }

        p.Conformances.Add(new("Leaf", "K1", [], [("A", new Con("Leaf", null))]));
        p.Conformances.Add(new("Leaf", "K2", [], [("B", new Con("Leaf", null))]));
        var valid = Solver.Validate(p);
        var contracts = p.Contracts.Keys.Where(k => k != "Copy").ToArray();
        var names = r.Next(3) == 0 ? new[] { "f" } : new[] { "f", "g" };
        Ty[] candidates(string name)
        {
            var (t, u) = (new Par(name, "T"), new Par(name, "U"));
            return [t, u, new Proj(t, "A"), new Proj(u, "A"), new Proj(t, "B"), new Proj(u, "B"), new Proj(new Proj(t, "A"), "A"), new Proj(new Proj(t, "A"), "B"),
                new Proj(new Proj(u, "B"), "B"), new Con("Wrap", t), new Con("Wrap", new Proj(t, "A")), new Con("Wrap", u), new Con("Leaf", null)];
        }

        foreach (var name in names)
        {
            var (t, u) = (new Par(name, "T"), new Par(name, "U"));
            var premises = new List<(Ty, string)> { (t, Pick(r, contracts)) };
            if (r.Next(2) == 0)
            {
                premises.Add((u, Pick(r, contracts)));
            }

            var decl = new Decl(name, ["T", "U"], [.. premises], [], [], []);
            var scope = new Scope(p, decl, valid);
            var formed = candidates(name).Where(x => x is not Con && scope.WellFormed(x)).ToArray();
            if (r.Next(3) == 0 && formed.Length > 0)
            {
                premises.Add((Pick(r, formed), Pick(r, contracts)));
                decl = decl with { Premises = [.. premises] };
                scope = new Scope(p, decl, valid);
                formed = candidates(name).Where(x => scope.WellFormed(x)).ToArray();
            }
            else
            {
                formed = candidates(name).Where(x => scope.WellFormed(x)).ToArray();
            }

            var drops = Enumerable.Range(0, 1 + r.Next(2)).Select(_ => Pick(r, formed)).Distinct().ToArray();
            p.Decls[name] = decl with { Drops = drops };
        }

        foreach (var name in names)
        {
            var scope = new Scope(p, p.Decls[name], valid);
            var formed = candidates(name).Where(x => scope.WellFormed(x)).ToArray();
            var calls = new List<(string, (string, Ty)[])>();
            for (var c = 1 + r.Next(2); c > 0; c--)
            {
                var callee = Pick(r, names);
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    (string, Ty)[] subst = [("T", Pick(r, formed)), ("U", Pick(r, formed))];
                    Ty map(Par x) => subst.FirstOrDefault(s => s.Item1 == x.Name && x.Decl == callee).Item2 ?? x;
                    if (p.Decls[callee].Premises.All(x => scope.Proves(Subst.Apply(x.Type, map), x.Contract)))
                    {
                        calls.Add((callee, subst));
                        break;
                    }
                }
            }

            p.Decls[name] = p.Decls[name] with { Calls = [.. calls] };
        }

        return p;
    }

    private static bool WellFormed(Prog p)
    {
        var valid = Solver.Validate(p);
        foreach (var decl in p.Decls.Values)
        {
            var scope = new Scope(p, decl, valid);
            if (decl.Premises.Any(x => !scope.WellFormed(x.Type)) || decl.Drops.Any(x => !scope.WellFormed(x)))
            {
                return false;
            }

            foreach (var (callee, subst) in decl.Calls)
            {
                var target = p.Decls[callee];
                Ty map(Par x) => subst.FirstOrDefault(s => s.Param == x.Name && x.Decl == callee).Type ?? x;
                if (subst.Any(s => !scope.WellFormed(s.Type)) || target.Premises.Any(x => !scope.Proves(Subst.Apply(x.Type, map), x.Contract)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string Show(Prog p) => string.Join("; ", p.Contracts.Values.Where(k => k.Name != "Copy").Select(k => $"{k.Name}:{string.Join(",", k.Parents)} {string.Join(" ", k.Assocs.Select(a => $"{a.Assoc} is {string.Join("&", a.Constraints)}"))} {string.Join(" ", k.Refines.Select(x => $"{x.Assoc} is {x.Constraint}"))}"))
        + " | " + string.Join("; ", p.Conformances.Select(f => $"{f.Con}:{f.Contract} when {string.Join("&", f.Conditions)} {string.Join(" ", f.Rules.Select(x => $"{x.Assoc}={x.Rule}"))}"))
        + " | " + string.Join("; ", p.Decls.Values.Select(d => $"{d.Name} [{string.Join(", ", d.Premises.Select(x => $"{x.Type}:{x.Contract}"))}] drops {string.Join(",", d.Drops)} calls {string.Join(" ", d.Calls.Select(c => $"{c.Callee}<{string.Join(",", c.Subst.Select(s => $"{s.Param}={s.Type}"))}>"))}"));
}
