#:property Nullable=enable
// Executable model of A6's derived rules (A5 step 2 plan, unit 2x-P10, ER-derived; PD17) and A2/Binding on a Field Move out of drop's
// self (SPEC 16.3.1; PLAN G100). Record: A6DerivedPrototype.md. Run: dotnet run A6DerivedPrototype.cs in a folder outside the
// repository (-v prints every record per variant); the exit code is the number of HIDDEN results of the Recommended variant outside the
// listed cases (0 expected).
//
// Variants: Today (the code at the base: every Opaque root's subtree read-checked, nothing derived); R4-M10 (as written: every problem
// at an Opaque site dropped); Brief (the unit brief's rules: an Opaque write completes the written Place's Move Path only, an Opaque
// read is no Move, with the classes and H3's requirement match below); Recommended (Brief, and a Move-kind root, an @move node Binding
// resolved inside the site, is a Move); AnyCause (H3 reads Binding.FailureCauses: every problem at a site is derived, PD17's text);
// RootWrite (an Opaque write initializes the root's subtree, the EXIT audit's first wording); MoveSyntactic (the rejected @move node
// itself counts as a Move); NonLeafDerived and NonLeafAssumeInit (an Unknown root's problems derived, or its subtree assumed
// initialized); FlagIsGuess and FlagUnresolved (A3's flag as Binding.IsGuess flags a whole unresolved compound node today, or as a bare
// BindingState test); NoE4a (Recommended without unit 2x-E4a's Binding rule).
//
// What it models (each case is a body in a small structured language, the MIR shape the builder makes of it):
// - A1 (MirInit's rules, R4-M21): move paths "x", "x.a"; lanes MaybeUninit, MaybeMoved, EverInit joined by union. Def(p) checks every
//   strict ancestor constructed (SPEC 15.1.2) and a let path's single placement (15.1.1), then initializes p's subtree; Move(p) and
//   Use(p) need p's subtree initialized (15.1.1, 15.1.3). A constructor's self has its own bit set and its Fields uninitialized
//   (SPEC 6.3); its end uses the whole self (delivery).
// - An Opaque site (a node Binding rejected, built as one Opaque value, plan 5.4): the Binding record the site rests on (its rule) and
//   the site's roots with the access kind the builder would publish (G136, 2g-2a): ReadWhole r (r named as a value), ReadPlace p (a
//   resolved operand's Field Place p read), ReadPart r (r only the prefix of a Field path whose Place is not published), Write p and
//   Update p (an assignment's or update's resolved target), Move r (an explicit @move node Binding resolved), TypeName r (a name in a
//   Type expression), Unknown r (r named inside a site that contains statements: a failed Block, do, if or match; "non-leaf").
// - A3: a failure body's normal completion with a site whose completion Binding left undecided (Binding.IsGuess: an unresolved
//   expression, or a body the parser supplied) taken as diverging (direct) or completing (derived).
// - Publication (H3, DIAGNOSTICS 4.3): a Direct problem is published with its code; a problem at a site whose class is Repeat is
//   suppressed when the site's Binding record judges the same rule, else published direct; a problem whose class is Unknown (it
//   depends on what the rejected node does) is derived from the site's causes and suppressed, since every site rests on a published
//   Binding record.
// - SPEC verdict per case: the independent failed requirements (location, rule) that must be published (SPEC 23.1.2), Binding's
//   included. HIDDEN = such a failure published by neither Binding nor the MIR (unsound for SPEC 23.1.2); FALSE = a published MIR record
//   SPEC does not list (a derived or non-failure reported as an independent error: DIAGNOSTICS "Later phases").
// Rules (vocabulary of this model): init = a use of an uninitialized or Moved Place (15.1.1); complete = a whole use of an incomplete
// value or a part written in a never-constructed Place (15.1.2); let = a second placement of a let path (15.1.1); type, call, name =
// Binding's; take = a Move of a part of drop's self (16.3.1); helper = Binding's InvalidAssignment of `self@ref` in drop (G100);
// fall = a require failure body that completes normally (14.11).

using System.Text;

