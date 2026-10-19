#:property Nullable=disable
// Model of S5 (draft/Changes/2026-10-19 Effect Atoms Widen.md): effect summaries in terms of a declaration's own Type parameters
// (SPEC 15.6.4), composed along each summary dependency (a call, or a destruction that runs a drop body) through its substitution, with
// normalization (SPEC 8.2.3, 8.5) and destruction resolution (SPEC 16.3.3). A summary keeps the destruction of an abstract Type (a Type
// parameter or an associated-Type projection) that its declaration's premises do not prove Copy as an atom; a concrete part is resolved,
// a Type with a drop body runs that body's summary. Every parameter-dependent effect kind (destruction, a published row's requirement, a
// call through a Callable input) behaves alike here, so only destructions are modelled.
// A Contract C is recursive when T is C supplies (SPEC 8.3.2), directly or through the Constraints of the Contracts so supplied, a
// conformance of a projection of T to C's declaration or to a Contract that refines it (Prog.Recursive). "A recursive base": one of the
// projection's bases, at any depth, proven to conform to a recursive Contract.
// Rules:
//   EXACT  the current letter: no widening; a projection-depth cap marks an infinite fixed point (the cap is the model's, not a rule).
//   S5C    form (c), the owner's choice: an atom with a recursive base is recorded as unknown environment effects, wherever it is kept.
//   ENUM   S5C with "recursive" defined by the second draft's enumeration, which misses Constraints on deeper projections of Self
//          (review B5; case 18).
//   S5A    form (a), rejected: an atom with a recursive base that passes along a dependency on a cycle of dependencies (calls and drop
//          bodies), and whose substituted Type is not itself in the same declaration, is recorded as unknown environment effects.
//   S5A1   form (a) with cycles over call edges only (the first draft; review B3).
//   IMM    form (a) with the immediate base only (review B1's reading).
//   LIT    the open-key transcription: any projection atom that passes along a dependency cycle and is not mapped to itself.
//   DEPTH  alternative (b): exact up to projection depth 4; a deeper atom is a Resource failure.
// A use's verdict: Unknown (unknown environment effects, or an abstract atom its premises do not prove Copy), Env (a concrete destruction
// with an environment effect), Resource, Infinite (the model's cap), or None.
// Run (outside the repository): dotnet run S5EffectWidening.cs [-- -v] [-- -random N]
using System.Text;

var verbose = args.Contains("-v");
var randomIndex = Array.IndexOf(args, "-random");
var randomCount = randomIndex >= 0 ? int.Parse(args[randomIndex + 1]) : 50000;
var rules = new[] { Rule.Exact, Rule.S5C, Rule.Enum, Rule.S5A, Rule.S5A1, Rule.Imm, Rule.Lit, Rule.Depth };
var failures = 0;

