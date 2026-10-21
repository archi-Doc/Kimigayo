// G170 C1 prototype runner: fixed cases (PlanB §3's B/D counterexamples and §4.3's forms), agreement of C with the exhaustive
// bounded reference on generated finite models, order invariance, the Resource boundary and cost workloads.
// Run: dotnet run -c Release -- <fixed|random|order|resource|cost|all> [count] [seed]
#pragma warning disable
#nullable enable
using System.Diagnostics;
using System.Text;

namespace G170C1;

public static class Runner
{
    private static readonly object LogLock = new();

    public static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "all";
        var count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 2000;
        var seed = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 20261021;
        var failures = 0;
        if (mode == "trace")
        {
            var m = ModelText.Parse(File.ReadAllText(args[1]));
            var solver = new Solver(m) { Trace = Console.WriteLine };
            var inadmissible = args.Length > 4 ? args[4].Split(',').Select(int.Parse) : [];
            var a = solver.Supply(ModelText.ParseFormula(m, args[2]), args.Length > 3 && args[3] == "reads" ? new View("reads", inadmissible, wantsReads: true) : View.Ordinary, args.Length > 3 && args[3] == "limits" ? new Limits { Rounds = 300, Size = 30_000, Work = 3_000_000 } : new Limits { Rounds = args.Length > 3 ? 300 : 12 });
            Console.WriteLine(a + " reads " + string.Join(" ", a.PositiveReads.Select(x => "{" + string.Join(",", x) + "}")));
            return 0;
        }

        if (mode == "refdump")
        {
            var m = ModelText.Parse(File.ReadAllText(args[1]));
            var subject = ModelText.ParseType(m, args[2]);
            var r = new Reference(m, View.Ordinary, false, 5);
            r.Run([subject]);
            foreach (var (pos, decl, reference) in r.FactsOn(subject))
            {
                Console.WriteLine((pos ? "+" : "-") + reference);
            }

            foreach (var t in m.Terms.All.Where(t => !t.HasVar && r.Same(t, subject)))
            {
                Console.WriteLine("member " + t);
            }

            return 0;
        }

        if (mode == "time")
        {
            var m = ModelText.Parse(File.ReadAllText(args[1]));
            foreach (var q in args.Skip(2))
            {
                var f = ModelText.ParseFormula(m, q);
                var sw = Stopwatch.StartNew();
                var a = new Solver(m).Supply(f, View.Ordinary, new Limits { Rounds = 300, Size = 30_000, Work = 3_000_000 });
                var tc = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var r = Ref(m, View.Ordinary, f, 5);
                var t5 = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var r7 = Ref(m, View.Ordinary, f, 7);
                var t7 = sw.Elapsed.TotalMilliseconds;
                Console.WriteLine($"{q}: C {a} established-at-stop {a.EstablishedAtStop} {tc:F0} ms; ref5 {Show(r)} {t5:F0} ms; ref7 {Show(r7)} {t7:F0} ms");
            }

            return 0;
        }

        if (mode is "fixed" or "all")
        {
            failures += Fixed();
        }

        if (mode is "random" or "all")
        {
            failures += RandomAgreement(count, seed);
        }

        if (mode is "order" or "all")
        {
            failures += Order(Math.Min(count, 400), seed + 1);
        }

        if (mode is "resource" or "all")
        {
            failures += ResourceBoundary();
        }

        if (mode is "cost" or "all")
        {
            Cost();
        }

        Console.WriteLine($"FAILURES {failures}");
        return failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- fixed cases
    private const string Trees = """
        contract Copy
        contract Tree
          associate Child is Tree
        contract CopyTree : Tree, Copy
          clause Self.(Tree).Child is CopyTree
        """;

    public static string Chain(int depth, string qualifier = "Tree") => "T" + string.Concat(Enumerable.Repeat(".(" + qualifier + ").Child", depth));