var verbose = args.Contains("-v");
var variants = new Variant[]
{
    new("Today", Write.None, Read.Whole, false, NonLeaf.Direct, Cause.None, false, Flag.None, false),
    new("R4-M10", Write.None, Read.Whole, false, NonLeaf.Direct, Cause.None, true, Flag.None, false),
    new("Brief", Write.Place, Read.Kinds, false, NonLeaf.Direct, Cause.Match, false, Flag.GuessLeaf, true),
    new("Recommended", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.GuessLeaf, true),
    new("AnyCause", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Any, false, Flag.GuessLeaf, true),
    new("RootWrite", Write.Root, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.GuessLeaf, true),
    new("MoveSyntactic", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.GuessLeaf, true, MoveSyntactic: true),
    new("NonLeafDerived", Write.Place, Read.Kinds, true, NonLeaf.Derived, Cause.Match, false, Flag.GuessLeaf, true),
    new("NonLeafAssumeInit", Write.Place, Read.Kinds, true, NonLeaf.AssumeInit, Cause.Match, false, Flag.GuessLeaf, true),
    new("FlagIsGuess", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.Guess, true),
    new("FlagUnresolved", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.Unresolved, true),
    new("NoE4a", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.GuessLeaf, false),
    new("PartDerived", Write.Place, Read.Kinds, true, NonLeaf.Direct, Cause.Match, false, Flag.GuessLeaf, true, PartDerived: true),
};

var cases = Cases.All();
var table = new StringBuilder();
table.Append("| Case | ").Append(string.Join(" | ", variants.Select(v => v.Name))).Append(" |\n|---|").Append(string.Concat(variants.Select(_ => "---|"))).Append('\n');
var unsound = new Dictionary<string, int>();
var unlisted = new Dictionary<string, int>();
var falses = new Dictionary<string, int>();
foreach (var c in cases)
{
    table.Append("| ").Append(c.Id).Append(c.Listed is null ? string.Empty : " (listed: " + c.Listed + ")").Append(' ');
    foreach (var v in variants)
    {
        var (hidden, wrong, records) = Evaluate(c, v);
        unsound[v.Name] = unsound.GetValueOrDefault(v.Name) + hidden.Count;
        unlisted[v.Name] = unlisted.GetValueOrDefault(v.Name) + (c.Listed is null ? hidden.Count : 0);
        falses[v.Name] = falses.GetValueOrDefault(v.Name) + wrong.Count;
        var cell = hidden.Count + wrong.Count == 0 ? "ok" : string.Join("; ", hidden.Select(h => "HIDDEN " + h).Concat(wrong.Select(w => "FALSE " + w)));
        table.Append("| ").Append(cell).Append(' ');
        if (verbose)
        {
            Console.WriteLine($"{c.Id} [{v.Name}]: {string.Join(", ", records)}");
        }
    }

    table.Append("|\n");
}

Console.Write(table);
Console.WriteLine();
Console.WriteLine("| Variant | HIDDEN (unsound, SPEC 23.1.2) | of them in cases not listed | FALSE |\n|---|---|---|---|");
foreach (var v in variants)
{
    Console.WriteLine($"| {v.Name} | {unsound[v.Name]} | {unlisted[v.Name]} | {falses[v.Name]} |");
}

return unlisted["Recommended"];

// Runs a case under a variant: the published records, the expected failures neither side published, and the published MIR records
// SPEC does not list.
static (List<string> Hidden, List<string> Wrong, List<string> Records) Evaluate(Case c, Variant v)
{
    var a1 = new A1(c, v);
    a1.Run();
    var published = new List<(string At, string Rule, string Code)>();
    foreach (var p in a1.Problems)
    {
        var site = p.Site is null ? null : c.Sites.First(s => s.Name == p.Site);
        var suppressed = p.Class switch
        {
            _ when v.DropAtSite && p.Site is not null => true,
            Class.Direct => false,
            Class.Repeat => v.Cause == Cause.Any || (v.Cause == Cause.Match && site!.Rule == p.Rule),
            Class.Unknown => v.Cause != Cause.None,
            _ => false,
        };
        if (!suppressed && !published.Contains((p.At, p.Rule, p.Code)))
        {
            published.Add((p.At, p.Rule, p.Code));
        }
    }

    foreach (var (at, kind) in c.Falls)
    {
        // A3 (MirLints with the derived rule): a failure body that completes normally is direct when it does with every undecided site
        // diverging, derived when only the sites' completion makes it complete.
        var direct = Completes(kind, guessDiverges: true, v);
        var any = Completes(kind, guessDiverges: false, v);
        if (direct || (any && v.Cause == Cause.None))
        {
            published.Add((at, "fall", "RequireFallthrough_Kd"));
        }
    }

    // init and complete are one family (A1's 15.1 state rules, told apart only by the message); a published MIR record of the family of a
    // Binding record at its location repeats Binding's failure.
    var binding = c.Binding.Concat(v.E4a ? c.TakeMoves.Select(m => (At: m, Rule: "take")) : []).ToList();
    var hidden = c.Expected.Where(e => !binding.Any(b => Same(b, e)) && !published.Any(p => Same((p.At, p.Rule), e))).Select(e => $"{e.Rule}@{e.At}").ToList();
    var wrong = published.Where(p => !c.Expected.Any(e => Same((p.At, p.Rule), e)) || binding.Any(b => Same(b, (p.At, p.Rule))))
        .Select(p => $"{p.Code}({p.Rule})@{p.At}").ToList();
    return (hidden, wrong, binding.Select(b => $"B:{b.Rule}@{b.At}").Concat(published.Select(p => $"{p.Code}@{p.At}")).ToList());
}