Console.WriteLine("case | use | instantiable | expected | EXACT | S5C | ENUM | S5A | S5A1 | IMM | LIT | DEPTH");
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

        Console.WriteLine(row);
        foreach (var r in new[] { Rule.S5C, Rule.S5A })
        {
            if (results[r].Infinite || (c.Instantiable && results[r].Verdict(use) != expected))
            {
                failures++;
                Console.WriteLine($"  FAIL: {r} {(results[r].Infinite ? "did not terminate" : "differs from the exact verdict on an instantiable program")}");
            }
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

enum Rule { Exact, S5C, Enum, S5A, S5A1, Imm, Lit, Depth }

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

// A Contract: parents, declared associated Types with their Constraints, and Constraints on inherited ones (SPEC 8.5.3). Contracts have
// no Type arguments here (review B2 stays unmodelled).
sealed record Contract(string Name, string[] Parents, (string Assoc, string[] Constraints)[] Assocs, (string Assoc, string Constraint)[] Refines);

// A conformance of a concrete constructor with at most one argument X, under conditions on X, with associated-Type rules over X.
sealed record Conformance(string Con, string Contract, string[] Conditions, (string Assoc, Ty Rule)[] Rules);

sealed record Decl(string Name, string[] Params, (Ty Type, string Contract)[] Premises, (Ty From, Ty To)[] Identities, Ty[] Drops, (string Callee, (string Param, Ty Type)[] Subst)[] Calls);

sealed class Prog
{
    private readonly Dictionary<string, bool> recursive = [];

    public Dictionary<string, Contract> Contracts { get; } = new() { ["Copy"] = new("Copy", [], [], []) };

    public List<Conformance> Conformances { get; } = [];

    // Constructors whose destruction has an environment effect; the others destroy their argument.
    public HashSet<string> EnvCons { get; } = [];

    // Constructors with a drop body: destroying Con<A> runs the body's declaration (one parameter, X) with X := A, and the body alone
    // destroys the parts (its Drops); StructPremises are the constructor's own Constraints on X, needed to form Con<A>.
    public Dictionary<string, string> DropBodies { get; } = [];

    public Dictionary<string, string[]> StructPremises { get; } = [];

    public Dictionary<string, Decl> Decls { get; } = [];

    // Contract-wide Constraints on a deeper projection of Self, `Self.(K).P1.(...).Pn is M` declared in K (SPEC Appendix F
    // ConstraintSubject; review B5): (K, [P1 ... Pn], M).
    public List<(string Contract, string[] Path, string Target)> Nested { get; } = [];

    // Whether Recursive counts Nested (false only under ENUM, which reproduces the second draft's enumeration).
    public bool CountNested { get; set; } = true;

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

    // SPEC 8.3.3 as the proposal defines it, through the proof rule "Contract refinement" (SPEC 8.3.2): T is k supplies the conformances
    // of projections of T that k's Constraints (declared on its associated Types, inherited, placed on inherited ones, or on deeper
    // projections of Self) require, and then those the supplied Contracts' Constraints require; k is recursive when one of them is a
    // conformance to k or to a Contract that refines it.
    public bool Recursive(string k)
    {
        var key = this.CountNested ? k : k + "#enumeration";
        if (!this.recursive.TryGetValue(key, out var known))
        {
            this.recursive[key] = known = this.FindRecursion(k);
        }

        return known;
    }

    public IEnumerable<string> AllAssocs(string k) => this.Contracts[k].Assocs.Select(a => a.Assoc).Concat(this.Contracts[k].Parents.SelectMany(this.AllAssocs)).Distinct();

    private bool FindRecursion(string k)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>(this.Yields(k));
        while (stack.Count > 0)
        {
            var m = stack.Pop();
            if (this.Refines(m, k))
            {
                return true;
            }

            if (seen.Add(m))
            {
                foreach (var n in this.Yields(m))
                {
                    stack.Push(n);
                }
            }
        }

        return false;
    }

    // The Contracts whose conformance T is k supplies for a projection of T, one Constraint deep (every Constraint here names a nonempty
    // path, so each is a projection of T).
    private IEnumerable<string> Yields(string k) => this.AllAssocs(k).SelectMany(a => this.Constraints(k, a) ?? [])
        .Concat(this.CountNested ? this.Nested.Where(n => this.Refines(k, n.Contract)).Select(n => n.Target) : []);
}

// Proofs, normalization and destruction resolution in one declaration's scope; conformances in progress supply no evidence (SPEC 8.3.3).
sealed class Scope(Prog program, Decl decl, HashSet<Conformance> valid)
{
    private readonly Dictionary<(Ty, string), bool> cache = [];

    public Decl Decl => decl;

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

    // What destroying a value of a Type does (SPEC 16.3.3): an abstract Type is an atom; a concrete one with a drop body runs that body
    // (bodies), any other destroys its argument; a marked constructor has an environment effect.
    public void Resolve(Ty x, List<Ty> atoms, List<(string Body, Ty Arg)> bodies, ref bool env)
    {
        x = this.Norm(x);
        if (x.Abstract || x is not Con con)
        {
            atoms.Add(x); // A projection of a concrete Type that no conformance reduces is not well-formed; the solver flags it.
            return;
        }

        env |= program.EnvCons.Contains(con.Name);
        if (program.DropBodies.TryGetValue(con.Name, out var body))
        {
            bodies.Add((body, con.Arg));
        }
        else if (con.Arg is not null)
        {
            this.Resolve(con.Arg, atoms, bodies, ref env);
        }
    }

    // A projection one of whose bases (its base, that base's base, and so on) is proven to conform to a recursive Contract.
    public bool RecursiveBase(Ty x)
    {
        for (var b = (x as Proj)?.Base; b is not null; b = (b as Proj)?.Base)
        {
            if (this.ConformsToRecursive(b))
            {
                return true;
            }
        }

        return false;
    }

