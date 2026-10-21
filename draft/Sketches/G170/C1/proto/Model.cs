// G170 C1 prototype (BU planning session, 2026-10-21): the input model shared by the reference evaluator, the demand
// evaluator (Solver) and the real-source adapter. Draft data, never part of the solution.
//
// Terms are hash-consed trees with value-equality heads. A projection is Proj(name)[base, ref], a bound Contract reference
// is Ref(decl)[args...], a template variable is Var(i): 0 = Self, 1.. = the Contract's (or a conformance's) own parameters.
#pragma warning disable
#nullable enable
using System.Text;

namespace G170C1;

public enum SymKind : byte { Param, Prim, Con, Ref, Proj, Opaque }

public abstract record Head
{
    public abstract string Show(IReadOnlyList<string> kids);
}

public sealed record VarHead(int Index) : Head
{
    public override string Show(IReadOnlyList<string> kids) => this.Index == 0 ? "Self" : "$" + this.Index;
}

/// <summary>A model head (Kind, Name) or an adapter head whose Key carries the binding identity.</summary>
public sealed record SymHead(SymKind Kind, string Name, object? Key = null) : Head
{
    public bool Equals(SymHead? other) => other is not null && this.Kind == other.Kind && (this.Key is null ? other.Key is null && this.Name == other.Name : Equals(this.Key, other.Key));

    public override int GetHashCode() => this.Key is null ? HashCode.Combine(this.Kind, this.Name) : HashCode.Combine(this.Kind, this.Key);

    public override string Show(IReadOnlyList<string> kids) => this.Kind switch
    {
        SymKind.Proj => kids[0] + ".(" + kids[1] + ")." + this.Name,
        _ => kids.Count == 0 ? this.Name : this.Name + "<" + string.Join(", ", kids) + ">",
    };
}

public sealed class Term
{
    internal Term(Head head, Term[] kids, int id)
    {
        this.Head = head;
        this.Kids = kids;
        this.Id = id;
        var depth = 0;
        var hasVar = head is VarHead;
        foreach (var kid in kids)
        {
            depth = Math.Max(depth, kid.Depth + (head is SymHead { Kind: SymKind.Ref } ? 0 : 1));
            hasVar |= kid.HasVar;
        }

        this.Depth = depth;
        this.HasVar = hasVar;
    }

    public Head Head { get; }

    public Term[] Kids { get; }

    public int Id { get; }

    /// <summary>Gets the projection/constructor nesting depth (a reference adds none).</summary>
    public int Depth { get; }

    public bool HasVar { get; }

    public override string ToString() => this.Head.Show(this.Kids.Select(x => x.ToString()).ToArray());

    public void Vars(HashSet<int> into)
    {
        if (this.Head is VarHead v)
        {
            into.Add(v.Index);
        }

        foreach (var kid in this.Kids)
        {
            kid.Vars(into);
        }
    }
}

public sealed class TermStore
{
    private readonly Dictionary<Key, Term> map = new();
    private readonly List<Term> all = new();

    public IReadOnlyList<Term> All => this.all;

    public Term Make(Head head, params Term[] kids)
    {
        var key = new Key(head, kids);
        if (!this.map.TryGetValue(key, out var term))
        {
            term = new(head, kids, this.all.Count);
            this.map.Add(key, term);
            this.all.Add(term);
        }

        return term;
    }

    public Term Var(int index) => this.Make(new VarHead(index));

    public Term Subst(Term pattern, Term?[] binding)
    {
        if (pattern.Head is VarHead v)
        {
            return binding[v.Index] ?? pattern;
        }

        if (!pattern.HasVar)
        {
            return pattern;
        }

        var kids = new Term[pattern.Kids.Length];
        for (var i = 0; i < kids.Length; i++)
        {
            kids[i] = this.Subst(pattern.Kids[i], binding);
        }

        return this.Make(pattern.Head, kids);
    }