// Whether a failure body completes normally: "unit" completes; "guess" is an undecided site (a parser-supplied body, or an unresolved
// expression); "recovered" an unresolved recovery form that completes (a chained comparison, DIAGNOSTICS Later phases); "never" and
// "return" do not complete; "seq:a,b" completes when every item does; "if:a" (no else) always completes; "ifelse:a|b" when either
// branch does; "loop:a" never completes.
static bool Same((string At, string Rule) a, (string At, string Rule) b) => a.At == b.At && Family(a.Rule) == Family(b.Rule);

static string Family(string rule) => rule is "init" or "complete" ? "state" : rule;

static bool Completes(string kind, bool guessDiverges, Variant v)
{
    if (kind.StartsWith("seq:", StringComparison.Ordinal))
    {
        return Split(kind[4..], ',').All(k => Completes(k, guessDiverges, v));
    }

    if (kind.StartsWith("ifelse:", StringComparison.Ordinal))
    {
        return Split(kind[7..], '|').Any(k => Completes(k, guessDiverges, v));
    }

    return kind switch
    {
        "unit" => true,
        "never" or "return" => false,
        _ when kind.StartsWith("if:", StringComparison.Ordinal) => true,
        _ when kind.StartsWith("loop:", StringComparison.Ordinal) => false,
        "guess" => !(guessDiverges && v.Flag != Flag.None),
        "supplied" => !(guessDiverges && v.Flag is Flag.Guess or Flag.GuessLeaf), // A parser-supplied body: only IsGuess's first clause flags it.
        "recovered" => !(guessDiverges && v.Flag == Flag.Unresolved), // A recovery form: IsGuess does not flag it; a bare state test does.
        // An unresolved compound node (a match or if Binding left unresolved because a part failed): IsGuess as implemented flags the whole
        // node (StructuralCompletion.Compose tests the guess first); the leaf rule descends into its written parts.
        _ when kind.StartsWith("node:", StringComparison.Ordinal) => guessDiverges && v.Flag is Flag.Guess or Flag.Unresolved ? false : Completes(kind[5..], guessDiverges, v),
        _ => throw new InvalidOperationException(kind),
    };
}

// Splits at top-level separators only (nested seq/if items are bracketed in [ ]).
static IEnumerable<string> Split(string text, char separator)
{
    var depth = 0;
    var start = 0;
    for (var i = 0; i < text.Length; i++)
    {
        depth += text[i] == '[' ? 1 : text[i] == ']' ? -1 : 0;
        if (depth == 0 && text[i] == separator)
        {
            yield return Trim(text[start..i]);
            start = i + 1;
        }
    }

    yield return Trim(text[start..]);

    static string Trim(string item) => item.StartsWith('[') && item.EndsWith(']') ? item[1..^1] : item;
}

enum Write { None, Place, Root }

enum Read { Whole, Kinds }

enum NonLeaf { Direct, Derived, AssumeInit }

enum Cause { None, Match, Any }

enum Flag { None, Guess, GuessLeaf, Unresolved }

enum Class { Direct, Repeat, Unknown }

enum K { ReadWhole, ReadPlace, ReadPart, Write, Update, Move, TypeName, Unknown }

/// <summary>A rule variant. Write: how an Opaque write acts (None: today, the root is read-checked; Place: the written Place's Move
/// Path is defined; Root: the root's subtree, the EXIT audit's first wording). Read: Whole (today: every root's subtree checked, problems
/// direct) or Kinds (the model's per-kind rules). MoveKind: a Move-kind root is moved (else an Opaque read is no Move). NonLeaf: how
/// an Unknown root acts. Cause: H3's reading (None: everything direct; Match: requirement-matched; Any: Binding.FailureCauses of the
/// site, PD17's text). DropAtSite: R4-M10 as written. Flag: A3's undecided flag (None: today; Guess: Binding.IsGuess; Unresolved: a bare
/// BindingState test). E4a: Binding rejects a Move of a part of drop's self (unit 2x-E4a).</summary>
record Variant(string Name, Write Write, Read Read, bool MoveKind, NonLeaf NonLeaf, Cause Cause, bool DropAtSite, Flag Flag, bool E4a, bool MoveSyntactic = false,
    bool PartDerived = false);