    // The first draft's reading under SPEC 8.5.2's "the base": the immediate base only (review B1; rule IMM).
    public bool ImmediateRecursiveBase(Ty x) => x is Proj(var b, _) && this.ConformsToRecursive(b);

    public bool WellFormed(Ty x) => x switch
    {
        Par => true,
        Proj(var b, var a) => this.WellFormed(b) && (this.Norm(b).Abstract || this.Norm(new Proj(b, a)) is not Proj { Base: Con })
            && program.Contracts.Keys.Any(k => program.AllAssocs(k).Contains(a) && this.Proves(b, k)),
        Con(var n, var arg) => (arg is null || this.WellFormed(arg))
            && (!program.StructPremises.TryGetValue(n, out var premises) || premises.All(c => arg is not null && this.Proves(arg, c))),
        _ => false,
    };

    private bool ConformsToRecursive(Ty b) => program.Contracts.Keys.Any(k => program.Recursive(k) && this.Proves(b, k));

    private bool Prove(Ty x, string c, int depth)
    {
        if (x.Abstract)
        {
            if (decl.Premises.Any(p => this.Norm(p.Type) == x && program.Refines(p.Contract, c)))
            {
                return true;
            }

            if (x is Proj(var b, var a) && program.Contracts.Keys.Any(k => program.Constraints(k, a) is { } cons
                && cons.Any(m => program.Refines(m, c)) && this.Proves(b, k, depth + 1)))
            {
                return true;
            }

            // A Contract-wide Constraint on a deeper projection: x is y.P1...Pn with y proven to conform to the declaring Contract.
            foreach (var (k, path, target) in program.Nested.Where(n => program.Refines(n.Target, c)))
            {
                var (y, matched) = (x, true);
                for (var i = path.Length - 1; i >= 0 && matched; i--)
                {
                    matched = y is Proj(_, var step) && step == path[i];
                    y = (y as Proj)?.Base;
                }

                if (matched && y is not null && this.Proves(y, k, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (x is not Con con)
        {
            return false; // A projection of a concrete Type that no conformance reduces.
        }

        return valid.Any(f => f.Con == con.Name && program.Refines(f.Contract, c)
            && f.Conditions.All(cond => con.Arg is not null && this.Proves(con.Arg, cond, depth + 1)));
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

    public HashSet<(string Body, Ty Arg)> Bodies { get; } = []; // The drop bodies its destructions run, with their arguments.

    public bool Unknown { get; set; }

    public bool Env { get; set; }

    public bool Resource { get; set; }
}

sealed class Result(Dictionary<string, Summary> summaries, Dictionary<string, Scope> scopes, bool infinite)
{
    public bool Infinite => infinite;

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

    public string Describe() => string.Join("; ", summaries.Select(kv => $"{kv.Key}: {{{string.Join(", ", kv.Value.Atoms)}}}"
        + $"{(kv.Value.Unknown ? " +unknown" : string.Empty)}{(kv.Value.Env ? " +env" : string.Empty)}{(kv.Value.Resource ? " +resource" : string.Empty)}"))
        + (infinite ? " (cap reached)" : string.Empty);
}

sealed class Solver(Prog program, Rule rule, int cap = 8)
{
    private readonly HashSet<(string From, string To)> dependencies = [];
    private Dictionary<string, Scope> scopes;
    private Dictionary<string, Summary> summaries;
    private bool infinite;

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

    // The fixed point; for the rules that test recursion, rerun until the dependency graph (calls and the drop bodies found) is complete,
    // so the cycles are those of the final graph.
    public Result Run()
    {
        program.CountNested = rule != Rule.Enum;
        try
        {
            return this.RunRule();
        }
        finally
        {
            program.CountNested = true;
        }
    }

    private Result RunRule()
    {
        var valid = Validate(program);
        foreach (var (name, decl) in program.Decls)
        {
            foreach (var (callee, _) in decl.Calls)
            {
                this.dependencies.Add((name, callee));
            }
        }

        while (true)
        {
            var known = this.dependencies.Count;
            this.Solve(valid);
            if (this.infinite || this.dependencies.Count == known || rule is not (Rule.S5A or Rule.Imm or Rule.Lit))
            {
                return new(this.summaries, this.scopes, this.infinite);
            }
        }
    }

    private void Solve(HashSet<Conformance> valid)
    {
        this.scopes = program.Decls.ToDictionary(d => d.Key, d => new Scope(program, d.Value, valid));
        this.summaries = program.Decls.ToDictionary(d => d.Key, _ => new Summary());
        this.infinite = false;
        foreach (var (name, decl) in program.Decls)
        {
            foreach (var drop in decl.Drops)
            {
                this.Add(name, drop);
            }
        }

        for (var changed = true; changed && !this.infinite;)
        {
            changed = false;
            foreach (var (name, decl) in program.Decls)
            {
                var s = this.summaries[name];
                var before = (s.Atoms.Count, s.Bodies.Count, s.Unknown, s.Env, s.Resource);
                foreach (var (callee, subst) in decl.Calls)
                {
                    this.Compose(name, callee, p => subst.FirstOrDefault(x => x.Param == p.Name && p.Decl == callee).Type ?? p, call: true);
                }

                foreach (var (body, arg) in s.Bodies.ToArray())
                {
                    this.Compose(name, body, p => p.Decl == body && p.Name == "X" ? arg : p, call: false);
                }

                changed |= before != (s.Atoms.Count, s.Bodies.Count, s.Unknown, s.Env, s.Resource);
            }
        }
    }

    // A dependency of the caller on the callee's summary (a call, or a drop body), composed through the substitution; under S5A, IMM and
    // LIT a source atom that passes along a dependency on a cycle of dependencies, and under S5A1 one that passes through a call on a
    // cycle of calls, whose substituted Type is not itself in the same declaration, is unknown.
    private void Compose(string caller, string callee, Func<Par, Ty> map, bool call)
    {
        var (to, from) = (this.summaries[caller], this.summaries[callee]);
        to.Unknown |= from.Unknown;
        to.Env |= from.Env;
        to.Resource |= from.Resource;
        var cyclic = rule == Rule.S5A1 ? call && this.Reaches(callee, caller, callsOnly: true)
            : rule is Rule.S5A or Rule.Imm or Rule.Lit && this.Reaches(callee, caller, callsOnly: false);
        foreach (var atom in from.Atoms.ToArray())
        {
            var image = Subst.Apply(atom, map);
            var widening = cyclic && atom is Proj && rule switch
            {
                Rule.Lit => true,
                Rule.Imm => this.scopes[callee].ImmediateRecursiveBase(atom),
                _ => this.scopes[callee].RecursiveBase(atom),
            };
            if (widening && !(caller == callee && this.scopes[caller].Norm(image) == atom))
            {
                to.Unknown = true;
                continue;
            }

            this.Add(caller, image);
        }
    }

    // Adds what destroying a value of the Type contributes to the declaration's summary: nothing for a Type its premises prove Copy
    // (SPEC 16.3.3 "unless the Type is Copy", 8.11.2's row "not proven Copy"); under S5C unknown environment effects for an atom with a recursive base; under DEPTH a Resource failure past
    // depth 4; the drop bodies it runs as dependencies.
    private void Add(string name, Ty type)
    {
        var (scope, s) = (this.scopes[name], this.summaries[name]);
        var atoms = new List<Ty>();
        var bodies = new List<(string Body, Ty Arg)>();
        var env = false;
        scope.Resolve(type, atoms, bodies, ref env);
        s.Env |= env;
        foreach (var (body, arg) in bodies)
        {
            this.dependencies.Add((name, body));
            this.infinite |= s.Bodies.Add((body, arg)) && arg.Depth > cap;
        }

        foreach (var atom in atoms)
        {
            if (scope.Proves(atom, "Copy"))
            {
                continue;
            }

            if (rule is Rule.S5C or Rule.Enum && scope.RecursiveBase(atom))
            {
                s.Unknown = true;
            }
            else if (rule == Rule.Depth && atom.Depth > 4)
            {
                s.Resource = true;
            }
            else if (s.Atoms.Add(atom) && (atom.Depth > cap || !atom.Abstract))
            {
                this.infinite = true;
            }
        }
    }

    private bool Reaches(string from, string to, bool callsOnly)
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
                foreach (var (_, next) in this.dependencies.Where(e => e.From == d && (!callsOnly || program.Decls[d].Calls.Any(c => c.Callee == e.To))))
                {
                    stack.Push(next);
                }
            }
        }

        return false;
    }
}

sealed record Case(string Name, bool Instantiable, Prog Program, (string Use, string Expected)[] Uses);

static class Cases
{
    public static IEnumerable<Case> All()
    {
        // 1. walk<T: Tree> destroys T and recurses on T.Child (the proposal's walk). Uses: a generic caller under Tree, and one under
        //    CopyTree (Tree, Copy, Child is CopyTree), whose premises prove every T.Child... Copy (exact None; uninstantiable).
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
        //    whose Items are Str (no environment effect). Instantiable: S5C and S5A are exact; LIT widens.
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

        // 7. A non-recursive call over a recursive Contract: h<T: CopyTree> calls k<T.Child>, k<U: Tree> destroys U. T.Child is Copy in
        //    h, so nothing reaches h under any rule.
        {
            var p = Tree(new Prog());
            p.Contracts["CopyTree"] = new("CopyTree", ["Tree", "Copy"], [], [("Child", "CopyTree")]);
            p.Decls["k"] = new("k", ["U"], [(P("k", "U"), "Tree")], [], [P("k", "U")], []);
            p.Decls["h"] = new("h", ["T"], [(P("h", "T"), "CopyTree")], [], [], [("k", [("U", J(P("h", "T"), "Child"))])]);
            yield return new("7 non-recursive", false, p, [("h", "None")]);
        }

        // 8. Recursion without growth over a recursive Contract: f<T: CopyTree> recurses on T itself, destroying T.Child (Copy there).
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
        //     and recurses on Pair<T>, whose Item is X.Item again: finite; main's exact verdict is Env, and every rule agrees.
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
        //     recurses on T itself; main calls it with L1, whose chain ends at Str. Instantiable: S5C and S5A are exact, DEPTH a Resource.
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

        // 13. (Review B1.) A recursive base that is not the immediate one: Tree3.Child is Tree3, Tree3.Item is Box, Box.Inner unconstrained;
        //     f destroys T.Item.Inner and recurses on T.Child. Its immediate base T.Item is a Box; T is a Tree3. IMM does not terminate.
        {
            var p = new Prog();
            p.Contracts["Box"] = new("Box", [], [("Inner", [])], []);
            p.Contracts["Tree3"] = new("Tree3", [], [("Child", ["Tree3"]), ("Item", ["Box"])], []);
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "Tree3")], [], [J(t, "Item", "Inner")], [("f", [("T", J(t, "Child"))])]);
            yield return new("13 deep base", false, p, [("f", "Unknown")]);
        }