    private static int Fixed()
    {
        var failures = 0;
        Console.WriteLine("== FIXED (expected: + evidence, - negated evidence, none; C complete, P provisional, R resource)");
        foreach (var d in new[] { 1, 2, 4, 8 })
        {
            failures += Expect($"G170 depth {d}", Trees + "\ninput T is CopyTree", Chain(d) + " is Copy", "+ C");
            failures += Expect($"G170 depth {d}, unrelated contract", Trees + "\ncontract Foo\ninput T is CopyTree", Chain(d) + " is Foo", "none C");
        }

        const string NonRec = """
            contract Copy
            contract Holder
              associate Item
            contract CopyHolder : Holder
              clause Self.(Holder).Item is Copy
            contract Outer
              associate Inner is Holder
            contract CopyOuter : Outer
              clause Self.(Outer).Inner is CopyHolder
            input T is CopyOuter
            """;
        failures += Expect("non-recursive variant", NonRec, "T.(Outer).Inner.(Holder).Item is Copy", "+ C");

        // PlanB §3 B: an identity reaching a different subject (prefixes of U are {U}).
        const string Identity = """
            contract Copy
            contract Aa
              associate X
              clause Self.(Aa).X is Copy
            contract Mm
              associate Z
              associate W is Aa
              clause Self.(Mm).Z is Self.(Mm).W.(Aa).X
            """;
        failures += Expect("B: identity to another subject (input)", Identity + "\ninput T is Aa\ninput U is T.(Aa).X", "U is Copy", "+ C");
        failures += Expect("B: identity to another subject (derived)", Identity + "\ninput T is Mm\ninput U is T.(Mm).Z", "U is Copy", "+ C");
        failures += Expect("B: identity, other direction", Identity + "\ninput T is Aa\ninput T.(Aa).X is U", "U is Copy", "+ C");

        // PlanB §3 B: relevant arguments not visible in the requested prefixes (a Contract-argument clause).
        const string Args = """
            contract Ee
            contract Dd<A>
              clause A is Ee
            contract Cc
              associate Y
              associate X is Dd<Self.(Cc).Y>
            input T is Cc
            """;
        failures += Expect("B: Contract argument clause", Args, "T.(Cc).Y is Ee", "+ C");
        failures += Expect("B: Contract argument clause, other subject", Args, "T.(Cc).X is Ee", "none C");

        // Deep and non-prefix subjects.
        const string Deep = """
            contract Copy
            contract Bb
              associate Y
            contract Aa
              associate X is Bb
              clause Self.(Aa).X.(Bb).Y is Copy
            input T is Aa
            """;
        failures += Expect("deep subject", Deep, "T.(Aa).X.(Bb).Y is Copy", "+ C");
        failures += Expect("deep subject through an identity (non-prefix)", Deep + "\ninput U is T.(Aa).X", "U.(Bb).Y is Copy", "+ C");

        // Absent-head binder (PlanB §4.3 step 3): A is never invented.
        const string Absent = """
            contract Copy
            contract Tree
              associate Child is Tree
            contract Cg<A> : Tree
              clause Self.(Tree).Child is Copy
            """;
        failures += Expect("absent-head binder, instance available", Absent + "\ninput T is Cg<i32>", "T.(Tree).Child is Copy", "+ C");
        failures += Expect("absent-head binder, no instance", Absent + "\ninput T is Tree", "T.(Tree).Child is Copy", "none C");

        // Ancestors and Contract arguments; an identity with the Contract argument.
        const string Ancestors = """
            contract Pp<A>
              associate X
              clause Self.(Pp<A>).X is A
            contract Qq : Pp<i32>
            input T is Qq
            """;
        failures += Expect("ancestor with arguments", Ancestors, "T is Pp<i32>", "+ C");
        failures += Expect("ancestor, other arguments", Ancestors, "T is Pp<i64>", "none C");
        failures += Expect("inherited identity on the argument", Ancestors, "T.(Pp<i32>).X is i32", "+ C");

        // G180: Self inside a Contract reference's Type arguments.
        const string G180 = """
            contract Dd<A>
              associate N
            contract Cc
              associate X is Dd<Self>
            contract C2 : Dd<Self>
            """;
        failures += Expect("G180 associate X is D<Self>", G180 + "\ninput T is Cc", "T.(Cc).X is Dd<T>", "+ C");
        failures += Expect("G180 not D<U>", G180 + "\ninput T is Cc", "T.(Cc).X is Dd<U>", "none C");
        failures += Expect("G180 contract C2: D<Self>", G180 + "\ninput T is C2", "T is Dd<T>", "+ C");
        const string H6 = """
            contract Ee
            contract Dd<A>
              clause A is Ee
              associate N is Dd<Self.(Dd<A>).M>
              associate M is Ee
            contract Cc
              associate X is Dd<Self.(Cc).Y>
              associate Y is Ee
            input T is Cc
            """;
        failures += Expect("G180 h6 N", H6, "T.(Cc).X.(Dd<T.(Cc).Y>).N is Dd<T.(Cc).X.(Dd<T.(Cc).Y>).M>", "+ C");
        failures += Expect("G180 h6 M", H6, "T.(Cc).X.(Dd<T.(Cc).Y>).M is Ee", "+ C");

        // PlanB §3 D: growing substitutions; finite answers both ways.
        const string Growing = """
            contract Copy
            contract Gc<A>
              associate Child is Gc<List<A>>
              clause A is Copy
            input T is Gc<i32>
            """;
        failures += Expect("D: growing argument, derivable", Growing, "List<List<i32>> is Copy", "+ C");
        failures += Expect("D: growing argument, not derivable", Growing, "List<List<i64>> is Copy", "none C");
        failures += ExpectEnumerate("D: growing argument, enumerate T", Growing, "T", "C");

        // Logic: negation, contradictory evidence, exact compounds.
        const string Logic = """
            contract Copy
            contract Aa
            contract Bb
            contract Nn
              associate X
              clause Self.(Nn).X is not Copy
            contract Cc
              associate X is Aa or Bb
              associate Y is Aa and Bb
            """;
        failures += Expect("negated evidence", Logic + "\ninput T is Nn", "T.(Nn).X is Copy", "- C");
        failures += Expect("contradictory evidence", Logic + "\ninput T is Nn\ninput T.(Nn).X is Copy", "T.(Nn).X is Copy", "+- C");
        failures += Expect("disjunction supplies no disjunct", Logic + "\ninput T is Cc", "T.(Cc).X is Aa", "none C");
        failures += Expect("exact disjunction", Logic + "\ninput T is Cc", "T.(Cc).X is Aa or Bb", "+ C");
        failures += Expect("conjunction supplies conjuncts", Logic + "\ninput T is Cc", "T.(Cc).Y is Bb", "+ C");
        failures += Expect("exact conjunction", Logic + "\ninput T is Cc", "T.(Cc).Y is Aa and Bb", "+ C");
        failures += Expect("negated compound input", Logic + "\ninput T is not (Aa or Bb)", "T is Aa or Bb", "- C");

        // Proof recursion: an unseeded component supplies nothing; an independent support settles it.
        const string Cycle = """
            contract Aa
            contract Bb
            conformance S1 is Aa when S1 is Bb
            conformance S1 is Bb when S1 is Aa
            conformance S2 is Aa when S2 is Bb
            conformance S2 is Bb when S2 is Aa
            input S2 is Bb
            """;
        failures += Expect("unseeded proof cycle", Cycle, "S1 is Aa", "none C");
        failures += Expect("cycle with an independent support", Cycle, "S2 is Aa", "+ C");

        // Recursive-Contract prohibition (SPEC 8.3.3): a concrete conformance to Tree is never evidence.
        failures += Expect("recursive Contract, concrete conformance", Trees + "\nconformance S1 is Tree\n  binding Child = S1", "S1 is Tree", "none C");

        // Pending declaration: provisional, never a cached negative; discoverable through discovery.
        const string Pending = """
            contract Copy
            contract Aa pending
              associate X
              clause Self.(Aa).X is Copy
            input T is Aa
            """;
        failures += Expect("pending declaration", Pending, "T.(Aa).X is Copy", "none P");
        failures += ExpectEnumerate("pending declaration, discovery", Pending, "T.(Aa).X", "C", discovery: true, expectFact: "+Copy");

        // Evidence views: exclusion of the definition's own inferred value; a completed external value; independent support.
        const string Views = """
            contract Pp
              associate X
            contract Qq
              associate W
            conformance S1 is Pp
              binding X = i32 inferred 1
            conformance S2 is Qq
              binding W = S1.(Pp).X inferred 2
            """;
        var own = new View("frozen definition 1", [0], wantsReads: true);
        var other = new View("frozen definition 2", [1], wantsReads: true);
        failures += Expect("view: own inferred value excluded", Views, "S1.(Pp).X is i32", "none C", own);
        failures += Expect("view: completed external value admissible", Views, "S1.(Pp).X is i32", "+ C", other, reads: "{0}");
        failures += Expect("view: own value through another definition", Views, "S2.(Qq).W is i32", "none C", other);
        failures += Expect("view: ordinary, reads through both", Views, "S2.(Qq).W is i32", "+ C", new View("ordinary+reads", [], wantsReads: true), reads: "{0,1}");
        failures += Expect("view: independent explicit support", Views + "\ninput S1.(Pp).X is i32", "S1.(Pp).X is i32", "+ C", other, reads: "{}");

        // Closed-side identity in a recursive Contract: finite answers where the polarity settles; exhaustive work is Resource.
        const string IntTree = """
            contract Copy
            contract It
              associate Child is It
              associate Value
              clause Self.(It).Value is i32
              clause Self.(It).Value is Copy
            input T is It
            """;
        failures += Expect("closed identity, recursive: projection", IntTree, "T.(It).Value is Copy", "+ C");
        failures += Expect("closed identity, recursive: the closed Type", IntTree, "i32 is Copy", "+ C");
        failures += Expect("closed identity, recursive: frozen view, a read-free support", IntTree, "i32 is Copy", "+ C", new View("reads", [], wantsReads: true), reads: "{}");
        const string IntTreeRead = """
            contract Copy
            contract Pp
              associate X
            contract It
              associate Child is It
              associate Value
              clause Self.(It).Value is S1.(Pp).X
              clause Self.(It).Value is Copy
            input T is It
            conformance S1 is Pp
              binding X = i32
            """;
        failures += Expect("closed identity through a binding: ordinary view", IntTreeRead, "i32 is Copy", "+ C");
        failures += Expect("closed identity through a binding: every support reads it", IntTreeRead, "i32 is Copy", "R", new View("reads", [], wantsReads: true), limits: new Limits { Rounds = 200, Size = 20_000 });
        const string IntTreeArgs = """
            contract Dd<A>
            contract It
              associate Child is It
              associate Value
              clause Self.(It).Value is i32
              clause Self.(It).Value is Dd<Self>
            input T is It
            """;
        failures += Expect("closed identity, argument bound by the goal", IntTreeArgs, "i32 is Dd<T.(It).Child>", "+ C");
        failures += Expect("closed identity, argument not derivable", IntTreeArgs, "i32 is Dd<U>", "none C");
        failures += ExpectEnumerate("closed identity, enumerate the closed Type", IntTreeArgs, "i32", "R", limits: new Limits { Rounds = 200, Size = 20_000 });
        return failures;
    }