/// <summary>An Opaque site: its label, the rule of the Binding record it rests on, and its roots' kinds.</summary>
record Site(string Name, string Rule, (K Kind, string Place)[] Roots);

/// <summary>A case: its body (statements), sites, Binding's records, the SPEC verdict (the independent failures that must be published),
/// the require failure bodies (A3), and the Moves of parts of drop's self that unit 2x-E4a's Binding rule rejects (each a location).</summary>
record Case(string Id, string[] Body, Site[] Sites, (string At, string Rule)[] Binding, (string At, string Rule)[] Expected,
    (string At, string Kind)[] Falls, string[] TakeMoves)
{
    /// <summary>Gets why a case's remaining result is a listed limit of every variant, or null.</summary>
    internal string? Listed { get; init; }
}

record Problem(string Code, string At, string Rule, Class Class, string? Site);

/// <summary>A1 over a structured body. Statements: "var x{a,b}" / "let x{a,b}" (uninitialized local with Fields), "let= x{...}"
/// (initialized let), "arg x{...}" (initialized parameter), "ctor self{a,b}" (constructor receiver), "def p @L", "move p @L",
/// "use p @L", "op S @L" (an Opaque site), "if" / "else" / "fi" (a branch, joined by union), "end @L" (a constructor's end: the whole
/// self used), "dropend f,g @L" (drop's field cleanup: A2's verdict per Field; a Field marked * has a Type that declares a drop, whose
/// incomplete layer is MovedPlace_Kd at its Drop, R4-M6b). In a drop body the receiver is "self"; under 2x-E4a a "move self.f" is the
/// Binding-rejected node, built as an Opaque read of self (ReadPart, no Move).</summary>
sealed class A1(Case c, Variant v)
{
    private readonly Dictionary<string, (bool Uninit, bool Moved, bool Ever)> state = new();
    private readonly HashSet<string> once = new();
    private readonly List<string> paths = [];

    internal List<Problem> Problems { get; } = [];

    internal void Run()
    {
        var position = 0;
        this.Block(ref position, stop: false);
    }

    private static string Root(string p) => p.Split('.')[0];

    private static bool Within(string p, string ancestor) => p == ancestor || p.StartsWith(ancestor + ".", StringComparison.Ordinal);

    private IEnumerable<string> Subtree(string p) => this.paths.Where(x => Within(x, p));

    private IEnumerable<string> Ancestors(string p)
    {
        var parts = p.Split('.');
        for (var i = 1; i < parts.Length; i++)
        {
            yield return string.Join('.', parts[..i]);
        }
    }

    // Runs statements until "else"/"fi" (stop) or the end; returns the stopping word.
    private string Block(ref int position, bool stop)
    {
        while (position < c.Body.Length)
        {
            var line = c.Body[position++];
            var word = line.Split(' ')[0];
            if (word is "else" or "fi")
            {
                return word;
            }

            if (word == "if")
            {
                var before = new Dictionary<string, (bool, bool, bool)>(this.state);
                var ended = this.Block(ref position, stop: true);
                var then = new Dictionary<string, (bool Uninit, bool Moved, bool Ever)>(this.state);
                this.Restore(before);
                if (ended == "else")
                {
                    this.Block(ref position, stop: true);
                }

                foreach (var key in this.state.Keys.ToList())
                {
                    var (u, m, e) = this.state[key];
                    var (tu, tm, te) = then[key];
                    this.state[key] = (u || tu, m || tm, e || te);
                }

                continue;
            }

            this.Statement(line);
        }

        return "end";
    }

    private void Restore(Dictionary<string, (bool, bool, bool)> saved)
    {
        foreach (var (key, value) in saved)
        {
            this.state[key] = value;
        }
    }

    private void Statement(string line)
    {
        var words = line.Split(' ');
        var at = words.FirstOrDefault(w => w.StartsWith('@'))?[1..] ?? "?";
        switch (words[0])
        {
            case "var" or "let" or "let=" or "arg" or "ctor":
                this.Declare(words[0], words[1]);
                break;
            case "def":
                this.Def(words[1], at, null, Class.Direct);
                break;
            case "move" when v.E4a && Root(words[1]) == "self" && words[1].Contains('.') && c.TakeMoves.Contains(at):
                // The rejected Move is an Opaque site reading self: no Move, unless the kind is taken from the syntax's @move alone.
                this.Use(Root(words[1]), at, null, Class.Direct, own: true);
                if (v.MoveSyntactic)
                {
                    this.Set(words[1], uninit: true, moved: true);
                }

                break;
            case "move":
                this.Use(words[1], at, null, Class.Direct);
                this.Set(words[1], uninit: true, moved: true);
                break;
            case "use":
                this.Use(words[1], at, null, Class.Direct);
                break;
            case "op":
                this.Opaque(c.Sites.First(s => s.Name == words[1]), at);
                break;
            case "end":
                this.Use("self", at, null, Class.Direct);
                break;
            case "dropend":
                this.DropEnd(words[1].Split(','), at);
                break;
            default:
                throw new InvalidOperationException(line);
        }
    }