    private readonly struct Key(Head head, Term[] kids) : IEquatable<Key>
    {
        private readonly Head head = head;
        private readonly Term[] kids = kids;

        public bool Equals(Key other)
        {
            if (!this.head.Equals(other.head) || this.kids.Length != other.kids.Length)
            {
                return false;
            }

            for (var i = 0; i < this.kids.Length; i++)
            {
                if (!ReferenceEquals(this.kids[i], other.kids[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is Key other && this.Equals(other);

        public override int GetHashCode()
        {
            var hash = this.head.GetHashCode();
            foreach (var kid in this.kids)
            {
                hash = HashCode.Combine(hash, kid.Id);
            }

            return hash;
        }
    }
}

public enum FK : byte { Is, Eq, Not, And, Or }

/// <summary>A proposition: Is(subject, ref), Eq(left, right), Not, And, Or (SPEC 8.2.2; parentheses are transparent).</summary>
public sealed class Formula
{
    public Formula(FK kind, Term? a = null, Term? b = null, Formula? l = null, Formula? r = null)
    {
        this.Kind = kind;
        this.A = a;
        this.B = b;
        this.L = l;
        this.R = r;
    }

    public FK Kind { get; }

    public Term? A { get; }

    public Term? B { get; }

    public Formula? L { get; }

    public Formula? R { get; }

    /// <summary>Gets whether this is an atom C can store as one row: Is, Eq, not Is or not Eq.</summary>
    public bool IsAtom => this.Kind is FK.Is or FK.Eq || (this.Kind == FK.Not && this.L!.Kind is FK.Is or FK.Eq);

    /// <summary>Gets the subject of the clause (all atoms of one Kimigayo clause share it).</summary>
    public Term Subject => this.Kind is FK.Is or FK.Eq ? this.A! : this.L!.Subject;

    public static Formula Negate(Formula f) => f.Kind == FK.Not ? f.L! : new(FK.Not, l: f);

    public override string ToString() => this.Kind switch
    {
        FK.Is => $"{this.A} is {this.B}",
        FK.Eq => $"{this.A} is {this.B}",
        FK.Not => $"not ({this.L})",
        FK.And => $"({this.L}) and ({this.R})",
        _ => $"({this.L}) or ({this.R})",
    };

    public Formula Subst(TermStore terms, Term?[] binding) => this.Kind switch
    {
        FK.Is or FK.Eq => new(this.Kind, terms.Subst(this.A!, binding), terms.Subst(this.B!, binding)),
        FK.Not => new(FK.Not, l: this.L!.Subst(terms, binding)),
        _ => new(this.Kind, l: this.L!.Subst(terms, binding), r: this.R!.Subst(terms, binding)),
    };

    public void Terms(List<Term> into)
    {
        if (this.A is not null)
        {
            into.Add(this.A);
        }

        if (this.B is not null)
        {
            into.Add(this.B);
        }

        this.L?.Terms(into);
        this.R?.Terms(into);
    }

    /// <summary>Conjunction elimination at template level: the atoms and exact compounds an available formula supplies.</summary>
    public IEnumerable<Formula> Supplied()
    {
        if (this.Kind == FK.And)
        {
            yield return this; // the exact compound stays available as written
            foreach (var x in this.L!.Supplied())
            {
                yield return x;
            }

            foreach (var x in this.R!.Supplied())
            {
                yield return x;
            }
        }
        else
        {
            yield return this;
        }
    }
}

public enum Status : byte { Valid, Pending, Invalid }

public sealed class ContractDecl
{
    public ContractDecl(string name, Head refHead, int arity)
    {
        this.Name = name;
        this.RefHead = refHead;
        this.Arity = arity;
    }

    public string Name { get; }

    public Head RefHead { get; }

    public int Arity { get; }

    public List<Term> Parents { get; } = new();

    public List<Formula> Clauses { get; } = new();

    public Status Status { get; set; }

    public List<Term> Ancestors { get; } = new();

    public bool Recursive { get; set; }

    public override string ToString() => this.Name;
}

/// <summary>An associated-Type binding of a verified conformance, read through the inference owner's view.</summary>
public sealed class AssocBinding
{
    public int Id { get; init; }

    public Term Projection { get; init; } = null!;

    public Term Value { get; init; } = null!;

    public Status Status { get; init; }

    public int Definition { get; init; }

    public bool Inferred { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class Conformance
{
    public Term Type { get; init; } = null!;

    public Term Ref { get; init; } = null!;

    public int VarCount { get; init; }

    public Status Status { get; set; }

    public List<Formula> Conditions { get; } = new();

    public List<AssocBinding> Bindings { get; } = new();
}

/// <summary>The evidence view: the inference owner's inadmissible bindings and whether supports' reads are returned.</summary>
public sealed class View
{
    public static readonly View Ordinary = new("ordinary", [], wantsReads: false);

    public View(string name, IEnumerable<int> inadmissible, bool wantsReads)
    {
        this.Name = name;
        this.Inadmissible = new(inadmissible);
        this.WantsReads = wantsReads;
    }

    public string Name { get; }

    public HashSet<int> Inadmissible { get; }

    public bool WantsReads { get; }
}

public enum TemplateKind : byte { Input, Ancestor, Clause, Conformance, Binding }

/// <summary>One existing clause instance shape: conclusion over Var(0..VarCount-1), antecedent `Self is K&lt;$1..$n&gt;` and
/// conformance conditions. Ancestor, clause and conformance rows are the SPEC 8.3.2 refinement and verified-conformance rows.</summary>
public sealed class Template
{
    public int Id { get; init; }

    public TemplateKind Kind { get; init; }

    public ContractDecl? Antecedent { get; init; }

    public Formula Conclusion { get; init; } = null!;

    public int VarCount { get; init; }

    public Status Source { get; init; }

    public List<Formula> Conditions { get; init; } = new();

    public int BindingId { get; init; } = -1;

    public string Origin { get; init; } = string.Empty;

    public override string ToString() => $"#{this.Id} {this.Kind} {this.Origin}: {this.Conclusion}";
}

public sealed class ProgramModel
{
    public TermStore Terms { get; } = new();

    public List<ContractDecl> Decls { get; } = new();

    public Dictionary<Head, ContractDecl> ByHead { get; } = new();

    public List<Conformance> Conformances { get; } = new();

    public List<Formula> Inputs { get; } = new();

    public List<Template> Templates { get; } = new();

    public Dictionary<int, AssocBinding> Bindings { get; } = new();

    public ContractDecl? DeclOf(Term reference) => reference.Head is SymHead { Kind: SymKind.Ref } && this.ByHead.TryGetValue(reference.Head, out var d) ? d : null;

    public ContractDecl AddContract(string name, int arity, object? key = null)
    {
        var decl = new ContractDecl(name, new SymHead(SymKind.Ref, name, key), arity);
        this.Decls.Add(decl);
        this.ByHead.Add(decl.RefHead, decl);
        return decl;
    }

    public Term RefOf(ContractDecl decl, params Term[] args) => this.Terms.Make(decl.RefHead, args);

    public Term SelfRef(ContractDecl decl) => this.RefOf(decl, Enumerable.Range(1, decl.Arity).Select(this.Terms.Var).ToArray());

    /// <summary>Computes ancestors (transitive, substituted) and the SPEC 8.3.3 recursive classification, then the templates.</summary>
    public void Prepare()
    {
        foreach (var decl in this.Decls)
        {
            decl.Ancestors.Clear();
            this.CollectAncestors(decl, decl.Parents, new Term?[decl.Arity + 1], decl.Ancestors, 0);
        }

        foreach (var decl in this.Decls)
        {
            decl.Recursive = this.IsRecursiveSafe(decl);
        }

        this.Templates.Clear();
        foreach (var input in this.Inputs)
        {
            foreach (var f in input.Supplied())
            {
                this.Add(new() { Kind = TemplateKind.Input, Conclusion = f, VarCount = 0, Source = Status.Valid, Origin = "input" });
            }
        }

        foreach (var decl in this.Decls)
        {
            foreach (var ancestor in decl.Ancestors)
            {
                this.Add(new() { Kind = TemplateKind.Ancestor, Antecedent = decl, Conclusion = new(FK.Is, this.Terms.Var(0), ancestor), VarCount = decl.Arity + 1, Source = decl.Status, Origin = decl.Name });
            }

            foreach (var clause in decl.Clauses)
            {
                foreach (var f in clause.Supplied())
                {
                    this.Add(new() { Kind = TemplateKind.Clause, Antecedent = decl, Conclusion = f, VarCount = decl.Arity + 1, Source = decl.Status, Origin = decl.Name });
                }
            }
        }

        foreach (var c in this.Conformances)
        {
            var target = this.DeclOf(c.Ref);
            var status = target is { Recursive: true } ? Status.Invalid : c.Status; // SPEC 8.3.3: never proven
            this.Add(new() { Kind = TemplateKind.Conformance, Conclusion = new(FK.Is, c.Type, c.Ref), VarCount = c.VarCount, Source = status, Conditions = c.Conditions, Origin = c.Type + " is " + c.Ref });
            foreach (var b in c.Bindings)
            {
                this.Bindings[b.Id] = b;
                var bs = status == Status.Valid ? b.Status : status;
                this.Add(new() { Kind = TemplateKind.Binding, Conclusion = new(FK.Eq, b.Projection, b.Value), VarCount = c.VarCount, Source = bs, Conditions = c.Conditions, BindingId = b.Id, Origin = $"binding {b.Id}" });
            }
        }
    }

    private void Add(Template t)
    {
        this.Templates.Add(new() { Id = this.Templates.Count, Kind = t.Kind, Antecedent = t.Antecedent, Conclusion = t.Conclusion, VarCount = t.VarCount, Source = t.Source, Conditions = t.Conditions, BindingId = t.BindingId, Origin = t.Origin });
    }

    private void CollectAncestors(ContractDecl root, List<Term> parents, Term?[] binding, List<Term> into, int depth)
    {
        if (depth > this.Decls.Count)
        {
            return; // refinement cycles are errors; the generator never makes one
        }

        foreach (var parent in parents)
        {
            var substituted = depth == 0 ? parent : this.Terms.Subst(parent, binding);
            if (into.Contains(substituted))
            {
                continue;
            }

            into.Add(substituted);
            if (this.DeclOf(substituted) is { } p)
            {
                var next = new Term?[p.Arity + 1];
                next[0] = this.Terms.Var(0);
                for (var i = 0; i < p.Arity; i++)
                {
                    next[i + 1] = substituted.Kids[i];
                }

                this.CollectAncestors(root, p.Parents, next, into, depth + 1);
            }
        }
    }

    // SPEC 8.3.3: C is recursive when T is C supplies, directly or through the Contracts so supplied, a conformance of a
    // projection of T to C's declaration or to a refinement of it. A declaration-level search: edges are the ancestor and clause
    // rows whose subject is Self or a projection of Self; at least one projection edge must be on the path.
    private bool IsRecursive(ContractDecl root)
    {
        var seen = new HashSet<(ContractDecl, bool)>();
        var stack = new Stack<(ContractDecl Decl, bool Projected)>();
        stack.Push((root, false));
        while (stack.Count != 0)
        {
            var (decl, projected) = stack.Pop();
            if (!seen.Add((decl, projected)))
            {
                continue;
            }

            foreach (var ancestor in decl.Ancestors)
            {
                if (this.DeclOf(ancestor) is { } a)
                {
                    Visit(a, projected);
                }
            }

            foreach (var clause in decl.Clauses)
            {
                foreach (var f in clause.Supplied())
                {
                    if (f.Kind == FK.Is && this.DeclOf(f.B!) is { } d && SelfRooted(f.A!) is { } isProjection)
                    {
                        Visit(d, projected || isProjection);
                    }
                }
            }
        }

        return false;

        void Visit(ContractDecl d, bool projected)
        {
            if (projected && (d == root || d.Ancestors.Any(x => this.DeclOf(x) == root)))
            {
                throw new RecursiveFound();
            }

            stack.Push((d, projected));
        }
    }

    private static bool? SelfRooted(Term t)
        => t.Head is VarHead { Index: 0 } ? false : t.Head is SymHead { Kind: SymKind.Proj } && SelfRooted(t.Kids[0]) is not null ? true : null;

    public bool IsRecursiveSafe(ContractDecl decl)
    {
        try
        {
            return this.IsRecursive(decl);
        }
        catch (RecursiveFound)
        {
            return true;
        }
    }

    public void Classify()
    {
        foreach (var decl in this.Decls)
        {
            decl.Recursive = this.IsRecursiveSafe(decl);
        }
    }

    private sealed class RecursiveFound : Exception
    {
    }
}

/// <summary>A small text form for hand-written models: `contract C<A> : P<A>, Q`, indented `clause S is R`,
/// `associate X is R`, `input S is R`, `conformance S1 is C<i32> [pending|failed]` with `binding X = i32 [inferred d] [pending]`.</summary>
public sealed class ModelText
{
    private readonly ProgramModel model;
    private ContractDecl? current;
    private Conformance? conformance;
    private int conformanceVars = -1;
    private readonly List<(ContractDecl Decl, string Line)> deferred = new();
    private int bindingIds;

    private ModelText(ProgramModel model) => this.model = model;

    /// <summary>Parses a proposition (`S is R`) against an existing model.</summary>
    public static Formula ParseFormula(ProgramModel model, string text) => new ModelText(model).Requirement(text, null);

    /// <summary>Parses a Type against an existing model.</summary>
    public static Term ParseType(ProgramModel model, string text) => new ModelText(model).Type(text, null);

    public static ProgramModel Parse(string text)
    {
        var parser = new ModelText(new ProgramModel());
        var lines = text.Replace("\r", string.Empty).Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("contract ", StringComparison.Ordinal))
            {
                var header = line.Trim()[9..];
                var name = header.Split([' ', '<', ':'], StringSplitOptions.RemoveEmptyEntries)[0];
                var arity = header.Contains('<') && header.IndexOf('<') < (header.IndexOf(':') < 0 ? int.MaxValue : header.IndexOf(':')) ? header[(header.IndexOf('<') + 1)..header.IndexOf('>')].Split(',').Length : 0;
                parser.model.AddContract(name, arity);
            }
        }

        foreach (var raw in lines)
        {
            parser.Line(raw.TrimEnd());
        }

        foreach (var (decl, line) in parser.deferred)
        {
            parser.current = decl;
            parser.Clause(line);
        }

        parser.model.Prepare();
        return parser.model;
    }

    private void Line(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        if (text.StartsWith("contract ", StringComparison.Ordinal))
        {
            var header = text[9..];
            var colon = header.IndexOf(':');
            var name = header.Split([' ', '<', ':'], StringSplitOptions.RemoveEmptyEntries)[0];
            this.current = this.model.Decls.Single(x => x.Name == name);
            this.conformance = null;
            if (header.EndsWith(" pending", StringComparison.Ordinal))
            {
                this.current.Status = Status.Pending;
                header = header[..^8];
            }

            if (colon >= 0)
            {
                foreach (var parent in SplitTop(header[(colon + 1)..], ','))
                {
                    this.current.Parents.Add(this.Type(parent.Trim(), this.current));
                }
            }

            return;
        }

        if (text.StartsWith("conformance ", StringComparison.Ordinal))
        {
            var body = text[12..];
            var status = Status.Valid;
            if (body.EndsWith(" pending", StringComparison.Ordinal))
            {
                status = Status.Pending;
                body = body[..^8];
            }
            else if (body.EndsWith(" failed", StringComparison.Ordinal))
            {
                status = Status.Invalid;
                body = body[..^7];
            }

            var when = body.IndexOf(" when ", StringComparison.Ordinal);
            var conditions = when < 0 ? string.Empty : body[(when + 6)..];
            body = when < 0 ? body : body[..when];
            var at = body.IndexOf(" is ", StringComparison.Ordinal);
            this.conformanceVars = 0;
            var type = this.Type(body[..at], null);
            var reference = this.Type(body[(at + 4)..], null);
            var formulas = conditions.Length == 0 ? [] : conditions.Split(',').Select(x => this.Requirement(x.Trim(), null)).ToList();
            this.conformance = new() { Type = type, Ref = reference, Status = status, VarCount = this.conformanceVars + 1 };
            this.conformance.Conditions.AddRange(formulas);
            this.conformanceVars = -1;
            this.model.Conformances.Add(this.conformance);
            this.current = null;
            return;
        }

        if (text.StartsWith("binding ", StringComparison.Ordinal) && this.conformance is { } c)
        {
            var parts = text[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = parts[0];
            this.conformanceVars = c.VarCount - 1;
            var value = this.Type(parts[2], null);
            this.conformanceVars = -1;
            var inferred = Array.IndexOf(parts, "inferred");
            var decl = this.model.DeclOf(c.Ref)!;
            c.Bindings.Add(new()
            {
                Id = this.bindingIds++,
                Name = name,
                Projection = this.model.Terms.Make(new SymHead(SymKind.Proj, name), c.Type, c.Ref),
                Value = value,
                Status = parts.Contains("pending") ? Status.Pending : Status.Valid,
                Inferred = inferred >= 0,
                Definition = inferred >= 0 ? int.Parse(parts[inferred + 1]) : -1,
            });
            return;
        }

        if (text.StartsWith("input ", StringComparison.Ordinal))
        {
            this.model.Inputs.Add(this.Requirement(text[6..], null));
            this.current = null;
            return;
        }

        if (this.current is not null && (text.StartsWith("clause ", StringComparison.Ordinal) || text.StartsWith("associate ", StringComparison.Ordinal)))
        {
            this.deferred.Add((this.current, text));
        }
    }

    private void Clause(string text)
    {
        var decl = this.current!;
        if (text.StartsWith("associate ", StringComparison.Ordinal))
        {
            var rest = text[10..];
            var at = rest.IndexOf(" is ", StringComparison.Ordinal);
            var name = at < 0 ? rest.Trim() : rest[..at].Trim();
            if (at >= 0)
            {
                var subject = this.model.Terms.Make(new SymHead(SymKind.Proj, name), this.model.Terms.Var(0), this.model.SelfRef(decl));
                decl.Clauses.Add(this.RequirementOf(subject, rest[(at + 4)..], decl));
            }

            return;
        }

        decl.Clauses.Add(this.Requirement(text[7..], decl));
    }

    private Formula Requirement(string text, ContractDecl? context)
    {
        var at = text.IndexOf(" is ", StringComparison.Ordinal);
        return this.RequirementOf(this.Type(text[..at].Trim(), context), text[(at + 4)..].Trim(), context);
    }

    private Formula RequirementOf(Term subject, string text, ContractDecl? context)
    {
        text = text.Trim();
        var parts = SplitTop(text, " or ");
        if (parts.Count > 1)
        {
            return parts.Skip(1).Aggregate(this.RequirementOf(subject, parts[0], context), (l, r) => new Formula(FK.Or, l: l, r: this.RequirementOf(subject, r, context)));
        }

        parts = SplitTop(text, " and ");
        if (parts.Count > 1)
        {
            return parts.Skip(1).Aggregate(this.RequirementOf(subject, parts[0], context), (l, r) => new Formula(FK.And, l: l, r: this.RequirementOf(subject, r, context)));
        }

        if (text.StartsWith("not ", StringComparison.Ordinal))
        {
            return new(FK.Not, l: this.RequirementOf(subject, text[4..], context));
        }

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            return this.RequirementOf(subject, text[1..^1], context);
        }

        var type = this.Type(text, context);
        return this.model.DeclOf(type) is not null ? new(FK.Is, subject, type) : new(FK.Eq, subject, type);
    }

    private Term Type(string text, ContractDecl? context)
    {
        text = text.Trim();
        var dot = LastTopDot(text);
        if (dot >= 0)
        {
            // base.(Ref).Name
            var name = text[(dot + 1)..];
            var refEnd = dot - 1;
            var refStart = MatchingOpen(text, refEnd);
            var reference = this.Type(text[(refStart + 1)..refEnd], context);
            var baseText = text[..(refStart - 1)];
            return this.model.Terms.Make(new SymHead(SymKind.Proj, name), this.Type(baseText, context), reference);
        }

        var lt = text.IndexOf('<');
        var head = lt < 0 ? text : text[..lt];
        var args = lt < 0 ? [] : SplitTop(text[(lt + 1)..^1], ',').Select(x => this.Type(x.Trim(), context)).ToArray();
        if (head == "Self")
        {
            return this.model.Terms.Var(0);
        }

        if (this.conformanceVars >= 0 && head is "V" or "W")
        {
            var index = head == "V" ? 1 : 2;
            this.conformanceVars = Math.Max(this.conformanceVars, index);
            return this.model.Terms.Var(index);
        }

        if (context is not null && context.Arity > 0 && head.Length == 1 && head[0] >= 'A' && head[0] <= 'E' && this.model.Decls.All(x => x.Name != head))
        {
            return this.model.Terms.Var(1 + (head[0] - 'A'));
        }

        if (this.model.Decls.FirstOrDefault(x => x.Name == head) is { } decl)
        {
            return this.model.RefOf(decl, args);
        }

        var kind = head is "i32" or "i64" or "bool" ? SymKind.Prim : args.Length == 0 && head.Length <= 2 && char.IsUpper(head[0]) && !head.StartsWith('S') ? SymKind.Param : SymKind.Con;
        return this.model.Terms.Make(new SymHead(kind, head), args);
    }

    private static int LastTopDot(string text)
    {
        var depth = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            depth += text[i] is ')' or '>' ? 1 : text[i] is '(' or '<' ? -1 : 0;
            if (depth == 0 && text[i] == '.' && i > 0 && text[i - 1] == ')')
            {
                return i;
            }
        }

        return -1;
    }

    private static int MatchingOpen(string text, int close)
    {
        var depth = 0;
        for (var i = close; i >= 0; i--)
        {
            depth += text[i] == ')' ? 1 : text[i] == '(' ? -1 : 0;
            if (depth == 0)
            {
                return i;
            }
        }

        throw new FormatException(text);
    }

    private static List<string> SplitTop(string text, string separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] is '(' or '<' ? 1 : text[i] is ')' or '>' ? -1 : 0;
            if (depth == 0 && string.CompareOrdinal(text, i, separator, 0, separator.Length) == 0)
            {
                parts.Add(text[start..i]);
                start = i + separator.Length;
                i += separator.Length - 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static List<string> SplitTop(string text, char separator) => SplitTop(text, separator.ToString());
}