    private static int Expect(string name, string text, string query, string expected, View? view = null, Limits? limits = null, string? reads = null)
    {
        var m = ModelText.Parse(text);
        var f = ModelText.ParseFormula(m, query);
        var a = new Solver(m).Supply(f, view ?? View.Ordinary, limits);
        var r6 = Ref(m, view ?? View.Ordinary, f, 6);
        var r8 = Ref(m, view ?? View.Ordinary, f, 8);
        var verdict = a.Outcome == Outcome.Resource ? "R" : a.Verdict + " " + (a.Outcome == Outcome.Complete ? "C" : "P");
        var readText = "{" + string.Join("},{", a.PositiveReads.Select(x => string.Join(",", x))) + "}";
        var ok = verdict == expected && (reads is null || readText == reads);
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | {query} | C {a} reads {readText} | ref6 {Show(r6)} ref8 {Show(r8)} | expected {expected}{(reads is null ? string.Empty : " reads " + reads)}");
        return ok ? 0 : 1;
    }

    private static int ExpectEnumerate(string name, string text, string subject, string expected, bool discovery = false, string? expectFact = null, Limits? limits = null)
    {
        var m = ModelText.Parse(text);
        var s = ModelText.ParseType(m, subject);
        var a = new Solver(m).Enumerate(s, View.Ordinary, limits, discovery);
        var verdict = a.Outcome == Outcome.Resource ? "R" : a.Outcome == Outcome.Complete ? "C" : "P";
        var ok = verdict == expected && (expectFact is null || a.Facts.Contains(expectFact));
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | enumerate {subject} | C {a} facts [{string.Join(" ", a.Facts)}] members [{string.Join(" ", a.Members)}] | expected {expected}");
        return ok ? 0 : 1;
    }

    private static (bool Pos, bool Neg, bool Overflow) Ref(ProgramModel m, View view, Formula f, int bound, bool pending = false, HashSet<int>? bindings = null)
    {
        var r = new Reference(m, view, pending, bound, bindings);
        var seeds = new List<Term>();
        f.Terms(seeds);
        r.Run(seeds);
        var (p, n) = r.Answer(f);
        return (p, n, r.Overflow);
    }

    private static string Show((bool Pos, bool Neg, bool Overflow) r) => r.Overflow ? "overflow" : (r.Pos ? "+" : "") + (r.Neg ? "-" : "") + (!r.Pos && !r.Neg ? "none" : "");

    // ---------------------------------------------------------------- generated models
    private sealed class Stats
    {
        public int Models;
        public int Overflow;
        public int Queries;
        public int Stable;
        public int Agree;
        public int Disagree;
        public int ResourceStable;
        public int Unstable;
        public int ResourceUnstable;
        public int Provisional;
        public int Enumerations;
        public int EnumAgree;
        public int EnumDisagree;
        public int EnumResource;
        public int EnumUnstable;
        public int ReadsChecked;
        public int ReadsAgree;
        public int ReadsOver;
        public int ReadsDisagree;
        public readonly Dictionary<string, int> PositiveBy = new();
        public readonly List<long> V = new();
        public readonly List<long> E = new();
        public readonly List<long> J = new();
        public readonly List<long> N = new();
        public readonly List<long> Rounds = new();
        public int Derived;
        public int ResourceNone;
        public int ResourceFrozen;

