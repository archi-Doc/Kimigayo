// G170 C1 prototype runner: fixed cases (PlanB §3's B/D counterexamples, §4.3's forms, the review's cases), agreement of C with the
// exhaustive bounded reference on generated finite models, order invariance (answers, counts, work and limit outcomes), the
// Resource boundary and its transport through judgments, and cost workloads including program-size scaling.
// Run: dotnet run -c Release -- <fixed|random|order|resource|cost|all> [count] [seed]; probes: trace|time|enum|scale <model file>
// <queries…>, pending [count] [seed], limit <items…> (time to the item limit), profile <model file> <queries…> | profile limit
// <items…> (time per phase); G170_REVIEW_PROFILE=params: two-parameter models;
// G170_REVIEW_PROFILE=nested: nested projections of two subjects made one Type by depth-1 identities, with bound patterns;
// G170_REVIEW_PROFILE=structural: constructed Types on the T-projection's other side (proposal B's decomposition, Refuted, conflicts;
// a generated model whose reference has a structural conflict or cycle is a declaration error, counted and not compared).
#pragma warning disable
#nullable enable
using System.Diagnostics;
using System.Text;

namespace G170C1;

public static class Runner
{
    private static readonly object LogLock = new();
    private static readonly Limits Harness = new() { Rounds = 300, Items = int.Parse(Environment.GetEnvironmentVariable("G170_ITEMS") ?? "10000") };

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
            var inadmissible = args.Length > 3 ? args[3].Split(',').Select(int.Parse) : [];
            Console.WriteLine(solver.Supply(ModelText.ParseFormula(m, args[2]), new View("trace", inadmissible), Harness));
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
                var a = new Solver(m).Supply(f, View.Ordinary, Harness);
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

        if (mode == "enum")
        {
            var m = ModelText.Parse(File.ReadAllText(args[1]));
            var subject = ModelText.ParseType(m, args[2]);
            var a = new Solver(m).Enumerate(subject, View.Ordinary, Harness);
            Console.WriteLine($"C {a}: facts [{string.Join(" ", a.Facts)}] members [{string.Join(" ", a.Members)}]");
            foreach (var q in args.Skip(3))
            {
                Console.WriteLine($"  supply {q}: {new Solver(m).Supply(ModelText.ParseFormula(m, q), View.Ordinary, Harness)}; ref5 {Show(Ref(m, View.Ordinary, ModelText.ParseFormula(m, q), 5))}");
            }

            return 0;
        }

        if (mode == "profile")
        {
            // Phase attribution (C1-design §7): `profile limit <items>` (the unbounded enumeration) or `profile <model> <query…>`
            // (`enumerate <subject>` enumerates); the second run of each request is reported (warm).
            if (args[1] == "limit")
            {
                foreach (var items in args.Skip(2).Select(long.Parse))
                {
                    var m = UnboundedModel();
                    var solver = new Solver(m);
                    Answer a = null!;
                    for (var i = 0; i < 2; i++)
                    {
                        a = solver.Enumerate(ModelText.ParseType(m, "i32"), View.Ordinary, new Limits { Items = items });
                    }

                    Console.WriteLine($"item limit {items}: {a} total {solver.Phases.TotalMs:F1} ms: {solver.Phases}");
                }
            }
            else
            {
                var m = ModelText.Parse(File.ReadAllText(args[1]));
                var solver = new Solver(m);
                foreach (var q in args.Skip(2))
                {
                    Answer a = null!;
                    for (var i = 0; i < 2; i++)
                    {
                        a = q.StartsWith("enumerate ", StringComparison.Ordinal) ? solver.Enumerate(ModelText.ParseType(m, q[10..]), View.Ordinary, Harness) : solver.Supply(ModelText.ParseFormula(m, q), View.Ordinary, Harness);
                    }

                    Console.WriteLine($"{q}: {a} total {solver.Phases.TotalMs:F1} ms: {solver.Phases}");
                }
            }

            return 0;
        }

        if (mode == "limit")
        {
            // Time to the item limit: enumerating a closed Type's infinite class (`resource`'s unbounded case) at each item value.
            foreach (var items in args.Skip(1).Select(long.Parse))
            {
                var m = UnboundedModel();
                var sw = Stopwatch.StartNew();
                var a = new Solver(m).Enumerate(ModelText.ParseType(m, "i32"), View.Ordinary, new Limits { Items = items });
                Console.WriteLine($"item limit {items}: {a} ({sw.Elapsed.TotalSeconds:F2} s)");
            }

            return 0;
        }

        if (mode == "scale")
        {
            var m = ModelText.Parse(File.ReadAllText(args[1]));
            var solver = new Solver(m);
            foreach (var q in args.Skip(2))
            {
                var f = ModelText.ParseFormula(m, q);
                Answer a = null!;
                for (var i = 0; i < 5; i++)
                {
                    a = solver.Supply(f, View.Ordinary);
                }

                var times = new List<double>();
                for (var i = 0; i < 21; i++)
                {
                    var sw = Stopwatch.StartNew();
                    a = solver.Supply(f, View.Ordinary);
                    times.Add(sw.Elapsed.TotalMicroseconds);
                }

                times.Sort();
                Console.WriteLine($"{q}: {a} median {times[10]:F0} us");
            }

            return 0;
        }

        if (mode == "pending")
        {
            var rnd = new Random(seed);
            int complete = 0, wrong = 0, prov = 0, provMatters = 0;
            for (var i = 0; i < count; i++)
            {
                var text = Generate(rnd, out var queries, out _);
                var m = ModelText.Parse(text);
                var solver = new Solver(m);
                foreach (var q in queries)
                {
                    var f = ModelText.ParseFormula(m, q);
                    var a = solver.Supply(f, View.Ordinary, Harness);
                    if (a.Outcome == Outcome.Resource)
                    {
                        continue;
                    }

                    var r5 = Ref(m, View.Ordinary, f, 5);
                    var r7 = Ref(m, View.Ordinary, f, 7);
                    var p5 = Ref(m, View.Ordinary, f, 5, pending: true);
                    var p7 = Ref(m, View.Ordinary, f, 7, pending: true);
                    if (r5.Overflow || r7.Overflow || p5.Overflow || p7.Overflow || r5 != r7 || p5 != p7)
                    {
                        continue;
                    }

                    var matters = (p5.Pos, p5.Neg) != (r5.Pos, r5.Neg);
                    if (a.Outcome == Outcome.Complete)
                    {
                        complete++;
                        if (matters)
                        {
                            wrong++;
                            Console.WriteLine($"COMPLETE-BUT-PENDING-MATTERS model {i}: {q}: C {a}; ref {Show(r5)}; with pending {Show(p5)}{Environment.NewLine}{text}");
                        }
                    }
                    else
                    {
                        prov++;
                        provMatters += matters ? 1 : 0;
                    }
                }
            }

            Console.WriteLine($"== PENDING seed {seed}: Complete {complete}, pending would change {wrong}; Provisional {prov}, pending changes {provMatters}");
            return wrong;
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

    /// <summary>Seed 20261022 model 312 (default profile): `i32 is Cp` establishes its positive polarity and then exhausts the harness's
    /// item limit (3,000); the established polarity stands (proposal B 3.10).</summary>
    private const string LimitKeepsProven = """
        contract Cp
        contract K0
          associate X is not K0
          associate Y is K4
          clause Self is K2
        contract K1
          associate X is not Cp
          clause Self is Cp
          clause Self.(K1).X.(K1).X is i32
        contract K2 : K1
          associate X is i32
          associate Y is K3 and K4
          clause Self.(K2).X.(K2).Y is K1
          clause Self.(K2).X is Cp
        contract K3 : K0, Cp
          associate X
        contract K4
          associate X is K3
          clause Self.(K4).X is K4
        input T is K2
        input U is T.(K2).Y
        input T is Cp or K3
        input List<U> is K0
        conformance S1 is K4 pending
          binding X = i64
        """;

    /// <summary>The review's scaling program: n concrete conformances binding X = i32, unrelated to Holder.</summary>
    public static string Many(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract Pp\n  associate X\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\nconformance i32 is Copy\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"conformance S{i} is Pp\n  binding X = i32\n");
        }