        // 14. (Review N2.) A concrete image that destroys the source: Wrap<X>.Child is Pair<X.Child>, Pair<Y>.Child is Y.Child; f destroys
        //     T.Child and recurses on Wrap<T>, so T.Child passes as Pair<T.Child>, not itself, though its destruction destroys T.Child.
        {
            var p = Tree(new Prog());
            p.Conformances.Add(new("Pair", "Tree", ["Tree"], [("Child", J(Prog.X, "Child"))]));
            p.Conformances.Add(new("Wrap", "Tree", ["Tree"], [("Child", new Con("Pair", J(Prog.X, "Child")))]));
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "Tree")], [], [J(t, "Child")], [("f", [("T", new Con("Wrap", t))])]);
            yield return new("14 concrete self-image", false, p, [("f", "Unknown")]);
        }

        // 15. (Review B3, probe rv-F.) Growth through cleanup with no recursive call edge: Holder<X: Tree>'s drop body calls look<X>, which
        //     passes its T.Child to make<U>, which destroys a Holder<U>: Holder.drop, look and make depend on each other through a drop
        //     body. S5A1 (call edges only) does not terminate.
        {
            var p = Tree(new Prog());
            p.DropBodies["Holder"] = "drop";
            p.StructPremises["Holder"] = ["Tree"];
            var x = P("drop", "X");
            p.Decls["drop"] = new("drop", ["X"], [(x, "Tree")], [], [x], [("look", [("T", x)])]);
            var t = P("look", "T");
            p.Decls["look"] = new("look", ["T"], [(t, "Tree")], [], [], [("make", [("U", J(t, "Child"))])]);
            var u = P("make", "U");
            p.Decls["make"] = new("make", ["U"], [(u, "Tree")], [], [new Con("Holder", u)], []);
            yield return new("15 cleanup cycle", false, p, [("make", "Unknown"), ("look", "Unknown")]);
        }

        // 16. Option (c)'s extra loss, no recursion: k<U: Tree> destroys U, h<T: Tree> calls k<T.Child>, and g<V: CopyTree> calls h<V>.
        //     Exactly, every destroyed Type is Copy in g (None); S5C widens T.Child in h.
        {
            var p = Tree(new Prog());
            p.Contracts["CopyTree"] = new("CopyTree", ["Tree", "Copy"], [], [("Child", "CopyTree")]);
            p.Decls["k"] = new("k", ["U"], [(P("k", "U"), "Tree")], [], [P("k", "U")], []);
            p.Decls["h"] = new("h", ["T"], [(P("h", "T"), "Tree")], [], [], [("k", [("U", J(P("h", "T"), "Child"))])]);
            p.Decls["g"] = new("g", ["V"], [(P("g", "V"), "CopyTree")], [], [], [("h", [("T", P("g", "V"))])]);
            yield return new("16 stronger caller", false, p, [("g", "None")]);
        }

        // 17. Option (c)'s extra loss, recursion without growth: f<T: Tree> destroys T.Child and recurses on T; g<V: CopyTree> calls f<V>.
        {
            var p = Tree(new Prog());
            p.Contracts["CopyTree"] = new("CopyTree", ["Tree", "Copy"], [], [("Child", "CopyTree")]);
            var t = P("f", "T");
            p.Decls["f"] = new("f", ["T"], [(t, "Tree")], [], [J(t, "Child")], [("f", [("T", t)])]);
            p.Decls["g"] = new("g", ["V"], [(P("g", "V"), "CopyTree")], [], [], [("f", [("T", P("g", "V"))])]);
            yield return new("17 stronger caller, self", false, p, [("g", "None")]);
        }

        // 18. (Review B5, probe rv-G.) A Contract-wide Constraint on a two-step projection of Self: A.X is B, Self.(A).X.(B).Y is A, B.Y
        //     unconstrained. f<T: A> destroys T and calls g<T.X>; g<U: B, U.Y: A> destroys U and calls f<U.Y>. A is recursive by the
        //     proof rule (T is A supplies T.X.Y is A); the second draft's enumeration missed it, so ENUM does not terminate.
        {
            var p = new Prog();
            p.Contracts["B"] = new("B", [], [("Y", [])], []);
            p.Contracts["A"] = new("A", [], [("X", ["B"])], []);
            p.Nested.Add(("A", ["X", "Y"], "A"));
            var (t, u) = (P("f", "T"), P("g", "U"));
            p.Decls["f"] = new("f", ["T"], [(t, "A")], [], [t], [("g", [("U", J(t, "X"))])]);
            p.Decls["g"] = new("g", ["U"], [(u, "B"), (J(u, "Y"), "A")], [], [u], [("f", [("T", J(u, "Y"))])]);
            yield return new("18 deeper Constraint of Self", false, p, [("f", "Unknown"), ("g", "Unknown")]);
        }
    }

    private static Par P(string decl, string name) => new(decl, name);

    private static Proj J(Ty b, params string[] assocs) => (Proj)assocs.Aggregate(b, (t, a) => new Proj(t, a));

    private static Prog Tree(Prog p, string name = "Tree", string child = "Child")
    {
        p.Contracts[name] = new(name, [], [(child, [name])], []);
        return p;
    }
}