    private void Declare(string kind, string text)
    {
        var name = text.Split('{')[0];
        var fields = text.Contains('{') ? text[(text.IndexOf('{') + 1)..^1].Split(',').Select(f => f.TrimEnd('*')).ToArray() : [];
        var all = new List<string> { name };
        foreach (var f in fields)
        {
            all.Add(name + "." + f);
        }

        foreach (var p in all)
        {
            this.paths.Add(p);
            var initialized = kind is "let=" or "arg" || (kind == "ctor" && p == name);
            this.state[p] = (!initialized, false, kind is "let=" or "arg");
            if (kind is "let" or "let=")
            {
                this.once.Add(p);
            }
        }
    }

    private void Set(string p, bool uninit, bool moved)
    {
        foreach (var x in this.Subtree(p).ToList())
        {
            var (_, _, ever) = this.state[x];
            this.state[x] = (uninit, moved, ever || !uninit);
        }
    }

    // A definition (MirInit.Define): strict ancestors constructed, a let path placed once, then the subtree initialized.
    private void Def(string p, string at, string? site, Class cls)
    {
        foreach (var a in this.Ancestors(p))
        {
            if (this.state[a].Uninit)
            {
                this.Add(this.state[a].Moved ? "MovedPlace_Kd" : "UninitializedPlace_Kd", at, "complete", cls, site);
                break;
            }
        }

        if (this.once.Contains(p) && this.Subtree(p).Any(x => this.state[x].Ever))
        {
            this.Add("ReassignedLet_Kd", at, "let", cls, site);
        }

        this.Set(p, uninit: false, moved: false);
    }

    // A use (MirInit.Check): the subtree initialized; own = only the path's own bit decides, a missing part being judged by cls' partial.
    private void Use(string p, string at, string? site, Class cls, bool own = false, Class partial = Class.Direct)
    {
        var (u, m, _) = this.state[p];
        if (u)
        {
            this.Add(m ? "MovedPlace_Kd" : "UninitializedPlace_Kd", at, "init", cls, site);
            return;
        }

        var missing = this.Subtree(p).Where(x => x != p && this.state[x].Uninit).ToList();
        if (missing.Count > 0 && !own)
        {
            this.Add(missing.Any(x => this.state[x].Moved) ? "MovedPlace_Kd" : "UninitializedPlace_Kd", at, "complete", cls, site);
        }
        else if (missing.Count > 0 && partial != Class.Direct)
        {
            this.Add(missing.Any(x => this.state[x].Moved) ? "MovedPlace_Kd" : "UninitializedPlace_Kd", at, "complete", partial, site);
        }
    }

    // An Opaque site under the variant.
    private void Opaque(Site site, string at)
    {
        if (v.Read == Read.Whole)
        {
            foreach (var root in site.Roots.Select(r => Root(r.Place)).Distinct())
            {
                this.Use(root, at, site.Name, Class.Direct); // Today: every root's subtree read, no state change.
            }

            return;
        }

        foreach (var (kind, place) in site.Roots)
        {
            switch (kind)
            {
                case K.ReadWhole:
                    this.Use(place, at, site.Name, Class.Repeat);
                    break;
                case K.ReadPlace:
                    this.Use(place, at, site.Name, Class.Repeat);
                    break;
                case K.ReadPart:
                    this.Use(place, at, site.Name, Class.Repeat, own: !v.PartDerived ? false : true, partial: Class.Unknown);
                    break;
                case K.Write:
                    this.Def(v.Write == Write.Root ? Root(place) : place, at, site.Name, Class.Repeat);
                    break;
                case K.Update:
                    this.Use(place, at, site.Name, Class.Repeat);
                    this.Def(place, at, site.Name, Class.Repeat);
                    break;
                case K.Move:
                    this.Use(place, at, site.Name, Class.Repeat);
                    if (v.MoveKind)
                    {
                        this.Set(place, uninit: true, moved: true);
                    }

                    break;
                case K.TypeName:
                    break;
                case K.Unknown when v.NonLeaf == NonLeaf.AssumeInit:
                    this.Set(place, uninit: false, moved: false);
                    break;
                case K.Unknown:
                    this.Use(place, at, site.Name, v.NonLeaf == NonLeaf.Derived ? Class.Unknown : Class.Direct);
                    break;
            }
        }
    }