        return sb.ToString();
    }

    /// <summary>The round-2 review's selfid program: n Contracts each declaring `associate X is Self` (an identity side that is a
    /// variable), unrelated to Holder.</summary>
    public static string SelfId(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"contract W{i}\n  associate X is Self\n");
        }

        return sb.ToString();
    }

    /// <summary>The round-2 review's open program: n Contracts each declaring `associate X is List&lt;Self&gt;` and
    /// `Self.(Wi).Y is Option&lt;Self.(Wi).X&gt;` (sides mentioning the row's subject), U conforming to the first.</summary>
    public static string Open(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\ninput U is W1\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"contract W{i}\n  associate X is List<Self>\n  associate Y\n  clause Self.(W{i}).Y is Option<Self.(W{i}).X>\n");
        }

        return sb.ToString();
    }

    /// <summary>The round-3 review's palias program: n Contracts `Ki&lt;A&gt;: associate X is A` (a parameter identity side), with a Copy
    /// fact on another subject.</summary>
    public static string PAlias(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\ninput V is Copy\ninput T is K3<i32>\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"contract K{i}<A>\n  associate X is A\n");
        }

        return sb.ToString();
    }

    /// <summary>The round-3 review's deep2 program: n Contracts `Wi: Self is Self.(Wi).X.(Wi).X` (a nested-projection identity side),
    /// with a Copy fact on another subject.</summary>
    public static string Deep2(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\ninput V is Copy\ninput T is W3\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"contract W{i}\n  associate X\n  clause Self is Self.(W{i}).X.(W{i}).X\n");
        }

        return sb.ToString();
    }

    /// <summary>The round-4 review's shared program (R4-B1): n Contracts `Ki&lt;A&gt;: Self is It; Self.(It).Item is A` fixing one
    /// associated Type, with a Copy fact on another subject; no Ki has a fact (dead rows).</summary>
    public static string Shared(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract It\n  associate Item\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"contract K{i}<A>\n  clause Self is It\n  clause Self.(It).Item is A\n");
        }

        sb.Append("input V is Copy\n");
        return sb.ToString();
    }

    /// <summary>Shared with every Ki live (a conformance `Si is Ki&lt;i32&gt;` each): the live witness of R4-B1.</summary>
    public static string SharedLive(int n)
    {
        var sb = new StringBuilder(Shared(n));
        sb.Append("contract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"conformance S{i} is K{i}<i32>\n");
        }

        return sb.ToString();
    }

    /// <summary>Shared with two Ki live through one subject (`S is K1&lt;T&gt;`, `S is K2&lt;U&gt;`, `U is Copy`).</summary>
    public static string SharedPos(int n)
        => Shared(n) + "contract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\ninput S is K1<T>\ninput S is K2<U>\ninput U is Copy\n";

    /// <summary>Proposal B's open-live witness (rule 8 up): n live Contracts whose identity side is a constructed Type with a variable
    /// kid (`Wi: associate X is List&lt;Self&gt;`, each with a conformance), with a Copy fact on another subject: a class demand requests
    /// every such row once, so V and J are expected linear in n.</summary>
    public static string OpenLive(int n)
        => "contract Copy\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\ninput V is Copy\n" + string.Concat(Enumerable.Range(1, n).Select(i => $"contract W{i}\n  associate X is List<Self>\nconformance S{i} is W{i}\n"));

    /// <summary>The round-4 review's mix program: n pairs of a variable identity side (`Wi: associate X is Self`) and a parameter
    /// identity side (`Ki&lt;A&gt;: associate X is A`), T conforming to W3, with a Copy fact on another subject.</summary>
    public static string Mix(int n)
    {
        var sb = new StringBuilder("contract Copy\ncontract Holder\n  associate Item\n  clause Self.(Holder).Item is Copy\ninput T is Holder\ninput T is W3\ninput V is Copy\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"contract W{i}\n  associate X is Self\ncontract K{i}<A>\n  associate X is A\n");
        }

        return sb.ToString();
    }

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

        // Recursive-Contract prohibition (SPEC 8.3.3): a concrete conformance to Tree is never evidence; the classification
        // follows argument routes (the review's rec-arg: Cr reaches its projection through Dr's argument clause).
        failures += Expect("recursive Contract, concrete conformance", Trees + "\nconformance S1 is Tree\n  binding Child = S1", "S1 is Tree", "none C");
        const string RecArg = """
            contract Cr
              associate X
              clause Self is Dr<Self.(Cr).X>
            contract Dr<A>
              clause A is Cr
            input T is Cr
            conformance S1 is Cr
              binding X = S1
            """;
        failures += Expect("recursive through an argument route: projection", RecArg, "T.(Cr).X is Cr", "+ C");
        failures += Expect("recursive through an argument route: concrete conformance", RecArg, "S1 is Cr", "none C");

        // Seed 7 model 85: T.(Kb).Y is i32, and i32 is Kb through Ka's argument clause: a projection of T conforms to Kb.
        const string RecClosed = """
            contract Ka<A>
              clause A is Kb
            contract Kb
              associate X is Ka<i32>
              associate Y is i32
            input T is Kb
            conformance S1 is Kb
              binding X = S2
              binding Y = i32
            """;
        failures += Expect("recursive through an identity with a closed Type: projection", RecClosed, "T.(Kb).Y is Kb", "+ C");
        failures += Expect("recursive through an identity with a closed Type: concrete conformance", RecClosed, "S1 is Kb", "none C");

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

        // Published Resource states: a source whose own check ended at a limit is read as Resource, never as absent.
        const string Res = """
            contract Copy
            contract Aa
              associate X
            contract Bb resource
              associate Y
              clause Self.(Bb).Y is Copy
            conformance S1 is Aa resource
              binding X = i32
            conformance S2 is Aa
            input T is Bb
            """;
        failures += Expect("Resource conformance: its fact", Res, "S1 is Aa", "R");
        failures += Expect("Resource conformance: its binding", Res, "S1.(Aa).X is i32", "R");
        failures += Expect("Resource declaration: its clause", Res, "T.(Bb).Y is Copy", "R");
        failures += Expect("Resource sources, independent evidence", Res, "S2 is Aa", "+ C");
        failures += Expect("Resource sources, an input", Res, "T is Bb", "+ C");
        failures += Expect("the conformance under its own check (SPEC 8.3.3)", Res, "S2 is Aa", "none C", new View("check S2", [], [1]));

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
        var own = new View("frozen definition 1", [0]);
        var other = new View("frozen definition 2", [1]);
        failures += Expect("view: own inferred value excluded", Views, "S1.(Pp).X is i32", "none C", own);
        failures += Expect("view: completed external value admissible", Views, "S1.(Pp).X is i32", "+ C", other);
        failures += Expect("view: own value through another definition", Views, "S2.(Qq).W is i32", "none C", other);
        failures += Expect("view: ordinary, through both values", Views, "S2.(Qq).W is i32", "+ C");
        failures += Expect("view: independent explicit support", Views + "\ninput S1.(Pp).X is i32", "S1.(Pp).X is i32", "+ C", other);

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
        failures += Expect("closed identity through a binding", IntTreeRead, "i32 is Copy", "+ C");
        failures += Expect("closed identity through a binding: a frozen view settles like an ordinary one", IntTreeRead, "i32 is Copy", "+ C", new View("frozen, nothing excluded", []));
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
        failures += ExpectEnumerate("closed identity, enumerate the closed Type", IntTreeArgs, "i32", "R", limits: new Limits { Rounds = 200, Items = 20_000 });

        // Upward congruence needs the kids' identity: a class demand reaches its nodes' kids (found by this revision).
        const string Kids = """
            contract Copy
            contract Pp
              associate X
            contract Kk
              associate Y
              clause Self.(Kk).Y is Copy
            conformance S1 is Pp
              binding X = i32
            """;
        failures += Expect("congruence through a kid: a fact on List<i32>", Kids + "\ninput List<i32> is Copy", "List<S1.(Pp).X> is Copy", "+ C");
        failures += Expect("congruence through a kid: a member through List<i32>", Kids + "\ninput T is Kk\ninput T.(Kk).Y is List<i32>", "List<S1.(Pp).X> is Copy", "+ C");

        // One laziness rule for closed sides: a parameter-only side reached from an identity goal (bound expansion), and the
        // binding rows matched through their value (lazy) or their projection (eager).
        // A chain through a chain (random model 359): List<i32> = S1.(Kk).X (a closed side), = i64 (binding X's open side, meeting the
        // class only through that virtual node), = S1.(Kk).Y (binding Y's closed side, meeting it only through that chain).
        const string Chains = """
            contract Copy
            contract Kk
              associate X is List<i32>
              associate Y is Copy
            conformance S1 is Kk
              binding X = i64
              binding Y = i64
            """;
        failures += Expect("a chain through a chain", Chains, "List<i32> is Copy", "+ C");

        // Earlier gaps of this revision: a Contract concluded only by an input reaches the goal through identity (random model
        // 130); an identity goal's classes meet (model 97); a chain through another instance of the same row (model 12).
        failures += Expect("an input's Contract is concludable", "contract Kk\ncontract Kj\n  associate Y\n  clause Self.(Kj).Y is Self\ninput T is Kk\ninput T is Kj", "T.(Kj).Y is Kk", "+ C");
        // Proposal B (2026-10-22) changes this case's meaning: i32 and List<i32> in one class have different heads, a structural
        // conflict (B 3.3, 3.10: a premise error of K2, Error on the identity the request touches); before B it was `+ C`.
        failures += Expect("an identity goal's classes meet", "contract K2\n  associate Y is List<i32>\n  clause Self.(K2).Y is i32\ninput T is K2", "i32 is List<i32>", "+- C");
        failures += Expect("a chain through another instance of the same row", "contract Kc<A>\n  clause A.(Kd).Y is List<i32>\ncontract Kd\n  associate Y is Kc<Self>\ninput T is Kd\ninput U is Kd", "T.(Kd).Y is Kc<U>", "+ C");

        // Seed 7 model 225: T.(K1).X = List<i32> = S1.(K1).X = i32 (binding X's open side meets the class through K1's virtual node
        // ?.(K1).X, whose first kid can be anything); under List<…> the need for i32 goes down to the kid (upward congruence) and
        // is answered as its own lookup in a later round.
        const string Kid = """
            contract Cp
            contract K0<A>
              clause A is Cp
            contract K1 : K0<i32>
              associate X is List<i32>
            input T is K1
            conformance S1 is K1
              binding X = i32
            """;
        failures += Expect("a chain through a virtual node's first kid", Kid, "T.(K1).X is Cp", "+ C");
        failures += Expect("a need under a ground Type's kid", Kid, "List<T.(K1).X> is Cp", "+ C");

        // Seed 7 model 395: A is Kz<A> binds A = i64 from the argument; the subject i32 must then be i64's class: the classes meet.
        // Proposal B (2026-10-22) changes this case's meaning: the binding Y = i64 fails its Contract's associated requirement
        // `Y is i32` (B 3.7, an obligation; the reference's `!`: i32 and i64 in one class), and i32 and i64 never meet (rule 7),
        // so the request supplies nothing; before B it was `+ C` through that identity.
        failures += Expect("a bound variable meets the subject class", "contract Kz<A>\n  associate Y is i32\n  clause A is Kz<A>\nconformance S1 is Kz<i64>\n  binding Y = i64", "i32 is Kz<i64>", "none C");
        failures += Expect("closed parameter side, identity goal", "contract Kk<A>\n  clause A is List<i32>\ninput T is Kk<U>", "U is List<i32>", "+ C");
        failures += Expect("many bindings: through the projection", Many(50), "S7.(Pp).X is Copy", "+ C");
        failures += Expect("many bindings: an unrelated compound", Many(50), "List<i32> is Copy", "none C");
        failures += Expect("many bindings: a projection needed in the closed class (each binding is a candidate)", Many(50) + "contract Ww\ncontract Uu\n  clause Self.(Pp).X is Ww\nconformance S9 is Uu\n", "i32 is Ww", "+ C");

        // Round 2 (R2-B1): one need-driven rule for every identity side. An identity row whose side is a variable or mentions the
        // subject is selected only where a need, a meeting or an existing node reaches it: the work of a request is the same with 4
        // or 64 unrelated Contracts, and the rows are found where they are needed.
        failures += Expect("a variable identity side, needed through a projection", SelfId(8) + "input T is W3\n", "T.(W3).X.(Holder).Item is Copy", "+ C");
        failures += Expect("an open identity side, needed by an identity goal through two rows", Open(8), "U.(W1).Y is Option<List<U>>", "+ C");
        failures += Expect("an open identity side, not provable", Open(8), "U.(W2).Y is Option<List<U>>", "none C");
        failures += ExpectFlat("unrelated request, variable identity sides", SelfId, "T is Copy", "none C");
        failures += ExpectFlat("related request, variable identity sides", SelfId, "T.(Holder).Item is Copy", "+ C");
        failures += ExpectFlat("unrelated request, open identity sides", Open, "List<T> is Copy", "none C");
        failures += ExpectFlat("identity goal, open identity sides", Open, "U.(W1).Y is Option<List<U>>", "+ C");

        // A meeting at classes every variable side reaches requests no unrelated row (J reads their virtual nodes once: §7).
        failures += ExpectFlat("related identity goal, variable identity sides", n => SelfId(n) + "input T is W3\n", "T.(W3).X is T", "+ C", sameJ: false);
        failures += ExpectFlat("a meeting, variable identity sides", n => SelfId(n) + "input V is Copy\n", "T is Copy", "none C", sameJ: false);
        failures += ExpectFlat("a meeting, parameter identity sides", PAlias, "T is Copy", "none C", sameJ: false);
        failures += ExpectFlat("a meeting, nested-projection identity sides", Deep2, "T is Copy", "none C", sameJ: false);

        // Round 3 (R3-B1): a virtual node matches as a node, modulo classes. Nested projections of two subjects are one Type when the
        // subjects' projections are (T.(Inv).I and V.(Inv).I are both i32): met by two virtual nodes, and needed by a bound pattern.
        const string Inv = "contract Copy\ncontract Inv\n  associate I\n  clause Self.(Inv).I.(Inv).I is Self\ninput T is Inv\ninput V is Inv\ninput V is Copy\ninput T.(Inv).I is i32\ninput V.(Inv).I is i32";
        failures += Expect("an involution law: virtual nodes one Type modulo classes", Inv, "T is Copy", "+ C");
        failures += Expect("an involution law: the identity", Inv, "T is V", "+ C");
        failures += Expect("a nested-projection identity side met modulo classes", "contract Copy\ncontract W1\n  associate X\n  clause Self is Self.(W1).X.(W1).X\ninput T is W1\ninput V is W1\ninput V is Copy\ninput T.(W1).X is i32\ninput V.(W1).X is i32", "T is Copy", "+ C");
        failures += Expect("a free Contract argument inside a virtual node's subterm (decided once its row binds it)", "contract Copy\ncontract Inv<A>\n  associate I\n  clause Self.(Inv<A>).I.(Inv<A>).I is Self\ninput T is Inv<i64>\ninput V is Inv<i64>\ninput V is Copy\ninput T.(Inv<i64>).I is i32\ninput V.(Inv<i64>).I is i32", "T is Copy", "+ C");
        failures += Expect("a bound pattern needs a virtual node modulo classes", "contract W\n  associate X\ncontract D<A>\ncontract K\n  clause Self.(W).X.(W).X is D<Self>\ncontract M\n  associate P\n  clause Self.(M).P is Self.(W).X.(W).X\ninput T is K\ninput V is M\ninput T.(W).X is i32\ninput V.(W).X is i32", "V.(M).P is D<T>", "+ C");

        // Round 2 (R2-B2): Enumerate lists one fact per instance: same-declaration facts on the subject meet at their arguments
        // (two-parameter seed 4242 model 6, minimized; one and two parameters).
        failures += ExpectEnumerate("same-declaration facts meet (one parameter)", "contract Cp\ncontract K3<A>\n  associate X\n  clause Self is K3<i32>\ncontract K4<A>\n  associate Y is i32\n  clause Self is K3<Self.(K4<A>).Y>\nconformance S1 is K4<i64>\n  binding Y = i32", "S1", "C", facts: 2);
        failures += ExpectEnumerate("same-declaration facts meet (two parameters)", "contract Cp\ncontract K3<A>\n  associate X\n  clause Self is K3<i32>\ncontract K4<A, B>\n  associate Y is i32\n  clause Self is K3<Self.(K4<A, B>).Y>\ninput T is K4<i32, List<i32>>\nconformance S1 is K4<i64, List<i32>>\n  binding Y = i32", "S1", "C", facts: 2);

        // Round 4 (R4-B1): a promise is identified by (class, shape), never by the row or base producing it; dead rows are not rows.
        // n Contracts fixing one associated Type cost one promise per class (152dda62: V 36 / 256 at n = 3 / 10, 2n² chains).
        failures += Expect("shared identity sides, n = 3: unrelated request", Shared(3), "T is Copy", "none C");
        failures += Expect("shared identity sides, n = 10: unrelated request", Shared(10), "T is Copy", "none C");
        failures += ExpectFlat("unrelated request, shared identity sides", Shared, "T is Copy", "none C");
        failures += ExpectFlat("unrelated request, shared identity sides, two live", SharedPos, "W is Copy", "none C", sameJ: false);
        failures += Expect("shared identity sides, every row live, n = 3: unrelated request", SharedLive(3), "T is Copy", "none C");
        failures += Expect("shared identity sides, every row live, n = 10: unrelated request", SharedLive(10), "T is Copy", "none C");
        failures += ExpectLinear("unrelated request, shared identity sides, every row live", SharedLive, "T is Copy", "none C");

        // Round 4 (R4-B2): only the top is matched by head; a kid that is not a Type yet is decided once built (152dda62: none).
        const string Shape5 = "contract Copy\ncontract W\n  associate X\ncontract Gz\n  associate Z\ncontract G<A>\n  clause Self.(Gz).Z is Opt<A>.(W).X\ncontract Q<A>\n  clause List<A>.(W).X is Copy\ninput T is G<B0>\ninput T is Gz\ninput Opt<B0> is List<i32>\ninput S is Q<i32>";
        // Proposal B (2026-10-22) changes these two cases' meaning: the premise `Opt<B0> is List<i32>` is a structural conflict
        // (B 3.3, 3.10: a premise error of the declaration; the reference's `!`), and a conflicting class agrees with nothing
        // (rule 7), so the request supplies nothing; before B they were `+ C` through that identity. The R4-B2 witness keeps a
        // valid form below (`B0 is i32`, both clauses over `Opt`): the kid `Opt<B0>` of the promise is still not a Type when the
        // promise is matched and is decided once built.
        failures += Expect("a kid that is not a Type yet (shape5): a free Contract argument under the pattern's kid", Shape5, "T.(Gz).Z is Copy", "none C");
        failures += Expect("a kid that is not a Type yet, one level deeper (shape7)", Shape5.Replace("Opt<A>.(W).X", "Ev<Opt<A>>.(W).X").Replace("List<A>.(W).X", "Ev<List<A>>.(W).X"), "T.(Gz).Z is Copy", "none C");
        const string Shape5B = """
            contract Copy
            contract W
              associate X
            contract Gz
              associate Z
            contract G<A>
              clause Self.(Gz).Z is Opt<A>.(W).X
            contract Q<A>
              clause Opt<A>.(W).X is Copy
            input T is G<B0>
            input T is Gz
            input B0 is i32
            input S is Q<i32>
            """;
        failures += Expect("a kid that is not a Type yet is decided once built (shape5, valid under B)", Shape5B, "T.(Gz).Z is Copy", "+ C");
        failures += Expect("a kid that is not a Type yet, one level deeper (shape7, valid under B)", Shape5B.Replace("Opt<A>.(W).X", "Ev<Opt<A>>.(W).X"), "T.(Gz).Z is Copy", "+ C");

        // The round-4 review's seven variants of R3-B1 (each none on f550b474, + here and in the reference).
        const string Inv3 = "contract Copy\ncontract R\n  associate N\n  clause Self.(R).N.(R).N.(R).N is Self\ninput T is R\ninput V is R\ninput V is Copy\ninput T.(R).N is i32\ninput V.(R).N is i32";
        failures += Expect("an involution law of period 3: Copy", Inv3, "T is Copy", "+ C");
        failures += Expect("an involution law of period 3: the identity", Inv3, "T is V", "+ C");
        const string InvArg = "contract Copy\ncontract Inv<A>\n  associate I\n  clause Self.(Inv<A>).I.(Inv<A>).I is Self\ninput T is Inv<U>\ninput V is Inv<W>\ninput U is W\ninput V is Copy\ninput T.(Inv<U>).I is i32\ninput V.(Inv<W>).I is i32";
        failures += Expect("Contract arguments identical by identity: Copy", InvArg, "T is Copy", "+ C");
        failures += Expect("Contract arguments identical by identity: the identity", InvArg, "T is V", "+ C");
        const string InvKid = "contract Copy\ncontract Inv\n  associate I\n  clause Self.(Inv).I.(Inv).I is Self\ninput T is Inv\ninput V is Inv\ninput V is Copy\ninput T.(Inv).I is List<i32>\ninput V.(Inv).I is List<X>\ninput X is i32";
        failures += Expect("kid classes identical by congruence: Copy", InvKid, "T is Copy", "+ C");
        failures += Expect("kid classes identical by congruence: the identity", InvKid, "T is V", "+ C");
        failures += Expect("a depth-3 need through a bound pattern", "contract Copy\ncontract W\n  associate X\ncontract D<A>\ncontract K\n  clause Self.(W).X.(W).X.(W).X is D<Self>\ncontract M\n  associate P\n  clause Self.(M).P is Self.(W).X.(W).X.(W).X\ninput T is K\ninput V is M\ninput T.(W).X is i32\ninput V.(W).X is i32", "V.(M).P is D<T>", "+ C");
        failures += Expect("an involution law through conformances: the subjects are not one Type", "contract Copy\ncontract Inv\n  associate I\n  clause Self.(Inv).I.(Inv).I is Self\nconformance S1 is Inv\n  binding I = i32\nconformance S2 is Inv\n  binding I = i32\nconformance S2 is Copy", "S1 is Copy", "none C");

        // Round 5: the join with wildcards (a promise kid that is a ground Type not interned yet against a class), a variable at two
        // positions (one meet, no request), and an inherited defect (need rows are tagged effects: 152dda62 answers none).
        const string JoinWild = "contract Copy\ncontract W\n  associate X\ncontract G\n  associate Z\n  clause Self.(G).Z is List<i32>.(W).X\ncontract Q<A>\n  clause List<A>.(W).X is Self\ninput T is G\ninput V is Q<i32>\ninput V is Copy\ninput List<i32> is Copy";
        failures += Expect("the join with a wildcard kid: Copy", JoinWild, "T.(G).Z is Copy", "+ C");
        failures += Expect("the join with a wildcard kid: the identity", JoinWild, "T.(G).Z is V", "+ C");
        failures += Expect("the join with a wildcard kid: an unrelated request", JoinWild, "T is Copy", "none C");
        failures += Expect("a shape variable at two positions", "contract Copy\ncontract W\n  associate X\ncontract Q\n  associate Z\ncontract K<A>\n  clause Self.(W).X is Pair<A, A>\ncontract J<A>\n  associate Y is A\ncontract R\n  clause Self.(Q).Z is Pair<Self.(J<i32>).Y, i32>\ninput T is K<i32>\ninput T is W\ninput U is R\ninput U is Q\ninput U is J<i32>\ninput U.(J<i32>).Y is Copy\ninput i32 is Copy", "T.(W).X is U.(Q).Z", "+ C");
        failures += Expect("a round whose only effects are need rows does not settle (m41)", "contract Cp\ncontract K1\n  associate Y is Cp\n  clause Self is K2\n  clause Self.(K1).Y is K1\ncontract K2\n  associate X is i32\n  clause Self.(K2).X is Self\ninput T is K1", "T is Cp", "+ C");

        // Round 5 (rule 5): need rows are tagged effects of the round, so a round whose only effects are need rows settles no
        // polarity (152dda62: none; seed 20261022 model 41 of the default profile, and its minimal form).
        const string M41 = """
            contract Cp
            contract K0
              associate X
              associate Y is Self.(K0).Y
            contract K1
              associate Y is Cp and K1
              clause Self is K2 and K4
            contract K2
              associate X is i32
              clause Self.(K2).X is Self
            contract K3
              associate X is List<i32>
              associate Y is K1 or K2
              clause Self is K3
            contract K4 : Cp pending
              associate Y
              clause Self is K0 and K1
            input T is K1
            input U is K0
            input U is T.(K1).Y
            """;
        failures += Expect("need rows are effects of the round (seed 20261022 model 41)", M41, "T is Cp", "+ P");
        failures += Expect("need rows are effects of the round (minimal)", "contract Cp\ncontract K1\n  associate Y is Cp\n  clause Self is K2\n  clause Self.(K1).Y is K1\ncontract K2\n  associate X is i32\n  clause Self.(K2).X is Self\ninput T is K1", "T is Cp", "+ C");

        // Round 5 (the pending-meeting deferral loses nothing): every identity fact about a promised Type arrives through a
        // pattern with free kids (a conformance subject F<V>, a chain's own pattern), which agrees without meets (SPEC 8.2.3 gives
        // F<a> ≡ F<b> without a ≡ b).
        const string KidMeet = """
            contract Cp<A>
            contract K1
              associate X is F<Self>
            contract K2<A>
              clause Self is F<A>
            contract Q<A>
              clause F<A> is Cp<A>
            conformance F<V> is K2<i32>
            input T is K1
            input S is Q<i32>
            """;
        failures += Expect("kid meeting deferred: identity through a conformance subject", KidMeet, "T.(K1).X is Cp<i32>", "+ C");
        failures += Expect("kid meeting deferred: identity goal through a conformance subject", "contract K1\n  associate X is F<Self>\ncontract K2<A>\n  clause Self is F<A>\ncontract K3\n  associate Y is F<i32>\nconformance F<V> is K2<i32>\ninput T is K1\ninput U is K3", "T.(K1).X is F<i32>", "+ C");
        failures += Expect("kid meeting deferred: identity by an input on the compound", "contract Cp<A>\ncontract K1\n  associate X is F<Self>\ncontract Q<A>\n  clause F<A> is Cp<A>\ninput T is K1\ninput F<T> is F<i32>\ninput S is Q<i32>", "T.(K1).X is Cp<i32>", "+ C");
        failures += Expect("kid meeting deferred: identity through a parameter side", KidMeet.Replace("clause Self is F<A>", "clause Self is A").Replace("is K2<i32>", "is K2<F<i32>>"), "T.(K1).X is Cp<i32>", "+ C");
        failures += Expect("kid meeting deferred: identity goal through a parameter side", "contract K1\n  associate X is F<Self>\ncontract K2<A>\n  clause Self is A\ncontract K3\n  associate Y is F<i32>\nconformance F<V> is K2<F<i32>>\ninput T is K1\ninput U is K3", "T.(K1).X is F<i32>", "+ C");

        // proposal B (2026-10-22): structural decomposition
        // The identity structure decomposes constructed Types (B 3.3): same-head constructors in one class make their kids one class;
        // different heads are a structural conflict and a Type identical to its own proper part a cycle (B 3.6, 3.10: premise errors,
        // the reference's `!`, `+-` where a request reaches them); a negated identity holds when the structure refutes it, unbound
        // arguments allowed; projections and Contract arguments are never decomposed (B 3.5); a polarity established before a limit
        // stands (B 3.10). 152dda62: none on same, same-nested, refuted-open, number, decomp-down, conflict and cyclic.
        failures += Expect("B same: two inputs decomposed", "input T is Option<U>\ninput T is Option<i32>", "U is i32", "+ C");
        failures += Expect("B same-nested: decomposed two levels down", "input T is Option<List<U>>\ninput T is Option<List<i32>>", "U is i32", "+ C");
        failures += Expect("B conflict: different heads in one class, reached by the request", "input T is Option<U>\ninput T is Array<U>", "T is Option<U>", "+- C");
        failures += Expect("B refuted-open: different heads with an unbound argument", "input T is Array<V>", "T is Option<i32>", "- C");
        failures += Expect("B refuted-kid: a Refuted kid pair", "input T is Option<U>\ninput U is bool", "T is Option<i32>", "- C");
        const string Fill = """
            contract Flag
            conformance Slot<V> is Flag when V is Option<i32>
            input T is Slot<Array<W>>
            """;
        failures += Expect("B fill: a conditional conformance's condition Refuted by heads", Fill, "Array<W> is Option<i32>", "- C");
        failures += Expect("B fill: the conformance does not apply (vacuous today: identity conditions are outside C1's rows, Condition requires Is)", Fill, "T is Flag", "none C");
        failures += Expect("B independent: projections are not decomposed", "contract Source\n  associate Element\ninput T is Source\ninput U is Source\ninput T.(Source).Element is U.(Source).Element", "T is U", "none C");
        failures += Expect("B cyclic: a Type identical to its own proper part", "input T is Option<U>\ninput U is Option<T>", "T is Option<U>", "+- C");
        failures += Expect("B reconstructed: a projection of the Type itself is no cycle", "contract Linked\n  associate Next\nconformance Cell<V> is Linked\n  binding Next = V\ninput T is Linked\ninput T is Cell<T.(Linked).Next>", "T is Cell<T.(Linked).Next>", "+ C");
        failures += Expect("B number: an associated-Type specification decomposed against a binding", "contract Wrapped\n  associate Inner\n  associate Outer is Option<Self.(Wrapped).Inner>\nconformance Number is Wrapped\n  binding Outer = Option<i32>", "Number.(Wrapped).Inner is i32", "+ C");
        failures += Expect("B args-not-decomposed: Contract arguments are two conformances", "contract K<A>\ninput T is K<U>\ninput T is K<i32>", "U is i32", "none C");
        failures += Expect("B decomp-down: the demand on a kid class is answered by the parent's promise (rule 8 down)", "contract Copy\ncontract K<A>\n  clause Self is Option<Pair<A, i32>>\ncontract Holder\n  associate Item\nconformance Pair<V, W> is Copy when V is Copy\ninput T is K<U>\ninput T is Option<T.(Holder).Item>\ninput U is Copy", "T.(Holder).Item is Copy", "+ C");
        failures += Expect("B two-variable-kids: neither node exists before the rows fire (rule 8 up)", "contract K0\n  associate Y\ncontract K1<A>\n  clause Self.(K0).Y is Option<A>\ncontract K2<A>\n  clause Self.(K0).Y is Option<A>\ninput T is K0\ninput T is K1<U>\ninput T is K2<W>", "U is W", "+ C");
        failures += Expect("B ground-kid: a ground side and a variable kid (rule 8 up)", "contract K0\n  associate X\ncontract K1\n  clause Self.(K0).X is List<i32>\ncontract K2<A>\n  clause Self.(K0).X is List<A>\ninput T is K0\ninput T is K1\ninput T is K2<U>", "U is i32", "+ C");
        failures += Expect("B limit-keeps-proven: the polarity established before the item limit stands (seed 20261022 model 312)", LimitKeepsProven, "i32 is Cp", "+ R", limits: new Limits { Rounds = 300, Items = 3000 });
        failures += Expect("B conflict-reached-by-row: the row fires on need and the conflict is the request's", "contract K<A>\n  clause Self is Array<A>\ninput T is K<U>\ninput T is Option<U>", "T is Array<U>", "+- C");
        failures += ExpectLinear("B open-live: unrelated request, live constructor sides with a variable kid (rule 8 up)", OpenLive, "T is Copy", "none C");

        // The design review of the addendum (rounds 1–2): a negated component refutes the parent (B2); projection-headed kids stay
        // deferred (B1: `T.(K).Y ≡ U.(K).X` is an ordinary premise, so `$1.(K).X` against `$1.(K).Y` or a constructor decides nothing);
        // the structure need (B3: an identity goal's negative polarity requests every promise at its classes, so a projection side
        // refutes by its binding's head); the kid pair selects the negated row (round 2 B1).
        failures += Expect("B negated-component: a negated component refutes the parent", "input T is Option<U>\ninput U is not i32", "T is Option<i32>", "- C");
        const string ProjKids = """
            contract Cp
            contract K
              associate X
              associate Y
            contract Q<A>
              clause Self is Pair<A.(K).X, i32>
            contract R<A>
              clause Self is Pair<A.(K).Y, i32>
            input T is K
            input U is K
            input T.(K).Y is U.(K).X
            input V is Q<U>
            input W is R<T>
            input W is Cp
            """;
        failures += Expect("B proj-kids: projection-headed kids are deferred, not compared by head", ProjKids, "V is Cp", "+ C");
        failures += Expect("B proj-vs-con: a projection kid against a constructor kid is deferred", "contract Cp\ncontract K\n  associate X\n  associate Y\ncontract Q<A>\n  clause Self is Pair<A.(K).X, i32>\ncontract R\n  clause Self is Pair<List<i64>, i32>\ninput T is K\ninput U is K\ninput T.(K).Y is U.(K).X\ninput T.(K).Y is List<i64>\ninput V is Q<U>\ninput W is R\ninput W is Cp", "V is Cp", "+ C");
        failures += Expect("B projection-side: the negative polarity needs the projection's structure (the structure need)", "contract K\n  associate X is List<i32>\ninput T is K", "T.(K).X is Array<U>", "- C");
        failures += Expect("B negated-row-kid: the kid pair selects the negated row", "contract K\n  associate X\n  clause Self.(K).X is not i32\ninput T is K\ninput U is Option<T.(K).X>", "U is Option<i32>", "- C");
        // The final independent review (round 1): B1 a duplicate node owned a re-keyed key (w2b); B2 rule 8's kid heads reached no candidate
        // selection and decomposed promises never entered the join (w13a, w13b, w15); B3 a negated row whose other side the antecedent binds (w3).
        failures += Expect("B w2b-retire: a duplicate node leaves every list and owns no key (one node per Type)", "contract Cp\nconformance Pair<V, W> is Cp when V is Cp\ninput R is P\ninput Q2 is Q\ninput P is P2\ninput A is Pair<P, Q>\ninput B is Pair<P2, Q2>\ninput X1 is List<R>\ninput X2 is Option<R>\ninput X3 is Set<R>\ninput P is Cp", "A is Cp", "+ C");
        failures += Expect("B w13b-filter-ground: a row whose subject head the class gains by decomposition is a candidate", "contract Cp\ncontract K0\n  associate Y\ncontract K1\n  clause Self.(K0).Y is Option<List<i32>>\nconformance List<V> is Cp\ninput T is K0\ninput T is K1\ninput T.(K0).Y is Option<V>", "V is Cp", "+ C");
        failures += Expect("B w13a-filter-projkid: the same with a projection kid", "contract Cp\ncontract K0\n  associate Y\ncontract K9\n  associate Z\ncontract K1<A>\n  clause Self.(K0).Y is Option<List<A.(K9).Z>>\nconformance List<V> is Cp\ninput T is K0\ninput T is K1<U>\ninput U is K9\ninput T.(K0).Y is Option<V>", "V is Cp", "+ C");
        failures += Expect("B w15-join-only: decomposed promises enter the join and expand their producers", "contract Cp\ncontract K0\n  associate Y\ncontract K9\n  associate Z\ncontract K7\n  associate W is List<i32>\ncontract K1<A>\n  clause Self.(K0).Y is Option<List<A.(K9).Z>>\ninput T is K0\ninput T is K1<U>\ninput U is K9\ninput S is K7\ninput T.(K0).Y is Option<V>\ninput U.(K9).Z is i32\ninput S.(K7).W is Cp", "V is Cp", "+ C");
        failures += Expect("B w3-negated-param: a negated row whose other side the antecedent binds is requested", "contract K<A>\n  associate X\n  clause Self.(K).X is not A\ninput T is K<U>", "T.(K).X is U", "- C");
        return failures;
    }

    // The same request at n = 4, 8 and 64: the expected answer at each size, and V and J at most linear in n (the slope from 8 to
    // 64 no more than the slope from 4 to 8, plus one per n).
    private static int ExpectLinear(string name, Func<int, string> program, string query, string expected)
    {
        var sizes = new[] { 4, 8, 64 };
        var answers = sizes.Select(n =>
        {
            var m = ModelText.Parse(program(n));
            return new Solver(m).Supply(ModelText.ParseFormula(m, query), View.Ordinary);
        }).ToArray();
        var ok = true;
        foreach (var a in answers)
        {
            var verdict = a.Outcome == Outcome.Resource ? "R" : a.Verdict + " " + (a.Outcome == Outcome.Complete ? "C" : "P");
            ok &= verdict == expected;
        }

        ok &= Linear(answers.Select(a => a.V).ToArray()) && Linear(answers.Select(a => a.J).ToArray());
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | {query} | n=4: C {answers[0]} | n=8: C {answers[1]} | n=64: C {answers[2]} | expected {expected}, V and J linear");
        return ok ? 0 : 1;

        bool Linear(long[] x) => (x[2] - x[1]) * 4 <= (x[1] - x[0] + 4) * 56;
    }

    // The same request on the program at two sizes: the expected answer, and the same rounds, V, J and N (nothing unrelated is read);
    // with sameJ false, the same rows (rounds, V, N) only.
    private static int ExpectFlat(string name, Func<int, string> program, string query, string expected, bool sameJ = true)
    {
        var answers = new[] { 4, 64 }.Select(n =>
        {
            var m = ModelText.Parse(program(n));
            return new Solver(m).Supply(ModelText.ParseFormula(m, query), View.Ordinary);
        }).ToArray();
        var (a, b) = (answers[0], answers[1]);
        var ok = Verdict(a) == expected && (a.Verdict, a.Rounds, a.V, a.N) == (b.Verdict, b.Rounds, b.V, b.N) && (!sameJ || a.J == b.J);
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | {query} | n=4: C {a} | n=64: C {b} | expected {expected}, equal {(sameJ ? "work" : "rows")}");
        return ok ? 0 : 1;
    }

    private static int Expect(string name, string text, string query, string expected, View? view = null, Limits? limits = null)
    {
        var m = ModelText.Parse(text);
        var f = ModelText.ParseFormula(m, query);
        var a = new Solver(m).Supply(f, view ?? View.Ordinary, limits);
        var r6 = Ref(m, view ?? View.Ordinary, f, 6);
        var r8 = Ref(m, view ?? View.Ordinary, f, 8);
        var ok = Verdict(a) == expected;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | {query} | C {a} | ref6 {Show(r6)} ref8 {Show(r8)} | expected {expected}");
        return ok ? 0 : 1;
    }

    // The verdict as the fixed cases write it: the polarities, then C (Complete) or P (Provisional); at a limit R, after the
    // polarities established at the stop when there are any (B 3.10).
    private static string Verdict(Answer a) => a.Outcome == Outcome.Resource ? (a.Positive || a.Negative ? a.Verdict + " R" : "R") : a.Verdict + " " + (a.Outcome == Outcome.Complete ? "C" : "P");

    private static int ExpectEnumerate(string name, string text, string subject, string expected, bool discovery = false, string? expectFact = null, Limits? limits = null, int? facts = null)
    {
        var m = ModelText.Parse(text);
        var s = ModelText.ParseType(m, subject);
        var a = new Solver(m).Enumerate(s, View.Ordinary, limits, discovery);
        var verdict = a.Outcome == Outcome.Resource ? "R" : a.Outcome == Outcome.Complete ? "C" : "P";
        var ok = verdict == expected && (expectFact is null || a.Facts.Contains(expectFact)) && (facts is null || a.Facts.Count == facts);
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} | enumerate {subject} | C {a} facts [{string.Join(" ", a.Facts)}] members [{string.Join(" ", a.Members)}] | expected {expected}");
        return ok ? 0 : 1;
    }

    // The reference's answer; Error when its structure has a conflict or a cycle (B 3.10: a premise error of the model's declarations).
    private static (bool Pos, bool Neg, bool Overflow, bool Error) Ref(ProgramModel m, View view, Formula f, int bound, bool pending = false, HashSet<int>? bindings = null)
    {
        var r = new Reference(m, view, pending, bound, bindings);
        var seeds = new List<Term>();
        f.Terms(seeds);
        r.Run(seeds);
        var (p, n) = r.Answer(f);
        return (p, n, r.Overflow, r.Conflict || r.Cyclic);
    }

    private static string Show((bool Pos, bool Neg, bool Overflow, bool Error) r) => r.Overflow ? "overflow" : (r.Pos ? "+" : "") + (r.Neg ? "-" : "") + (!r.Pos && !r.Neg ? "none" : "") + (r.Error ? "!" : "");

    // ---------------------------------------------------------------- generated models
    private sealed class Stats
    {
        public int Models;
        public int Overflow;
        public int DeclarationErrors;
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
        public int Contracts;
        public int Recursive;
        public int ClassificationDiffers;
        public int Frozen;
        public readonly Dictionary<string, int> PositiveBy = new();
        public readonly List<long> V = new();
        public readonly List<long> E = new();
        public readonly List<long> J = new();
        public readonly List<long> N = new();
        public readonly List<long> Rounds = new();
        public int Derived;
        public int ResourceNone;

        public void Add(Stats o)
        {
            this.Models += o.Models;
            this.Overflow += o.Overflow;
            this.DeclarationErrors += o.DeclarationErrors;
            this.Queries += o.Queries;
            this.Derived += o.Derived;
            this.ResourceNone += o.ResourceNone;
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
            this.Contracts += o.Contracts;
            this.Recursive += o.Recursive;
            this.ClassificationDiffers += o.ClassificationDiffers;
            this.Frozen += o.Frozen;
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
            if (Environment.GetEnvironmentVariable("G170_PROGRESS") is not null)
            {
                lock (LogLock)
                {
                    Console.Error.WriteLine($"start {i} at {DateTime.Now:HH:mm:ss} heap {GC.GetTotalMemory(false) >> 20} MB");
                }
            }

            results[i] = OneModel(i, cases[i].Text, cases[i].Queries, cases[i].Subjects);
            if (Environment.GetEnvironmentVariable("G170_PROGRESS") is not null)
            {
                lock (LogLock)
                {
                    Console.Error.WriteLine($"end {i} {sw.Elapsed.TotalSeconds:F1} s");
                }
            }
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
                if (!line.StartsWith("RESOURCE", StringComparison.Ordinal) && !line.StartsWith("ENUM-RESOURCE", StringComparison.Ordinal) || printed++ < 12)
                {
                    Console.WriteLine(line);
                }
            }
        }

        Console.WriteLine($"== RANDOM seed {seed}: {clock.Elapsed.TotalSeconds:F0} s; models {st.Models} (reference overflow {st.Overflow}, declaration errors {st.DeclarationErrors}); Supply queries {st.Queries} ({st.Derived} of them reference-derived facts, {st.Frozen} in a frozen view): stable {st.Stable} " +
            $"(agree {st.Agree}, disagree {st.Disagree}, C Resource {st.ResourceStable}), unstable {st.Unstable} (C Resource {st.ResourceUnstable}), provisional {st.Provisional}");
        Console.WriteLine($"   C Resource on stable queries: {st.ResourceStable}, of which the reference has evidence for neither polarity {st.ResourceNone}");
        Console.WriteLine($"   answer kinds on stable agreement: {string.Join(", ", st.PositiveBy.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))}");
        Console.WriteLine($"   Enumerate: {st.Enumerations}: agree {st.EnumAgree}, disagree {st.EnumDisagree}, C Resource {st.EnumResource}, unstable {st.EnumUnstable}");
        Console.WriteLine($"   recursive classification (Model's declaration search vs the reference's bounded SPEC 8.3.3 closure): {st.Contracts} Contracts, {st.Recursive} recursive, {st.ClassificationDiffers} differ");
        Console.WriteLine($"   cost per stable request: V {Dist(st.V)}; E {Dist(st.E)}; J {Dist(st.J)}; N {Dist(st.N)}; rounds {Dist(st.Rounds)}");
        return st.Disagree + st.EnumDisagree;
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
            views.Add(new View("frozen 0", inferred.Where(b => b.Definition == 0).Select(b => b.Id)));
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

            if (r1.Conflict || r1.Cyclic || r2.Conflict || r2.Cyclic)
            {
                // B 3.10: a structural conflict or cycle is a premise error reported at the declaration, never a request's answer:
                // the model's requests are not compared (counted once per model).
                if (st.DeclarationErrors == 0)
                {
                    st.DeclarationErrors = 1;
                    log.Add($"DECLARATION-ERROR model {i} view {view.Name}: {(r1.Conflict || r2.Conflict ? "conflict" : "cyclic")}{nl}{text}");
                }

                continue;
            }

            if (view == View.Ordinary)
            {
                st.Contracts += m.Decls.Count;
                st.Recursive += m.Decls.Count(d => d.Recursive);
                var differs = m.Decls.Where(d => d.Recursive != r1.Recursive.Contains(d)).ToList();
                st.ClassificationDiffers += differs.Count;
                if (differs.Count != 0)
                {
                    log.Add($"CLASSIFICATION model {i}: {string.Join(", ", differs.Select(d => d.Name + (d.Recursive ? " (Model only)" : " (reference only)")))}{nl}{text}");
                }
            }

            // Reference-derived queries: facts the bounded reference found on the query subjects (positive-biased).
            var derived = querySubjects.SelectMany(s => r1.FactsOn(s).Take(3).Select(x => x.Positive ? new Formula(FK.Is, s, x.Ref) : new Formula(FK.Not, l: new Formula(FK.Is, s, x.Ref)))).Take(8).ToList();
            st.Derived += derived.Count;
            var solver = new Solver(m);
            foreach (var f in formulas.Concat(derived))
            {
                st.Queries++;
                st.Frozen += view == View.Ordinary ? 0 : 1;
                var a = solver.Supply(f, view, Harness);
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
            }

            if (view != View.Ordinary)
            {
                continue;
            }

            foreach (var s in subjectTerms)
            {
                st.Enumerations++;
                var a = solver.Enumerate(s, view, Harness);
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
        var two = Environment.GetEnvironmentVariable("G170_REVIEW_PROFILE") == "params";
        var nested = Environment.GetEnvironmentVariable("G170_REVIEW_PROFILE") == "nested";
        var structural = Environment.GetEnvironmentVariable("G170_REVIEW_PROFILE") == "structural";
        var arity = names.Select(_ => two ? r.Next(3) : (r.Next(4) == 0 ? 1 : 0)).ToArray();
        var assocs = names.Select(_ => new[] { "X", "Y" }.Where(_ => r.Next(3) != 0).ToArray()).ToArray();
        var constructed = new HashSet<(int Contract, string Assoc)>(); // associates whose declared requirement is a constructed Type
        var usedOr = false;
        string Pick(params string[] xs) => xs[r.Next(xs.Length)];
        string Own(int i) => arity[i] == 2 ? names[i] + "<A, B>" : arity[i] == 1 ? names[i] + "<A>" : names[i];
        string Ref(int j, int ctx) => arity[j] == 0 ? names[j] : arity[j] == 2 ? names[j] + "<" + Arg(ctx) + ", " + Arg(ctx) + ">" : names[j] + "<" + Arg(ctx) + ">";
        string Arg(int ctx)
        {
            if (ctx < 0)
            {
                return Pick("i32", "i64", "U", "List<i32>");
            }

            var options = new List<string> { "i32", "Self", "List<i32>" };
            if (arity[ctx] >= 1)
            {
                options.Add("A");
                options.Add("List<A>");
            }

            if (arity[ctx] == 2)
            {
                options.Add("B");
                options.Add("List<B>");
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

                        if (ctx >= 0 && arity[ctx] >= 1)
                        {
                            options.Add(arity[ctx] == 2 ? Pick("A", "B") : "A");
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
                    var requirement = Requirement(i);
                    if (requirement.StartsWith("i32", StringComparison.Ordinal) || requirement.StartsWith("List<", StringComparison.Ordinal))
                    {
                        constructed.Add((i, z));
                    }

                    sb.Append(" is ").Append(requirement);
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

                if (arity[i] >= 1)
                {
                    subjectOptions.Add(arity[i] == 2 ? Pick("A", "B") : "A");
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

            // The nested profile (R3-N3): a nested projection of Self is Self, or names a Contract with Self as an argument (a bound
            // pattern), or is a projection of another Contract's nested projection.
            if (nested && assocs[i].Length != 0)
            {
                var z = assocs[i][r.Next(assocs[i].Length)];
                var path = "Self.(" + Own(i) + ")." + z + ".(" + Own(i) + ")." + z;
                var j = r.Next(n);
                sb.Append("  clause ").Append(path).Append(" is ").Append(Pick("Self", Ref(j, i), assocs[j].Length != 0 ? "Self.(" + Ref(j, i) + ")." + assocs[j][0] : "Self")).Append('\n');
            }
        }

        var inputs = new List<string>();
        var tContract = r.Next(n);
        if (nested && assocs[tContract].Length == 0 && Enumerable.Range(0, n).Where(i => assocs[i].Length != 0).ToList() is { Count: > 0 } withAssocs)
        {
            tContract = withAssocs[r.Next(withAssocs.Count)]; // the nested profile needs T's projection
        }
        var tRef = Ref(tContract, -1);
        inputs.Add("T is " + tRef);
        if (r.Next(2) == 0)
        {
            inputs.Add("U is " + Ref(r.Next(n), -1));
        }

        var tAssoc = assocs[tContract].Length != 0 ? assocs[tContract][r.Next(assocs[tContract].Length)] : null;
        string? tProjection = tAssoc is null ? null : "T.(" + tRef + ")." + tAssoc;
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

        // Compound subjects (this revision): a fact on a compound Type and an identity with one, so congruence through kids is
        // exercised.
        if (r.Next(100) < 15)
        {
            inputs.Add(Pick("List<i32>", "List<U>") + " is " + Pick("Cp", names[r.Next(n)] is var z1 && arity[Array.IndexOf(names, z1)] == 0 ? z1 : "Cp"));
        }

        if (tProjection is not null && r.Next(100) < 10)
        {
            inputs.Add("U is List<" + tProjection + ">");
        }

        // The structural profile (proposal B): the T-projection's other side may be a constructed Type (one per projection), emitted only
        // when the projection's declared requirement is not itself a constructed Type (B 3.7: the model would be a declaration error),
        // so decomposition, structural Refuted and conflicts occur. The other profiles draw nothing here (their seeds stay comparable).
        if (structural && tProjection is not null && !constructed.Contains((tContract, tAssoc!)))
        {
            inputs.Add(tProjection + " is " + Pick("List<i32>", "List<U>", "Option<U>"));
        }

        // The nested profile: a second subject V of T's Contract whose projection is the same Type as T's (both i32), so nested
        // projections of T and V are one Type; V carries a fact.
        string? vProjection = null;
        if (nested && tProjection is not null)
        {
            vProjection = "V" + tProjection[1..];
            inputs.Add("V is " + tRef);
            inputs.Add(tProjection + " is i32");
            inputs.Add(vProjection + " is i32");
            inputs.Add("V is " + Pick("Cp", Ref(r.Next(n), -1)));
        }

        foreach (var input in inputs)
        {
            sb.Append("input ").Append(input).Append('\n');
        }

        string? s1Ref = null;
        var s1Contract = r.Next(n);
        if (r.Next(10) < 4)
        {
            s1Ref = arity[s1Contract] == 0 ? names[s1Contract] : arity[s1Contract] == 2 ? names[s1Contract] + "<" + Pick("i32", "i64") + ", " + Pick("i32", "List<i32>") + ">" : names[s1Contract] + "<" + Pick("i32", "i64") + ">";
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
            sb.Append("conformance S2 is ").Append(arity[j] == 0 ? names[j] : arity[j] == 2 ? names[j] + "<i32, i64>" : names[j] + "<i32>");
            if (r.Next(2) == 0)
            {
                var k = r.Next(n);
                sb.Append(" when S2 is ").Append(arity[k] == 0 ? names[k] : arity[k] == 2 ? "Cp" : names[k] + "<i32>");
            }

            sb.Append('\n');
        }

        subjects = new List<string> { "T", "U", "i32", "S1", "List<i32>" };
        if (tProjection is not null)
        {
            subjects.Add(tProjection);
            subjects.Add("List<" + tProjection + ">");
            var j = r.Next(n);
            if (assocs[j].Length != 0)
            {
                subjects.Add(tProjection + ".(" + Ref(j, -1) + ")." + assocs[j][r.Next(assocs[j].Length)]);
            }
        }

        if (s1Ref is not null && assocs[s1Contract].Length != 0)
        {
            subjects.Add("S1.(" + s1Ref + ")." + assocs[s1Contract][0]);
            subjects.Add("List<S1.(" + s1Ref + ")." + assocs[s1Contract][0] + ">");
        }

        if (vProjection is not null)
        {
            subjects.Add("V");
            subjects.Add(tProjection + tProjection![1..]); // T's nested projection, one Type with V's
        }

        queries = new List<string>();
        if (vProjection is not null)
        {
            queries.Add("T is V");
            queries.Add("T is Cp");
        }

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

        if (structural)
        {
            queries.Add("U is i32");
            queries.Add("U is List<i32>");
            if (tProjection is not null)
            {
                queries.Add(tProjection + " is List<U>");
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
    // Declaration order (templates and Contracts) shuffled; ordinary and frozen views. Each request is compared in full (verdict,
    // outcome, reason, rounds, V, E, J, N, peak items) at harness limits, then at its own need and need − 1 for each limit: the
    // limit outcome must be the same in both orders, Complete/Provisional at the need and Resource below it.
    private static int Order(int count, int seed)
    {
        var rnd = new Random(seed);
        var requests = 0;
        var boundaries = 0;
        var differences = 0;
        var unexpected = 0;
        for (var i = 0; i < count; i++)
        {
            var text = Generate(rnd, out var queries, out _);
            var m = ModelText.Parse(text);
            var shuffled = ModelText.Parse(text);
            var order = rnd;
            var templates = shuffled.Templates.OrderBy(_ => order.Next()).ToList();
            shuffled.Templates.Clear();
            shuffled.Templates.AddRange(templates);
            var decls = shuffled.Decls.OrderBy(_ => order.Next()).ToList();
            shuffled.Decls.Clear();
            shuffled.Decls.AddRange(decls);
            var a = new Solver(m);
            var b = new Solver(shuffled);
            var views = new List<View> { View.Ordinary };
            if (m.Bindings.Values.Any(x => x.Inferred))
            {
                views.Add(new View("frozen 0", m.Bindings.Values.Where(x => x.Inferred && x.Definition == 0).Select(x => x.Id)));
            }

            foreach (var q in queries.Take(4))
            {
                foreach (var view in views)
                {
                    requests++;
                    var x = a.Supply(ModelText.ParseFormula(m, q), view, Harness);
                    var y = b.Supply(ModelText.ParseFormula(shuffled, q), view, Harness);
                    differences += Compare(i, q, view, "harness", x, y);
                    if (x.Outcome == Outcome.Resource)
                    {
                        continue;
                    }

                    var checks = new List<(string Name, Limits Limits, bool Below)>
                    {
                        ("items=need", new Limits { Rounds = Harness.Rounds, Items = x.PeakItems }, false),
                        ("items=need-1", new Limits { Rounds = Harness.Rounds, Items = x.PeakItems - 1 }, true),
                    };
                    if (x.Rounds > 0)
                    {
                        checks.Add(("rounds=need", new Limits { Rounds = x.Rounds, Items = Harness.Items }, false));
                        checks.Add(("rounds=need-1", new Limits { Rounds = x.Rounds - 1, Items = Harness.Items }, true));
                    }

                    foreach (var (name, limits, below) in checks)
                    {
                        boundaries++;
                        var xl = a.Supply(ModelText.ParseFormula(m, q), view, limits);
                        var yl = b.Supply(ModelText.ParseFormula(shuffled, q), view, limits);
                        differences += Compare(i, q, view, name, xl, yl);
                        if ((xl.Outcome == Outcome.Resource) != below)
                        {
                            unexpected++;
                            if (unexpected < 6)
                            {
                                Console.WriteLine($"BOUNDARY model {i}: {q} view {view.Name} {name}: {xl} (harness {x})");
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine($"== ORDER: {requests} requests on {count} models (templates and Contracts shuffled; ordinary and frozen views) and {boundaries} limit-boundary runs: {differences} differ in verdict, outcome, reason, rounds, V, E, J, N or peak items; {unexpected} boundary outcomes other than Complete/Provisional at the need and Resource below it");

        // Models where a merge happens in every round and a chain is stored before a merge (the join's J and the stored promises'
        // re-keying must be order-free): the unbounded enumeration at a small item limit, G170 depth 8, nested 39 at 3,000 items.
        var fixedDifferences = 0;
        var fixedRuns = 0;
        var fixedModels = 0;
        foreach (var (name, text, request, subject, limits) in new (string, string, string?, string?, Limits)[]
        {
            ("unbounded enumeration, 300 items", UnboundedText, null, "i32", new Limits { Items = 300 }),
            ("G170 depth 8", Trees + "\ninput T is CopyTree", Chain(8) + " is Copy", null, Harness),
            ("nested 39 at 3,000 items", Nested39, "T is V", null, new Limits { Rounds = 300, Items = 3000 }),
            ("nested 39 at 300 items", Nested39, "T is V", null, new Limits { Rounds = 300, Items = 300 }),
            ("fired set re-keyed when facts become one (structural seed 4242 model 37)", FiredRekey, "T.(K1<i32>).Y is K2<U>", null, Harness),
        })
        {
            var m = ModelText.Parse(text);
            fixedModels++;
            var x = request is null ? new Solver(m).Enumerate(ModelText.ParseType(m, subject!), View.Ordinary, limits) : new Solver(m).Supply(ModelText.ParseFormula(m, request), View.Ordinary, limits);
            for (var k = 0; k < 6; k++)
            {
                var shuffled = ModelText.Parse(text);
                var order = new Random(seed + 100 + k);
                var templates = shuffled.Templates.OrderBy(_ => order.Next()).ToList();
                shuffled.Templates.Clear();
                shuffled.Templates.AddRange(templates);
                var decls = shuffled.Decls.OrderBy(_ => order.Next()).ToList();
                shuffled.Decls.Clear();
                shuffled.Decls.AddRange(decls);
                var y = request is null ? new Solver(shuffled).Enumerate(ModelText.ParseType(shuffled, subject!), View.Ordinary, limits) : new Solver(shuffled).Supply(ModelText.ParseFormula(shuffled, request), View.Ordinary, limits);
                fixedRuns++;
                fixedDifferences += Compare(-1, name, View.Ordinary, $"shuffle {k}", x, y);
            }

            Console.WriteLine($"   {name}: {x} peak {x.PeakItems} [{x.Detail}]");
        }

        Console.WriteLine($"== ORDER (merges every round): {fixedRuns} shuffled runs of {fixedModels} fixed requests: {fixedDifferences} differ");
        return differences + unexpected + fixedDifferences;

        int Compare(int model, string q, View view, string setting, Answer x, Answer y)
        {
            if ((x.Verdict, x.Outcome, x.Reason, x.Rounds, x.V, x.E, x.J, x.N, x.PeakItems) == (y.Verdict, y.Outcome, y.Reason, y.Rounds, y.V, y.E, y.J, y.N, y.PeakItems))
            {
                return 0;
            }

            Console.WriteLine($"ORDER model {model}: {q} view {view.Name} {setting}: {x} peak {x.PeakItems} [{x.Detail}] vs {y} peak {y.PeakItems} [{y.Detail}]");
            return 1;
        }
    }

    // ---------------------------------------------------------------- resource boundary and transport
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
            var items = free.PeakItems;
            var atRounds = new Solver(m).Supply(f, View.Ordinary, new Limits { Rounds = rounds });
            var belowRounds = new Solver(m).Supply(f, View.Ordinary, new Limits { Rounds = rounds - 1 });
            var atItems = new Solver(m).Supply(f, View.Ordinary, new Limits { Items = items });
            var belowItems = new Solver(m).Supply(f, View.Ordinary, new Limits { Items = items - 1 });
            var ok = atRounds.Outcome == Outcome.Complete && belowRounds.Outcome == Outcome.Resource && atItems.Outcome == Outcome.Complete && belowItems.Outcome == Outcome.Resource;
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} G170 depth {d}: needs rounds {rounds}, peak items {items}; limit=need {atRounds.Verdict}/{atRounds.Outcome}, need-1 rounds {belowRounds.Outcome} ({belowRounds.Reason}), need-1 items {belowItems.Outcome} ({belowItems.Reason})");
        }

        // An unbounded search ends in Resource at every limit: enumerating a closed Type's infinite class.
        var it = UnboundedModel();
        foreach (var items in new[] { 1_000, 3_000, 10_000 })
        {
            var sw = Stopwatch.StartNew();
            var a = new Solver(it).Enumerate(ModelText.ParseType(it, "i32"), View.Ordinary, new Limits { Items = items });
            Console.WriteLine($"{(a.Outcome == Outcome.Resource ? "PASS" : "FAIL")} unbounded enumeration, item limit {items}: {a} ({sw.Elapsed.TotalSeconds:F1} s)");
            failures += a.Outcome == Outcome.Resource ? 0 : 1;
        }

        // B 3.10: a polarity established before the limit stands: the judgment is Proven, with the limit's record and no Unproven.
        var kept = ModelText.Parse(LimitKeepsProven);
        var checker = new Checker(kept, new Limits { Rounds = 300, Items = 3000 });
        var proof = checker.Require("limit-keeps-proven", ModelText.ParseFormula(kept, "i32 is Cp"));
        var keptOk = proof == Proof.Proven && checker.Records.Count == 1 && checker.Records[0].StartsWith("Resource(limit-keeps-proven", StringComparison.Ordinal);
        failures += keptOk ? 0 : 1;
        Console.WriteLine($"{(keptOk ? "PASS" : "FAIL")} limit keeps proven (seed 20261022 model 312, i32 is Cp at 3,000 items): {proof}; records [{string.Join(" ", checker.Records)}]");

        failures += Transport();
        return failures;
    }

    private const string UnboundedText = """
        contract Dd<A>
        contract It
          associate Child is It
          associate Value
          clause Self.(It).Value is i32
          clause Self.(It).Value is Dd<Self>
        input T is It
        """;

    /// <summary>The nested profile's model 39 (seed 77; review-assets/round4/models/n39.txt): chain promises multiply with
    /// nested-projection identity rows.</summary>
    // Two facts that become one when their classes merge (`i32 is K0<List<i32>>`, `U is K0<i32>` under U = List<i32> = i32): a
    // subscription that fired on one of them fires again on the other unless its fired set follows the facts' union.
    private const string FiredRekey = """
        contract Cp
        contract K0<A> : Cp
          associate X is K4
          associate Y is Cp and Cp
          clause Self.(K0<A>).X is List<i32>
          clause A is Self.(K0<A>).X
        contract K1<A> : K0<i32>
          associate X is K2<List<i32>>
          associate Y is Self
          clause A is K0<List<i32>>
        contract K2<A>
          associate X is K3
          associate Y is Cp or K3
          clause A.(K2<i32>).X is Cp or Cp
          clause Self.(K2<A>).Y.(K4).Y is K2<Self.(K2<A>).X>
        contract K3
          associate X is K3 and Cp
          associate Y is Self.(K3).X
        contract K4 pending
          associate Y
          clause Self.(K4).Y is Cp or Cp
        input T is K1<i32>
        input U is T.(K1<i32>).Y
        input T.(K1<i32>).Y is List<i32>
        conformance S2 is K4 when S2 is K1<i32>
        """;

    private const string Nested39 = """
        contract Cp
        contract K0 : Cp
          associate X is K3
          associate Y
          clause Self.(K0).X.(K0).Y is K2 or Cp
          clause Self.(K0).X.(K0).X is Self
        contract K1
          associate X
          associate Y
          clause Self.(K1).X is List<i32>
          clause Self.(K1).X.(K1).X is Self.(K0).X
        contract K2
          associate X
          clause Self.(K2).X is K0 or Cp
          clause Self.(K2).X.(K2).X is Self.(K1).X
        contract K3 : K0, Cp
          associate Y
          clause Self is Cp
          clause Self.(K3).Y.(K3).Y is Self.(K0).X
        input T is K3
        input U is K3
        input V is K3
        input T.(K3).Y is i32
        input V.(K3).Y is i32
        input V is K1
        """;

    private static ProgramModel UnboundedModel() => ModelText.Parse(UnboundedText);

    // Transport: a conformance whose own check hits the limit publishes Resource; judgments that read it (alone, under and/or,
    // through a condition, through its binding) are Resource and report nothing; independent judgments report as usual. Judged
    // first with the conformance Pending (not final: restarted after publication) and again with the check first; the records
    // must be the same. A control publishes Valid at the default limit.
    private static int Transport()
    {
        const string Text = """
            contract Copy
            contract Tree
              associate Child is Tree
            contract CopyTree : Tree, Copy
              clause Self.(Tree).Child is CopyTree
            contract Deep
              associate D
              clause Self.(Deep).D.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child is Copy
            contract Bb
            input T is CopyTree
            conformance S1 is Deep pending
              binding D = T
            conformance S2 is Bb when S1 is Deep
            """;
        var uses = new (string Scope, string Requirement)[]
        {
            ("a: S1 is Deep", "S1 is Deep"),
            ("b: and with an unprovable one", "S1 is Deep and U is Copy"),
            ("c: or with a proven one", "S1 is Deep or T is Copy"),
            ("d: a condition (S2 is Bb when S1 is Deep)", "S2 is Bb"),
            ("e: through its binding (S1.(Deep).D is Copy)", "S1.(Deep).D is Copy"),
            ("f: independent, unprovable", "U is Copy"),
            ("g: independent, proven", "T is Copy"),
        };
        var failures = 0;
        foreach (var (label, limits, expected) in new[]
        {
            ("check at a 6-round limit", new Limits { Rounds = 6 }, "Resource(check S1 is Deep: rounds>6) Unproven(f: independent, unprovable)"),
            ("check at the default limit", new Limits(), "Unproven(b: and with an unprovable one) Unproven(f: independent, unprovable)"),
        })
        {
            var outcomes = new List<string>();
            foreach (var checkFirst in new[] { false, true })
            {
                var m = ModelText.Parse(Text);
                var checker = new Checker(m);
                var pending = new List<(string Scope, string Requirement)>();
                if (checkFirst)
                {
                    checker.Verify(m.Conformances[0], limits);
                }

                foreach (var (scope, requirement) in uses)
                {
                    if (checker.Require(scope, ModelText.ParseFormula(m, requirement)) is null)
                    {
                        pending.Add((scope, requirement)); // read a Pending source: judged again after publication
                    }
                }

                if (!checkFirst)
                {
                    checker.Verify(m.Conformances[0], limits);
                }

                var proofs = new List<string>();
                foreach (var (scope, requirement) in pending)
                {
                    proofs.Add(scope[..1] + "=" + checker.Require(scope, ModelText.ParseFormula(m, requirement)));
                }

                var records = string.Join(" ", checker.Records.OrderBy(x => x, StringComparer.Ordinal));
                outcomes.Add(records);
                var ok = records == expected;
                failures += ok ? 0 : 1;
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")} transport, {label}, {(checkFirst ? "check first" : "uses first, restarted")}: S1 {m.Conformances[0].Status}; restarted [{string.Join(" ", proofs)}]; records [{records}]");
            }

            if (outcomes[0] != outcomes[1])
            {
                failures++;
                Console.WriteLine($"FAIL transport, {label}: records depend on checking order");
            }
        }

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

        // Program-size scaling (the review's B3 workload): n conformances binding X = i32. Unrelated requests must not grow with
        // n; related ones grow only with the rows they can use.
        foreach (var n in new[] { 10, 100, 400, 1600 })
        {
            var many = ModelText.Parse(Many(n));
            foreach (var q in new[] { "List<i32> is Copy", "T.(Holder).Item is Copy", "i32 is Copy", "S1.(Pp).X is Copy", "i32 is Pp" })
            {
                Measure($"scaling n={n}: {q}", many, q, timed: n <= 400);
            }
        }

        // The round-2 review's workloads (R2-B1): identity sides that are variables or mention the subject, in n unrelated Contracts.
        foreach (var n in new[] { 10, 400, 1600 })
        {
            var selfId = ModelText.Parse(SelfId(n));
            var open = ModelText.Parse(Open(n));
            var related = ModelText.Parse(SelfId(n) + "input T is W3\n");
            var selfIdV = ModelText.Parse(SelfId(n) + "input V is Copy\n");
            var palias = ModelText.Parse(PAlias(n));
            var deep2 = ModelText.Parse(Deep2(n));
            foreach (var (name, m, q) in new[]
            {
                ("selfid", selfId, "T is Copy"), ("selfid", selfId, "T.(Holder).Item is Copy"), ("open", open, "List<T> is Copy"), ("open", open, "U.(W1).Y is Option<List<U>>"),
                ("selfid + T is W3", related, "T.(W3).X is T"), ("selfid + T is W3", related, "T.(W3).X.(Holder).Item is Copy"), ("selfid + V is Copy", selfIdV, "T is Copy"),
                ("palias", palias, "T is Copy"), ("palias", palias, "T.(K3<i32>).X is i32"), ("deep2", deep2, "T is Copy"), ("deep2", deep2, "T.(Holder).Item is Copy"),
            })
            {
                Measure($"scaling n={n}: {name} {q}", m, q);
            }
        }

        // The round-4 review's workloads (R4-B1): n Contracts fixing one associated Type (shared: dead rows; shared-pos: two live
        // through one subject; shared-live: every row live), and the mix of selfid and palias Contracts.
        foreach (var n in new[] { 10, 20, 40, 100, 400, 1600 })
        {
            Measure($"scaling n={n}: shared T is Copy", ModelText.Parse(Shared(n)), "T is Copy");
        }

        foreach (var n in new[] { 10, 400, 1600 })
        {
            Measure($"scaling n={n}: shared-pos W is Copy", ModelText.Parse(SharedPos(n)), "W is Copy");
            var mix = ModelText.Parse(Mix(n));
            Measure($"scaling n={n}: mix T is Copy", mix, "T is Copy");
            Measure($"scaling n={n}: mix T.(W3).X is T", mix, "T.(W3).X is T");
        }

        foreach (var n in new[] { 10, 40, 100, 400 })
        {
            Measure($"scaling n={n}: shared-live T is Copy", ModelText.Parse(SharedLive(n)), "T is Copy", timed: n <= 100);
        }

        // Proposal B's open-live witness (rule 8 up): n live rows with a variable constructor kid, requested once per needing class.
        foreach (var n in new[] { 10, 40, 100, 400 })
        {
            Measure($"scaling n={n}: open-live T is Copy", ModelText.Parse(OpenLive(n)), "T is Copy");
        }

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

    private static void Measure(string name, ProgramModel m, string query, bool timed = true)
    {
        var solver = new Solver(m);
        var f = ModelText.ParseFormula(m, query);
        Answer a = solver.Supply(f, View.Ordinary);
        if (!timed)
        {
            Console.WriteLine($"{name}: {a} peak {a.PeakItems}");
            return;
        }

        for (var i = 0; i < 4; i++)
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
        Console.WriteLine($"{name}: {a} peak {a.PeakItems}; {times[times.Count / 2]:F0} us, {bytes} B");
    }
}