        public void Add(Stats o)
        {
            this.Models += o.Models;
            this.Overflow += o.Overflow;
            this.Queries += o.Queries;
            this.Derived += o.Derived;
            this.ResourceNone += o.ResourceNone;
            this.ResourceFrozen += o.ResourceFrozen;
            this.Stable += o.Stable;
            this.Agree += o.Agree;
            this.Disagree += o.Disagree;
            this.ResourceStable += o.ResourceStable;
            this.Unstable += o.Unstable;
            this.ResourceUnstable += o.ResourceUnstable;
            this.Provisional += o.Provisional;
            this.Enumerations += o.Enumerations;
            this.EnumAgree += o.EnumAgree;
            this.EnumDisagree += o.EnumDisagree;
            this.EnumResource += o.EnumResource;
            this.EnumUnstable += o.EnumUnstable;
            this.ReadsChecked += o.ReadsChecked;
            this.ReadsAgree += o.ReadsAgree;
            this.ReadsOver += o.ReadsOver;
            this.ReadsDisagree += o.ReadsDisagree;
            foreach (var (k, v) in o.PositiveBy)
            {
                this.PositiveBy[k] = this.PositiveBy.GetValueOrDefault(k) + v;
            }

            this.V.AddRange(o.V);
            this.E.AddRange(o.E);
            this.J.AddRange(o.J);
            this.N.AddRange(o.N);
            this.Rounds.AddRange(o.Rounds);
        }
    }