// Random small programs: two Contracts (K1 with A and C, K2 with B), an optional refinement R and, when C is K2, sometimes a Constraint
// Self.(K1).C.(K2).B is ...; the conformances of three concrete constructors (Wrap, Leaf and, half the time, Hold with a drop body
// under X: K1); one or two declarations with premises, drops and calls whose substitutions come from a small grammar and prove the
// callee's premises. Contracts have no Type arguments (review B2 is not generated). Each well-formed program is solved under EXACT,
// S5C, S5A, S5A1, IMM and ENUM. Checked: S5C and S5A always terminate; a program whose exact fixed point reaches the cap has an atom
// with a recursive base (the proposal's lemma); and where no exact atom has one, S5C and S5A equal EXACT (same atoms, no unknown
// effects from widening). Counted: how often S5A1, IMM and ENUM do not terminate.
static class RandomSearch
{
    public static int Run(int count)
    {
        var random = new Random(20261019);
        var (wellFormed, recursiveContracts, bodies, nested, infinite, widenedC, widenedA, a1Infinite, immInfinite, enumInfinite, failures) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        for (var i = 0; i < count; i++)
        {
            var p = Generate(random);
            if (!WellFormed(p))
            {
                continue;
            }

            wellFormed++;
            recursiveContracts += p.Contracts.Keys.Any(p.Recursive) ? 1 : 0;
            bodies += p.DropBodies.Count > 0 ? 1 : 0;
            nested += p.Nested.Count > 0 ? 1 : 0;
            var exact = new Solver(p, Rule.Exact).Run();
            var s5c = new Solver(p, Rule.S5C).Run();
            var s5a = new Solver(p, Rule.S5A).Run();
            a1Infinite += new Solver(p, Rule.S5A1).Run().Infinite ? 1 : 0;
            immInfinite += new Solver(p, Rule.Imm).Run().Infinite ? 1 : 0;
            enumInfinite += new Solver(p, Rule.Enum).Run().Infinite ? 1 : 0;
            var valid = Solver.Validate(p);
            var recursiveAtom = exact.Summaries.Any(kv => kv.Value.Atoms.Any(a => new Scope(p, p.Decls[kv.Key], valid).RecursiveBase(a)));
            infinite += exact.Infinite ? 1 : 0;
            widenedC += !Same(exact, s5c) ? 1 : 0;
            widenedA += !Same(exact, s5a) ? 1 : 0;
            var problem = s5c.Infinite ? "S5C did not terminate"
                : s5a.Infinite ? "S5A did not terminate"
                : exact.Infinite && !recursiveAtom ? "infinite without a recursive base (lemma)"
                : !recursiveAtom && !Same(exact, s5c) ? "S5C differs from EXACT without a recursive base"
                : !recursiveAtom && !Same(exact, s5a) ? "S5A differs from EXACT without a recursive base"
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

        Console.WriteLine($"random: {count} generated, {wellFormed} well-formed, {recursiveContracts} with a recursive Contract, {bodies} with a drop body, "
            + $"{nested} with a Constraint on a deeper projection of Self, {infinite} with an infinite exact fixed point; S5C changed {widenedC}, S5A changed {widenedA}; {failures} failures; "
            + $"did not terminate: S5A1 {a1Infinite}, IMM {immInfinite}, ENUM {enumInfinite}");
        return failures;
    }

    private static bool Same(Result a, Result b) => !a.Infinite && !b.Infinite && a.Summaries.All(kv => b.Summaries[kv.Key].Atoms.SetEquals(kv.Value.Atoms)
        && b.Summaries[kv.Key].Unknown == kv.Value.Unknown);

    private static T Pick<T>(Random r, params T[] items) => items[r.Next(items.Length)];

    private static Prog Generate(Random r)
    {
        var p = new Prog();
        string[] constraint() => Pick<string[]>(r, [], ["K1"], ["K2"], ["Copy"], ["K1", "Copy"], ["K2", "Copy"]);
        p.Contracts["K1"] = new("K1", [], [("A", constraint()), ("C", Pick<string[]>(r, [], ["K2"], ["K2", "Copy"], ["Copy"]))], []);
        p.Contracts["K2"] = new("K2", [], [("B", constraint())], []);
        if (p.Contracts["K1"].Assocs[1].Constraints.Contains("K2") && r.Next(3) == 0)
        {
            p.Nested.Add(("K1", ["C", "B"], Pick(r, "K1", "K2", "Copy"))); // Self.(K1).C.(K2).B is ...
        }
        if (r.Next(3) == 0)
        {
            p.Contracts["R"] = new("R", ["K1"], [], [("A", Pick(r, "R", "K2", "Copy"))]);
        }

        Ty arg(Ty x) => Pick<Ty>(r, x, new Proj(x, "A"), new Proj(new Proj(x, "A"), "A"), new Con("Wrap", x), new Con("Wrap", new Proj(x, "A")), new Con("Leaf", null));
        p.Conformances.Add(new("Wrap", "K1", Pick<string[]>(r, ["K1"], [], ["K1", "K2"]), [("A", arg(Prog.X)), ("C", Pick<Ty>(r, new Proj(Prog.X, "C"), new Con("Leaf", null)))]));
        if (r.Next(2) == 0)
        {
            p.Conformances.Add(new("Wrap", "K2", Pick<string[]>(r, ["K1"], ["K2"], []), [("B", arg(Prog.X) is Proj { Assoc: "A" } ? new Proj(Prog.X, "B") : arg(Prog.X))]));
        }

        p.Conformances.Add(new("Leaf", "K1", [], [("A", new Con("Leaf", null)), ("C", new Con("Leaf", null))]));
        p.Conformances.Add(new("Leaf", "K2", [], [("B", new Con("Leaf", null))]));
        var hold = r.Next(2) == 0;
        if (hold)
        {
            p.DropBodies["Hold"] = "hold";
            p.StructPremises["Hold"] = ["K1"];
        }

        var valid = Solver.Validate(p);
        var contracts = p.Contracts.Keys.Where(k => k != "Copy").ToArray();
        var names = (r.Next(3) == 0 ? new[] { "f" } : new[] { "f", "g" }).Concat(hold ? ["hold"] : []).ToArray();
        Ty[] candidates(string name)
        {
            var (t, u) = (new Par(name, name == "hold" ? "X" : "T"), new Par(name, "U"));
            Ty[] list = [t, u, new Proj(t, "A"), new Proj(u, "A"), new Proj(t, "B"), new Proj(u, "B"), new Proj(new Proj(t, "A"), "A"), new Proj(new Proj(t, "A"), "B"),
                new Proj(new Proj(u, "B"), "B"), new Proj(t, "C"), new Proj(new Proj(t, "C"), "B"), new Proj(new Proj(new Proj(t, "A"), "C"), "B"),
                new Con("Wrap", t), new Con("Wrap", new Proj(t, "A")), new Con("Wrap", u), new Con("Leaf", null)];
            return hold ? [.. list.Where(x => name != "hold" || !x.ToString().Contains('U')), new Con("Hold", t), new Con("Hold", new Proj(t, "A"))] : [.. list.Where(x => name != "hold" || !x.ToString().Contains('U'))];
        }

        foreach (var name in names)
        {
            var t = new Par(name, name == "hold" ? "X" : "T");
            var u = new Par(name, "U");
            var premises = new List<(Ty, string)> { (t, name == "hold" ? "K1" : Pick(r, contracts)) };
            if (name != "hold" && r.Next(2) == 0)
            {
                premises.Add((u, Pick(r, contracts)));
            }

            var decl = new Decl(name, name == "hold" ? ["X"] : ["T", "U"], [.. premises], [], [], []);
            var scope = new Scope(p, decl, valid);
            if (name != "hold" && r.Next(3) == 0 && candidates(name).Where(x => x is not Con && scope.WellFormed(x)).ToArray() is { Length: > 0 } extra)
            {
                premises.Add((Pick(r, extra), Pick(r, contracts)));
                decl = decl with { Premises = [.. premises] };
                scope = new Scope(p, decl, valid);
            }

            var formed = candidates(name).Where(x => scope.WellFormed(x)).ToArray();
            var drops = Enumerable.Range(0, 1 + r.Next(2)).Select(_ => Pick(r, formed)).Distinct().ToArray();
            p.Decls[name] = decl with { Drops = name == "hold" ? [.. drops.Append(t).Distinct()] : drops };
        }

        foreach (var name in names)
        {
            var scope = new Scope(p, p.Decls[name], valid);
            var formed = candidates(name).Where(x => scope.WellFormed(x)).ToArray();
            var calls = new List<(string, (string, Ty)[])>();
            for (var c = 1 + r.Next(2); c > 0; c--)
            {
                var callee = Pick(r, names.Where(n => n != "hold").ToArray());
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

    private static string Show(Prog p) => string.Join("; ", p.Contracts.Values.Where(k => k.Name != "Copy")
            .Select(k => $"{k.Name}:{string.Join(",", k.Parents)} {string.Join(" ", k.Assocs.Select(a => $"{a.Assoc} is {string.Join("&", a.Constraints)}"))} "
                + string.Join(" ", k.Refines.Select(x => $"{x.Assoc} is {x.Constraint}"))))
        + " | " + string.Join("; ", p.Conformances.Select(f => $"{f.Con}:{f.Contract} when {string.Join("&", f.Conditions)} {string.Join(" ", f.Rules.Select(x => $"{x.Assoc}={x.Rule}"))}"))
        + " | " + string.Join("; ", p.Decls.Values.Select(d => $"{d.Name} [{string.Join(", ", d.Premises.Select(x => $"{x.Type}:{x.Contract}"))}] drops {string.Join(",", d.Drops)} "
            + $"calls {string.Join(" ", d.Calls.Select(c => $"{c.Callee}<{string.Join(",", c.Subst.Select(s => $"{s.Param}={s.Type}"))}>"))}"));
}