    // A2 at drop's field cleanup (R4-M6b): a Field whose Type declares a drop and that may be incomplete is MovedPlace_Kd at its Drop.
    private void DropEnd(string[] fields, string at)
    {
        foreach (var f in fields.Where(f => f.EndsWith('*')).Select(f => "self." + f.TrimEnd('*')))
        {
            var open = !this.state[f].Uninit && this.Subtree(f).Any(x => x != f && this.state[x].Uninit);
            if (open)
            {
                this.Add("MovedPlace_Kd", at, "complete", Class.Direct, null);
            }
        }
    }

    private void Add(string code, string at, string rule, Class cls, string? site)
    {
        if (!this.Problems.Any(p => p.Code == code && p.At == at))
        {
            this.Problems.Add(new(code, at, rule, cls, site));
        }
    }
}

static class Cases
{
    internal static List<Case> All() =>
    [
        // A1 at Opaque sites (probe cases o*, corpus shapes named by their ledger source).
        C("o01 rejected `x = \"text\"`; `return x`", ["var x", "op S @asg", "use x @ret"], [S("S", "type", (K.Write, "x"))], [("asg", "type")], []),
        C("o02 ctor: `self.a = a; self.b = b` (b rejected)", ["ctor self{a,b}", "def self.a @a", "op S @b", "end @end"], [S("S", "type", (K.Write, "self.b"))], [("b", "type")], []),
        C("o03 ctor: `self.a = a` rejected, b never set", ["ctor self{a,b}", "op S @a", "end @end"], [S("S", "type", (K.Write, "self.a"))], [("a", "type"), ("end", "complete")], []),
        C("o04b `x.b` moved; rejected `x.a = \"s\"`; `look(x.b)`", ["arg p", "var x{a,b}", "def x @init", "move x.b @mv", "op S @asg", "use x.b @look"], [S("S", "type", (K.Write, "x.a"))], [("asg", "type"), ("look", "init")], []),
        C("o05b `var x: Pair`; rejected `x.a = \"s\"`", ["var x{a,b}", "op S @asg"], [S("S", "type", (K.Write, "x.a"))], [("asg", "type"), ("asg", "complete")], []),
        C("o06 `var x`; rejected `let s: string = x`", ["var x", "op S @let"], [S("S", "type", (K.ReadWhole, "x"))], [("let", "type"), ("let", "init")], []),
        C("o07 x.b moved; rejected `let s: string = x.a`", ["arg p", "var x{a,b}", "def x @init", "move x.b @mv", "op S @let"], [S("S", "type", (K.ReadPlace, "x.a"))], [("let", "type")], []),
        C("o07p o07, the read Place not published", ["arg p", "var x{a,b}", "def x @init", "move x.b @mv", "op S @let"], [S("S", "type", (K.ReadPart, "x"))], [("let", "type")], []) with { Listed = "read Place not published" },
        C("o21 ctor: rejected `self.a = self.b + \"s\"` before `self.b = a`", ["ctor self{a,b}", "op S @val", "def self.a @val", "def self.b @b", "end @end"], [S("S", "type", (K.ReadPlace, "self.b"))], [("val", "type"), ("val", "init")], []),
        C("o21p o21, the read Place not published", ["ctor self{a,b}", "op S @val", "def self.a @val", "def self.b @b", "end @end"], [S("S", "type", (K.ReadPart, "self"))], [("val", "type"), ("val", "init")], []),
        C("o08 rejected `two(x@move)` (arity); `look(x)`", ["var x", "def x @init", "op S @call", "use x @look"], [S("S", "call", (K.Move, "x"))], [("call", "call"), ("look", "init")], []),
        C("o09 `let x; x = 1`; rejected `x = \"s\"`", ["let x", "def x @one", "op S @asg"], [S("S", "type", (K.Write, "x"))], [("asg", "type"), ("asg", "let")], []),
        C("o10 `let x = 1`; rejected `x = \"s\"`", ["let= x", "op S @asg"], [S("S", "type", (K.Write, "x"))], [("asg", "type"), ("asg", "let")], []),
        C("o11 `a` moved; rejected `a.1 += \"x\"`", ["var a{0,1}", "def a @init", "move a @mv", "op S @upd"], [S("S", "type", (K.Update, "a.1"))], [("upd", "type"), ("upd", "init")], []),
        C("o13 `x = unknown()` (value rejected, assignment kept)", ["var x", "def x @asg", "use x @ret"], [], [], []),
        C("o14 `if c => x = \"s\"` rejected; `return x`", ["var x", "if", "op S @asg", "fi", "use x @ret"], [S("S", "type", (K.Write, "x"))], [("asg", "type"), ("ret", "init")], []),
        C("o15 `if c => x = 1 else => x = \"t\"` rejected", ["var x", "if", "def x @one", "else", "op S @asg", "fi", "use x @ret"], [S("S", "type", (K.Write, "x"))], [("asg", "type")], []),
        C("o16 ctor: rejected `(self.a, self.b) = (a, b)`", ["ctor self{a,b}", "op S @asg", "end @end"], [S("S", "complete", (K.Write, "self.a"), (K.Write, "self.b"))], [("asg", "complete")], []),
        C("o17b x.b moved; rejected `x = \"s\"`; `look(x.b)`", ["arg p", "var x{a,b}", "def x @init", "move x.b @mv", "op S @asg", "use x.b @look"], [S("S", "type", (K.Write, "x"))], [("asg", "type")], []),
        C("o18 `let a` moved; rejected `a = (\"new\", \"x\")`", ["let= a{0,1}", "move a @mv", "op S @asg"], [S("S", "type", (K.Write, "a"))], [("asg", "type"), ("asg", "let")], []),
        C("o19 rejected `y = do: x = 1; see(x); \"s\"`; `x + y`", ["var x", "var y", "op S @do", "use x @sum", "use y @sum"], [S("S", "type", (K.Unknown, "x"), (K.Write, "y"))], [("do", "type")], []) with { Listed = "non-leaf, E4 beyond Q21" },
        C("o24 rejected `do: if c => x = 1; \"s\"`; `return x`", ["var x", "op S @do", "use x @ret"], [S("S", "type", (K.Unknown, "x"))], [("do", "type"), ("ret", "init")], []) with { Listed = "non-leaf, E4 beyond Q21" },
        C("o25 rejected `do: see(x); \"s\"` with x uninitialized", ["var x", "op S @do"], [S("S", "type", (K.Unknown, "x"))], [("do", "type"), ("do", "init")], []) with { Listed = "non-leaf, E4 beyond Q21" },
        C("o26 rejected `do: consume(x@move); \"s\"`; `look(x)`", ["var x", "def x @init", "op S @do", "use x @look"], [S("S", "type", (K.Unknown, "x"))], [("do", "type"), ("look", "init")], []) with { Listed = "non-leaf, E4 beyond Q21" },
        C("o27 rejected `two(x@ref)` (arity), x uninitialized", ["var x", "op S @call"], [S("S", "call", (K.ReadWhole, "x"))], [("call", "call"), ("call", "init")], []),
        C("o28 `let a = (\"first\", 0)`; `a.1 = 2` (InvalidAssignment)", ["let= a{0,1}", "op S @asg"], [S("S", "let", (K.Write, "a.1"))], [("asg", "let")], []),
        C("o30 rejected `y = do: see(x); x = 1; \"s\"`; `x + y`", ["var x", "var y", "op S @do", "use x @sum"], [S("S", "type", (K.Unknown, "x"), (K.Write, "y"))], [("do", "type"), ("do", "init")], []) with { Listed = "non-leaf, E4 beyond Q21" },
        C("bdfa param `a.0` moved; `a.0 = \"new\"` (InvalidAssignment)", ["arg a{0,1}", "move a.0 @mv", "op S @asg"], [S("S", "let", (K.Write, "a.0"))], [("asg", "let")], []),
        C("72a6 ctor: `let escaped = self` before init (InvalidAssignment)", ["ctor self{value}", "op S @let", "def self.value @v", "end @end"], [S("S", "complete", (K.ReadWhole, "self"))], [("let", "complete")], []),
        C("52ea ctor: `self.a = self.b` (getter on incomplete self)", ["ctor self{a}", "op S @get", "def self.a @get", "end @end"], [S("S", "complete", (K.ReadWhole, "self"))], [("get", "complete")], []),
        C("4ce1 `let N: i32; let row: [N of u8]`", ["let N", "op S @row"], [S("S", "type", (K.TypeName, "N"))], [("row", "type")], []),
        C("b45a `use(port)` unresolved; port maybe uninitialized", ["var port", "if", "def port @bind", "fi", "op S @use"], [S("S", "name", (K.ReadWhole, "port"))], [("use", "name"), ("use", "init")], []),
        C("f1a0 ctor: `value = 1` (QualificationRequired)", ["ctor self{value}", "op S @asg", "end @end"], [S("S", "name")], [("asg", "name")], []) with { Listed = "unresolved target" },

        // A3: require failure bodies (probe cases r*, corpus shapes).
        F("r01 `require c else => logFailure()` (unresolved)", [("body", "guess")], [("call", "name")], []),
        F("r02 `else => if d => logFailure()`", [("body", "if:guess")], [("call", "name")], [("body", "fall")]),
        F("r03 `else => log(1)` (one candidate, Unit; no candidate applies)", [("body", "guess")], [("call", "call")], []),
        F("r04 `else => stop(1)` (stop: -> Never; no candidate applies)", [("body", "guess")], [("call", "call")], []),
        F("r06 `require unknownCond() else => ()`", [("body", "unit")], [("cond", "name")], [("body", "fall")]),
        F("r07 `require a else => require b else => logFailure()`", [("outer", "unit"), ("inner", "guess")], [("call", "name")], [("outer", "fall")]),
        F("r08 `else: logFailure(); logMore()`", [("body", "seq:guess,guess")], [("call", "name")], []),
        F("r09 `else: logFailure(); ()`", [("body", "seq:guess,unit")], [("call", "name")], []),
        F("r10 `else => loop => logFailure()`", [("body", "loop:guess")], [("call", "name")], []),
        F("r11 `else => log(\"x\")` (valid program)", [("body", "unit")], [], [("body", "fall")]),
        F("r12 `else: logFailure(); return`", [("body", "seq:guess,return")], [("call", "name")], []),
        F("r13 `else => if unknownCond() => ()` (the condition may be Never)", [("body", "seq:guess,[if:unit]")], [("cond", "name")], []),
        F("r15 `else => match d: true => logFailure(); false => ()`", [("body", "node:ifelse:guess|unit")], [("call", "name")], [("body", "fall")]),
        F("42d4 `require a == 1 else =>` (body supplied by the parser)", [("body", "supplied")], [("syntax", "syntax")], []),
        F("b9d8 `else => 0 < a < 2` (recovered chained comparison)", [("body", "recovered")], [("syntax", "syntax")], [("body", "fall")]),

        // Binding (2x-E4a) and A2 on a Field Move out of drop's self (probe cases d*; SPEC 16.3.1's example).
        D("d01 `let moved = self.buffer@move`", ["arg self{buffer}", "move self.buffer @mv", "dropend buffer @drop"], ["mv"], []),
        D("d02 Move, then `self.buffer = Buffer.init()`", ["arg self{buffer}", "move self.buffer @mv", "def self.buffer @restore", "dropend buffer @drop"], ["mv"], []),
        D("d03 `self.count@move` (a Copy Field)", ["arg self{count}", "move self.count @mv"], ["mv"], []),
        D("d04 `self.inner.x@move`", ["arg self{inner,inner.x}", "move self.inner.x @mv", "dropend inner @drop"], ["mv"], []),
        D("d05 `exchange(self.buffer@uniq, with: ...)` (valid)", ["arg self{buffer}", "def self.buffer @exchange", "dropend buffer @drop"], [], []),
        D("d11 `if self.on => let moved = self.buffer@move`", ["arg self{on,buffer}", "if", "move self.buffer @mv", "fi", "dropend buffer @drop"], ["mv"], []),
        D("d12 Move, then `observe(self.buffer@ref)`", ["arg self{buffer}", "move self.buffer @mv", "use self.buffer @observe"], ["mv"], []),
        D("d13 `self.box.value@move` (Box declares a drop)", ["arg self{box,box.value}", "move self.box.value @mv", "dropend box* @drop"], ["mv"], []),
        D("d17 two Moves of `self.value` (Box<T>)", ["arg self{value}", "move self.value @mv1", "move self.value @mv2"], ["mv1", "mv2"], []),
        D("16.3.1 Move variant (helper borrow `self@ref` rejected, G100)", ["arg self{buffer}", "move self.buffer @mv", "op S @whole", "use self.buffer @observe", "use self.buffer @exchange"],
            ["mv"], [("whole", "helper")], [S("S", "helper", (K.ReadWhole, "self"))]),
    ];

    private static Site S(string name, string rule, params (K, string)[] roots) => new(name, rule, roots);

    private static Case C(string id, string[] body, Site[] sites, (string, string)[] expected, (string, string)[] falls)
        => new(id, body, sites, sites.Select(s => (Loc(body, s.Name), s.Rule)).ToArray(), expected, falls, []);

    private static Case F(string id, (string, string)[] falls, (string, string)[] binding, (string, string)[] independent)
        => new(id, [], [], binding, binding.Concat(independent).ToArray(), falls, []);

    private static Case D(string id, string[] body, string[] takes, (string, string)[] binding, Site[]? sites = null)
        => new(id, body, sites ?? [], binding, takes.Select(t => (t, "take")).Concat(binding).ToArray(), [], takes);

    // The location of the statement that runs a site.
    private static string Loc(string[] body, string site) => body.First(l => l.StartsWith("op " + site + " ", StringComparison.Ordinal)).Split('@')[1];
}