    private static int RandomAgreement(int count, int seed)
    {
        var rnd = new Random(seed);
        var cases = new List<(string Text, List<string> Queries, List<string> Subjects)>();
        for (var i = 0; i < count; i++)
        {
            var text = Generate(rnd, out var queries, out var subjects);
            cases.Add((text, queries, subjects));
        }

        var only = Environment.GetEnvironmentVariable("G170_MODEL");
        if (only is not null)
        {
            var (text, queries, subjects) = cases[int.Parse(only)];
            Console.WriteLine(text + "QUERIES: " + string.Join(" | ", queries) + Environment.NewLine + "SUBJECTS: " + string.Join(" | ", subjects));
            return 0;
        }

        var results = new (Stats Stats, List<string> Log)[count];
        var clock = Stopwatch.StartNew();
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = int.Parse(Environment.GetEnvironmentVariable("G170_THREADS") ?? "6") }, i =>
        {
            var sw = Stopwatch.StartNew();
            results[i] = OneModel(i, cases[i].Text, cases[i].Queries, cases[i].Subjects);
            if (sw.Elapsed.TotalSeconds > 10)
            {
                results[i].Log.Add($"SLOW model {i}: {sw.Elapsed.TotalSeconds:F0} s");
            }
        });

        var st = new Stats();
        var printed = 0;
        foreach (var (s, log) in results)
        {
            st.Add(s);
            foreach (var line in log)
            {
                if (!line.StartsWith("RESOURCE", StringComparison.Ordinal) && !line.StartsWith("ENUM-RESOURCE", StringComparison.Ordinal) && !line.StartsWith("READS-OVER", StringComparison.Ordinal) || printed++ < 12)
                {
                    Console.WriteLine(line);
                }
            }
        }

        Console.WriteLine($"== RANDOM seed {seed}: {clock.Elapsed.TotalSeconds:F0} s; models {st.Models} (reference overflow {st.Overflow}); Supply queries {st.Queries} ({st.Derived} of them reference-derived facts): stable {st.Stable} " +
            $"(agree {st.Agree}, disagree {st.Disagree}, C Resource {st.ResourceStable}), unstable {st.Unstable} (C Resource {st.ResourceUnstable}), provisional {st.Provisional}");
        Console.WriteLine($"   C Resource on stable queries: {st.ResourceStable}, of which the reference has evidence for neither polarity {st.ResourceNone}, in a frozen view {st.ResourceFrozen}");
        Console.WriteLine($"   answer kinds on stable agreement: {string.Join(", ", st.PositiveBy.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))}");
        Console.WriteLine($"   Enumerate: {st.Enumerations}: agree {st.EnumAgree}, disagree {st.EnumDisagree}, C Resource {st.EnumResource}, unstable {st.EnumUnstable}");
        Console.WriteLine($"   reads (frozen views, <=3 bindings): checked {st.ReadsChecked}: exact {st.ReadsAgree}, over-approximated {st.ReadsOver}, disagree {st.ReadsDisagree}");
        Console.WriteLine($"   cost per stable request: V {Dist(st.V)}; E {Dist(st.E)}; J {Dist(st.J)}; N {Dist(st.N)}; rounds {Dist(st.Rounds)}");
        return st.Disagree + st.EnumDisagree + st.ReadsDisagree;
    }

    private static (Stats, List<string>) OneModel(int i, string text, List<string> queries, List<string> subjects)
    {
        var st = new Stats();
        var log = new List<string>();
        var nl = Environment.NewLine;
        var m = ModelText.Parse(text);
        st.Models++;
        var views = new List<View> { View.Ordinary };
        var inferred = m.Bindings.Values.Where(b => b.Inferred).ToList();
        if (inferred.Count != 0)
        {
            views.Add(new View("frozen 0", inferred.Where(b => b.Definition == 0).Select(b => b.Id), wantsReads: true));
        }

        foreach (var view in views)
        {
            var formulas = queries.Select(q => ModelText.ParseFormula(m, q)).ToList();
            var seeds = new List<Term>();
            foreach (var f in formulas)
            {
                f.Terms(seeds);
            }

            var subjectTerms = subjects.Select(s => ModelText.ParseType(m, s)).ToList();
            var querySubjects = formulas.Where(f => f.IsAtom).Select(f => f.Subject).Distinct().ToList();
            seeds.AddRange(subjectTerms);
            var r1 = new Reference(m, view, false, 5);
            r1.Run(seeds);
            var r2 = new Reference(m, view, false, 7);
            r2.Run(seeds);
            if (r1.Overflow || r2.Overflow)
            {
                st.Overflow++;
                continue;
            }

            // Reference-derived queries: facts the bounded reference found on the query subjects (positive-biased).
            var derived = querySubjects.SelectMany(s => r1.FactsOn(s).Take(3).Select(x => x.Positive ? new Formula(FK.Is, s, x.Ref) : new Formula(FK.Not, l: new Formula(FK.Is, s, x.Ref)))).Take(8).ToList();
            st.Derived += derived.Count;
            var solver = new Solver(m);
            foreach (var f in formulas.Concat(derived))
            {
                st.Queries++;
                var a = solver.Supply(f, view, new Limits { Rounds = 300, Size = 30_000, Work = 3_000_000 });
                var x1 = r1.Answer(f);
                var x2 = r2.Answer(f);
                if (a.Outcome == Outcome.Provisional)
                {
                    st.Provisional++;
                }

                if (x1 != x2)
                {
                    st.Unstable++;
                    st.ResourceUnstable += a.Outcome == Outcome.Resource ? 1 : 0;
                    continue;
                }

                st.Stable++;
                if (a.Outcome == Outcome.Resource)
                {
                    st.ResourceStable++;
                    if (x1 == (false, false))
                    {
                        st.ResourceNone++;
                    }

                    if (view.WantsReads)
                    {
                        st.ResourceFrozen++;
                    }

                    log.Add($"RESOURCE-ON-STABLE model {i} view {view.Name}: {f} ref {x1} C {a}{nl}{text}");
                    lock (LogLock)
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "g170-resource-stable.txt"), $"model {i} view {view.Name}: {f} ref {x1} C {a}{nl}{text}{nl}");
                    }
                    continue;
                }

                st.V.Add(a.V);
                st.E.Add(a.E);
                st.J.Add(a.J);
                st.N.Add(a.N);
                st.Rounds.Add(a.Rounds);
                var kind = (f.Kind == FK.Not ? f.L! : f).Kind + (f.IsAtom ? string.Empty : "-compound") + (a.Positive ? "+" : "") + (a.Negative ? "-" : "");
                st.PositiveBy[kind] = st.PositiveBy.GetValueOrDefault(kind) + 1;
                if ((a.Positive, a.Negative) == x1)
                {
                    st.Agree++;
                }
                else
                {
                    st.Disagree++;
                    log.Add($"DISAGREE model {i} view {view.Name}: {f} ref {x1} C {a}{nl}{text}");
                }

                if (view.WantsReads && a.Positive && f.IsAtom && f.Kind != FK.Not && m.Bindings.Count <= 3 && st.ReadsChecked < 40)
                {
                    st.ReadsChecked++;
                    var semantic = MinimalSufficient(m, view, f);
                    var mine = a.PositiveReads.Select(x => string.Join(",", x)).OrderBy(x => x).ToList();
                    var theirs = semantic.Select(x => string.Join(",", x)).OrderBy(x => x).ToList();
                    if (mine.SequenceEqual(theirs))
                    {
                        st.ReadsAgree++;
                    }
                    else if (semantic.All(sx => a.PositiveReads.Any(cx => sx.All(cx.Contains))) && a.PositiveReads.All(cx => semantic.Any(sx => sx.All(cx.Contains))))
                    {
                        st.ReadsOver++;
                        log.Add($"READS-OVER model {i}: {f} C [{string.Join(" ", mine)}] semantic [{string.Join(" ", theirs)}]{nl}{text}");
                    }
                    else
                    {
                        st.ReadsDisagree++;
                        log.Add($"READS model {i}: {f} C [{string.Join(" ", mine)}] semantic [{string.Join(" ", theirs)}]{nl}{text}");
                    }
                }
            }

            if (view != View.Ordinary)
            {
                continue;
            }

            foreach (var s in subjectTerms)
            {
                st.Enumerations++;
                var a = solver.Enumerate(s, view, new Limits { Rounds = 300, Size = 30_000, Work = 3_000_000 });
                var f1 = r1.FactsOn(s).Select(x => (x.Positive, x.Ref)).ToList();
                var f2 = r2.FactsOn(s).Select(x => (x.Positive, x.Ref)).ToList();
                var c1 = Classes(r1, f1);
                var c2 = Classes(r2, f2);
                if (c1 != c2)
                {
                    st.EnumUnstable++;
                    continue;
                }

                if (a.Outcome == Outcome.Resource)
                {
                    st.EnumResource++;
                    log.Add($"ENUM-RESOURCE model {i}: {s} ref {c1} C {a}{nl}{text}");
                    continue;
                }

                var complete = f1.All(x => Holds(new Solver(m).Supply(new Formula(FK.Is, s, x.Ref), view), x.Positive));
                if (complete && a.Facts.Count == c1)
                {
                    st.EnumAgree++;
                }
                else
                {
                    st.EnumDisagree++;
                    log.Add($"ENUM-DISAGREE model {i}: {s} ref [{string.Join(" ", f1.Select(x => (x.Positive ? "+" : "-") + x.Ref))}] ({c1} classes) C [{string.Join(" ", a.Facts)}] complete={complete}{nl}{text}");
                }
            }
        }

        return (st, log);
    }

    private static bool Holds(Answer a, bool positive) => a.Outcome != Outcome.Resource && (positive ? a.Positive : a.Negative);

    private static int Classes(Reference r, List<(bool Positive, Term Ref)> facts)
    {
        var seen = new List<(bool, Term)>();
        foreach (var f in facts)
        {
            if (!seen.Any(x => x.Item1 == f.Positive && r.Same(x.Item2, f.Ref)))
            {
                seen.Add(f);
            }
        }

        return seen.Count;
    }

    private static List<int[]> MinimalSufficient(ProgramModel m, View view, Formula f)
    {
        var ids = m.Bindings.Keys.Where(b => !view.Inadmissible.Contains(b)).OrderBy(x => x).ToArray();
        var result = new List<int[]>();
        for (var mask = 0; mask < 1 << ids.Length; mask++)
        {
            var set = ids.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            if (result.Any(r => r.All(set.Contains)))
            {
                continue;
            }

            if (Ref(m, view, f, 5, bindings: new HashSet<int>(set)).Pos)
            {
                result.Add(set);
            }
        }

        return result.Where(r => !result.Any(o => o != r && o.All(r.Contains))).ToList();
    }

    private static string Dist(List<long> xs)
    {
        if (xs.Count == 0)
        {
            return "-";
        }

        var s = xs.OrderBy(x => x).ToList();
        return $"median {s[s.Count / 2]}, p95 {s[(int)(s.Count * 0.95)]}, max {s[^1]}";
    }

    public static string Generate(Random r, out List<string> queries, out List<string> subjects)
    {
        var sb = new StringBuilder();
        var n = r.Next(3, 6);
        var names = Enumerable.Range(0, n).Select(i => "K" + i).ToArray();
        var arity = names.Select(_ => r.Next(4) == 0 ? 1 : 0).ToArray();
        var assocs = names.Select(_ => new[] { "X", "Y" }.Where(_ => r.Next(3) != 0).ToArray()).ToArray();
        var usedOr = false;
        string Pick(params string[] xs) => xs[r.Next(xs.Length)];
        string Own(int i) => arity[i] == 1 ? names[i] + "<A>" : names[i];
        string Ref(int j, int ctx) => arity[j] == 0 ? names[j] : names[j] + "<" + Arg(ctx) + ">";
        string Arg(int ctx)
        {
            if (ctx < 0)
            {
                return Pick("i32", "i64", "U", "List<i32>");
            }

            var options = new List<string> { "i32", "Self", "List<i32>" };
            if (arity[ctx] == 1)
            {
                options.Add("A");
                options.Add("List<A>");
            }

            if (assocs[ctx].Length != 0)
            {
                options.Add("Self.(" + Own(ctx) + ")." + assocs[ctx][r.Next(assocs[ctx].Length)]);
            }

            return options[r.Next(options.Count)];
        }

        string Requirement(int ctx)
        {
            var j = r.Next(n);
            switch (r.Next(10))
            {
                case 0 or 1 or 2:
                    return Ref(j, ctx);
                case 3:
                    return "Cp";
                case 4:
                    return "not " + Pick(Ref(j, ctx), "Cp");
                case 5 or 6:
                    {
                        var options = new List<string> { "i32", "List<i32>" };
                        if (ctx >= 0 && assocs[ctx].Length != 0)
                        {
                            options.Add("Self.(" + Own(ctx) + ")." + assocs[ctx][r.Next(assocs[ctx].Length)]);
                            options.Add("Self");
                        }

                        if (ctx >= 0 && arity[ctx] == 1)
                        {
                            options.Add("A");
                        }

                        if (ctx < 0)
                        {
                            options.Add("U");
                        }

                        return options[r.Next(options.Count)];
                    }

                case 7:
                    return Pick("Cp", names[r.Next(n)] is var x && arity[Array.IndexOf(names, x)] == 0 ? x : "Cp") + " and " + (arity[j] == 0 ? names[j] : "Cp");
                default:
                    usedOr = true;
                    return (arity[j] == 0 ? names[j] : "Cp") + " or " + Pick("Cp", names[r.Next(n)] is var y && arity[Array.IndexOf(names, y)] == 0 ? y : "Cp");
            }
        }

        sb.Append("contract Cp\n");
        for (var i = 0; i < n; i++)
        {
            sb.Append("contract ").Append(Own(i));
            var parents = new List<string>();
            if (i > 0 && r.Next(10) < 3)
            {
                parents.Add(Ref(r.Next(i), i));
            }

            if (r.Next(10) < 2)
            {
                parents.Add("Cp");
            }

            if (parents.Count != 0)
            {
                sb.Append(" : ").Append(string.Join(", ", parents.Distinct()));
            }

            if (r.Next(100) < 8)
            {
                sb.Append(" pending");
            }

            sb.Append('\n');
            foreach (var z in assocs[i])
            {
                sb.Append("  associate ").Append(z);
                if (r.Next(10) < 6)
                {
                    sb.Append(" is ").Append(Requirement(i));
                }

                sb.Append('\n');
            }

            var clauses = r.Next(3);
            for (var c = 0; c < clauses; c++)
            {
                var subjectOptions = new List<string>();
                foreach (var z in assocs[i])
                {
                    subjectOptions.Add("Self.(" + Own(i) + ")." + z);
                    var j = r.Next(n);
                    if (assocs[j].Length != 0)
                    {
                        subjectOptions.Add("Self.(" + Own(i) + ")." + z + ".(" + Ref(j, i) + ")." + assocs[j][r.Next(assocs[j].Length)]);
                    }
                }

                if (arity[i] == 1)
                {
                    subjectOptions.Add("A");
                    var j = r.Next(n);
                    if (assocs[j].Length != 0)
                    {
                        subjectOptions.Add("A.(" + Ref(j, i) + ")." + assocs[j][r.Next(assocs[j].Length)]);
                    }
                }

                subjectOptions.Add("Self");
                var subject = subjectOptions[r.Next(subjectOptions.Count)];
                var requirement = Requirement(i);
                if (subject == "Self" && !names.Concat(["Cp", "not"]).Any(requirement.StartsWith))
                {
                    requirement = "Cp";
                }

                sb.Append("  clause ").Append(subject).Append(" is ").Append(requirement).Append('\n');
            }
        }

        var inputs = new List<string>();
        var tContract = r.Next(n);
        var tRef = Ref(tContract, -1);
        inputs.Add("T is " + tRef);
        if (r.Next(2) == 0)
        {
            inputs.Add("U is " + Ref(r.Next(n), -1));
        }

        string? tProjection = assocs[tContract].Length != 0 ? "T.(" + tRef + ")." + assocs[tContract][r.Next(assocs[tContract].Length)] : null;
        if (tProjection is not null && r.Next(10) < 3)
        {
            inputs.Add(r.Next(2) == 0 ? tProjection + " is U" : "U is " + tProjection);
        }

        if (r.Next(100) < 15)
        {
            inputs.Add("U is not " + Pick("Cp", names[r.Next(n)] is var x && arity[Array.IndexOf(names, x)] == 0 ? x : "Cp"));
        }

        if (r.Next(100) < 15)
        {
            usedOr = true;
            inputs.Add("T is Cp or " + (names[r.Next(n)] is var y && arity[Array.IndexOf(names, y)] == 0 ? y : "Cp"));
        }

        if (tProjection is not null && r.Next(100) < 15)
        {
            inputs.Add(tProjection + " is i32");
        }

        foreach (var input in inputs)
        {
            sb.Append("input ").Append(input).Append('\n');
        }

        string? s1Ref = null;
        var s1Contract = r.Next(n);
        if (r.Next(10) < 4)
        {
            s1Ref = arity[s1Contract] == 0 ? names[s1Contract] : names[s1Contract] + "<" + Pick("i32", "i64") + ">";
            sb.Append("conformance S1 is ").Append(s1Ref).Append(r.Next(10) == 0 ? " pending" : string.Empty).Append('\n');
            foreach (var z in assocs[s1Contract])
            {
                sb.Append("  binding ").Append(z).Append(" = ").Append(Pick("i32", "i64", "List<i32>", "S2"));
                if (r.Next(10) < 3)
                {
                    sb.Append(" inferred ").Append(r.Next(2));
                }

                if (r.Next(10) == 0)
                {
                    sb.Append(" pending");
                }

                sb.Append('\n');
            }
        }

        if (r.Next(10) < 2)
        {
            var j = r.Next(n);
            sb.Append("conformance S2 is ").Append(arity[j] == 0 ? names[j] : names[j] + "<i32>");
            if (r.Next(2) == 0)
            {
                var k = r.Next(n);
                sb.Append(" when S2 is ").Append(arity[k] == 0 ? names[k] : names[k] + "<i32>");
            }

            sb.Append('\n');
        }

        subjects = new List<string> { "T", "U", "i32", "S1", "List<i32>" };
        if (tProjection is not null)
        {
            subjects.Add(tProjection);
            var j = r.Next(n);
            if (assocs[j].Length != 0)
            {
                subjects.Add(tProjection + ".(" + Ref(j, -1) + ")." + assocs[j][r.Next(assocs[j].Length)]);
            }
        }

        if (s1Ref is not null && assocs[s1Contract].Length != 0)
        {
            subjects.Add("S1.(" + s1Ref + ")." + assocs[s1Contract][0]);
        }

        queries = new List<string>();
        for (var q = 0; q < 10; q++)
        {
            var s = subjects[r.Next(subjects.Count)];
            var j = r.Next(n + 1);
            var reference = j == n ? "Cp" : Ref(j, -1);
            queries.Add(s + " is " + reference);
        }

        for (var q = 0; q < 3; q++)
        {
            var a = subjects[r.Next(subjects.Count)];
            var b = Pick("i32", "i64", "U", "List<i32>", "S2", subjects[r.Next(subjects.Count)]);
            if (a != b && !names.Contains(b))
            {
                queries.Add(a + " is " + b);
            }
        }

        if (usedOr)
        {
            var j = Enumerable.Range(0, n).FirstOrDefault(x => arity[x] == 0);
            queries.Add("T is Cp or " + (arity[j] == 0 ? names[j] : "Cp"));
            if (tProjection is not null)
            {
                queries.Add(tProjection + " is Cp or " + (arity[j] == 0 ? names[j] : "Cp"));
            }
        }

        var enumSubjects = new List<string> { subjects[r.Next(subjects.Count)], subjects[r.Next(subjects.Count)] };
        subjects = enumSubjects.Distinct().ToList();
        return sb.ToString();
    }

    // ---------------------------------------------------------------- order invariance
    private static int Order(int count, int seed)
    {
        var rnd = new Random(seed);
        var checkedCount = 0;
        var differences = 0;
        for (var i = 0; i < count; i++)
        {
            var text = Generate(rnd, out var queries, out _);
            var m = ModelText.Parse(text);
            var shuffled = ModelText.Parse(text);
            var order = rnd;
            var list = shuffled.Templates.OrderBy(_ => order.Next()).ToList();
            shuffled.Templates.Clear();
            shuffled.Templates.AddRange(list);
            var a = new Solver(m);
            var b = new Solver(shuffled);
            foreach (var q in queries.Take(4))
            {
                foreach (var limits in new[] { new Limits { Rounds = 300, Size = 30_000, Work = 3_000_000 }, new Limits { Rounds = 6, Size = 30_000 }, new Limits { Rounds = 300, Size = 60 } })
                {
                    var x = a.Supply(ModelText.ParseFormula(m, q), View.Ordinary, limits);
                    var y = b.Supply(ModelText.ParseFormula(shuffled, q), View.Ordinary, limits);
                    checkedCount++;
                    if ((x.Verdict, x.Outcome, x.Rounds, x.V, x.E, x.N) != (y.Verdict, y.Outcome, y.Rounds, y.V, y.E, y.N))
                    {
                        differences++;
                        if (differences < 6)
                        {
                            Console.WriteLine($"ORDER model {i}: {q} limits {limits.Rounds}/{limits.Size}: {x} [{x.Detail}] vs {y} [{y.Detail}]");
                            if (Environment.GetEnvironmentVariable("G170_DUMP") is not null)
                            {
                                var xs = a.DumpSubs();
                                var ys = b.DumpSubs();
                                foreach (var line in xs.Except(ys))
                                {
                                    Console.WriteLine("  only original: " + line);
                                }

                                foreach (var line in ys.Except(xs))
                                {
                                    Console.WriteLine("  only shuffled: " + line);
                                }

                                Console.WriteLine($"  multiset sizes {xs.Count} {ys.Count}, distinct {xs.Distinct().Count()} {ys.Distinct().Count()}");
                            }

                        }
                    }
                }
            }
        }

        Console.WriteLine($"== ORDER: {checkedCount} requests (declaration order shuffled; three limit settings): {differences} differ in verdict, outcome, rounds, V, E or N");
        return differences;
    }

    // ---------------------------------------------------------------- resource boundary
    private static int ResourceBoundary()
    {
        var failures = 0;
        Console.WriteLine("== RESOURCE BOUNDARY");
        var m = ModelText.Parse(Trees + "\ninput T is CopyTree");
        foreach (var d in new[] { 8, 64 })
        {
            var f = ModelText.ParseFormula(m, Chain(d) + " is Copy");
            var free = new Solver(m).Supply(f, View.Ordinary);
            var rounds = free.Rounds;
            var size = free.PeakSize;
            var atRounds = new Solver(m).Supply(f, View.Ordinary, new Limits { Rounds = rounds });
            var belowRounds = new Solver(m).Supply(f, View.Ordinary, new Limits { Rounds = rounds - 1 });
            var atSize = new Solver(m).Supply(f, View.Ordinary, new Limits { Size = size });
            var belowSize = new Solver(m).Supply(f, View.Ordinary, new Limits { Size = size - 1 });
            var ok = atRounds.Outcome == Outcome.Complete && belowRounds.Outcome == Outcome.Resource && atSize.Outcome == Outcome.Complete && belowSize.Outcome == Outcome.Resource;
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} G170 depth {d}: needs rounds {rounds}, peak size {size}; limit=need {atRounds.Verdict}/{atRounds.Outcome}, need-1 rounds {belowRounds.Outcome} ({belowRounds.Reason}), need-1 size {belowSize.Outcome} ({belowSize.Reason})");
        }

        // An unbounded search: every support's reads of i32 is Copy in a recursive Contract with a closed identity.
        var it = ModelText.Parse("""
            contract Copy
            contract Pp
              associate X
            contract It
              associate Child is It
              associate Value
              clause Self.(It).Value is S1.(Pp).X
              clause Self.(It).Value is Copy
            input T is It
            conformance S1 is Pp
              binding X = i32
            """);
        var goal = ModelText.ParseFormula(it, "i32 is Copy");
        foreach (var size in new[] { 1_000, 10_000, 100_000 })
        {
            var a = new Solver(it).Supply(goal, new View("reads", [], wantsReads: true), new Limits { Size = size, Rounds = 100_000 });
            Console.WriteLine($"{(a.Outcome == Outcome.Resource ? "PASS" : "FAIL")} unbounded supports, size limit {size}: {a}");
            failures += a.Outcome == Outcome.Resource ? 0 : 1;
        }

        // Dependent-use stopping: a judgment needing a Resource answer stops with the Resource record; independent errors survive.
        var checks = new (string Name, string Query, View View)[]
        {
            ("required Copy (provable)", Chain(8, "Tree").Replace("T", "T") + " is Copy", View.Ordinary),
            ("required, every support (Resource)", "i32 is Copy", new View("reads", [], wantsReads: true)),
            ("required, independent and unprovable", "T is Copy", View.Ordinary),
        };
        var combined = """
            contract Copy
            contract Tree
              associate Child is Tree
            contract CopyTree : Tree, Copy
              clause Self.(Tree).Child is CopyTree
            contract Pp
              associate X
            contract It
              associate Child is It
              associate Value
              clause Self.(It).Value is S1.(Pp).X
              clause Self.(It).Value is Copy
            input T is It
            input V is CopyTree
            conformance S1 is Pp
              binding X = i32
            """;
        var cm = ModelText.Parse(combined);
        var records = new List<string>();
        foreach (var (name, query, view) in checks)
        {
            var q = query.Replace("T.(Tree)", "V.(Tree)");
            var a = new Solver(cm).Supply(ModelText.ParseFormula(cm, q), view, new Limits { Size = 20_000 });
            // The checker: Resource -> one located Resource record and the dependent judgment is absorbed; Unknown -> unproven.
            records.Add(a.Outcome == Outcome.Resource ? $"Resource({name})" : a.Positive && !a.Negative ? $"ok({name})" : $"Unproven({name})");
        }

        var expected = "ok(required Copy (provable)) Resource(required, every support (Resource)) Unproven(required, independent and unprovable)";
        var got = string.Join(" ", records);
        Console.WriteLine($"{(got == expected ? "PASS" : "FAIL")} dependent-use stopping: {got}");
        failures += got == expected ? 0 : 1;
        return failures;
    }

    // ---------------------------------------------------------------- cost
    private static void Cost()
    {
        Console.WriteLine("== COST (prototype, Release; median of 15 timed requests after 5 untimed; allocation per request)");
        var trees = ModelText.Parse(Trees + "\ncontract Foo\ninput T is CopyTree");
        foreach (var d in new[] { 1, 2, 4, 8, 16, 32, 64 })
        {
            Measure($"G170 Copy depth {d}", trees, Chain(d) + " is Copy");
        }

        foreach (var d in new[] { 8, 64 })
        {
            Measure($"G170 unrelated Foo depth {d} (exhausted)", trees, Chain(d) + " is Foo");
        }

        foreach (var w in new[] { 8, 32, 128 })
        {
            var sb = new StringBuilder("contract Copy\n");
            for (var i = 0; i < w; i++)
            {
                sb.Append($"contract W{i}\n  associate X\n  clause Self.(W{i}).X is Copy\n");
            }

            for (var i = 0; i < w; i++)
            {
                sb.Append($"input T is W{i}\n");
            }

            Measure($"wide: {w} Contracts on T", ModelText.Parse(sb.ToString()), $"T.(W{w - 1}).X is Copy");
        }

        foreach (var depth in new[] { 4, 16, 64 })
        {
            // Shared subgoals: a diamond refinement lattice of the given height.
            var sb = new StringBuilder("contract Copy\ncontract L0 : Copy\ncontract R0 : Copy\n");
            for (var i = 1; i <= depth; i++)
            {
                sb.Append($"contract L{i} : L{i - 1}, R{i - 1}\ncontract R{i} : L{i - 1}, R{i - 1}\n");
            }

            sb.Append($"contract Top : L{depth}, R{depth}\ninput T is Top\n");
            Measure($"diamond refinements height {depth}", ModelText.Parse(sb.ToString()), "T is Copy");
        }

        var growing = ModelText.Parse("""
            contract Copy
            contract Gc<A>
              associate Child is Gc<List<A>>
              clause A is Copy
            input T is Gc<i32>
            """);
        Measure("growing argument, derivable depth 6", growing, "List<List<List<List<List<List<i32>>>>>> is Copy");
        Measure("growing argument, not derivable depth 6", growing, "List<List<List<List<List<List<i64>>>>>> is Copy");

        // Repeated top-level requests: nothing is reused across requests (request-local), so the cost repeats.
        var solver = new Solver(trees);
        var goal = ModelText.ParseFormula(trees, Chain(8) + " is Copy");
        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            solver.Supply(goal, View.Ordinary);
        }

        Console.WriteLine($"repeated: 1000 top-level requests of G170 depth 8: {sw.Elapsed.TotalMilliseconds:F1} ms, {(GC.GetAllocatedBytesForCurrentThread() - before) / 1000} B per request");
    }

    private static void Measure(string name, ProgramModel m, string query)
    {
        var solver = new Solver(m);
        var f = ModelText.ParseFormula(m, query);
        Answer a = null!;
        for (var i = 0; i < 5; i++)
        {
            a = solver.Supply(f, View.Ordinary);
        }

        var times = new List<double>();
        long bytes = 0;
        for (var i = 0; i < 15; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            a = solver.Supply(f, View.Ordinary);
            times.Add(sw.Elapsed.TotalMicroseconds);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        times.Sort();
        Console.WriteLine($"{name}: {a} peak {a.PeakSize}; {times[times.Count / 2]:F0} us, {bytes} B");
    }
}
