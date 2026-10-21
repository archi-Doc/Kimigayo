// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// G170 C1 real-source adapter (BU planning session, 2026-10-21): evidence, not a regression; kept as data under
// draft/Sketches/G170/C1/adapter/ and never part of the test project. It is compiled into a scratch copy of the test assembly
// together with the prototype's Model.cs, EGraph.cs, Solver.cs and Reference.cs, and runs C against real bound programs:
// - BindingSource reads Binding's existing facts and declaration clauses (a scope chain's DirectFacts, each Contract
//   declaration's ancestors and ClauseStorage, verified conformance paths with their associated bindings and inference
//   provenance) and translates them, read-only, into the prototype's model. It never writes Binding state (no fact, environment,
//   cache or flag); it interns nothing in TyCtxt.
// - The answers are compared with Binding's current supply (ProveCopy, the program's Binding issues) and with the bounded reference.
// Run: G170_C1_OUT=<file> xUnitTest.exe -method XunitTest.G170C1AdapterTest.Probe
using System.Diagnostics;
using System.Text;
using G170C1;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class G170C1AdapterTest
{
    private const string Trees = "contract Tree\n    associate Child is Tree\n\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n\n";
    private const string Tail = "\npublic func main() -> ()\n    ()\n";

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("G170_C1_OUT") is not { } path)
        {
            return;
        }

        var o = new StringBuilder();
        G170(o);
        NonRecursive(o);
        G180(o);
        Views(o);
        Cost(o);
        if (Environment.GetEnvironmentVariable("G170_C1_CORPUS") is { } corpus)
        {
            Corpus(o, corpus);
        }

        File.WriteAllText(path, o.ToString());
    }

    // Every function scope with input premises in the milestone programs and the Kimi library: C over the scope's DirectFacts and
    // all Contract clauses, for each input fact, each input subject's enumeration and the Copy of each abstract parameter Type.
    private static void Corpus(StringBuilder o, string directory)
    {
        o.Append("== Corpus: every function scope with input premises (milestones and the Kimi library)\n");
        var outcomes = new Dictionary<string, int>();
        var copy = new Dictionary<string, int>();
        var v = new List<long>();
        var e = new List<long>();
        var j = new List<long>();
        var scopes = 0;
        var requests = 0;
        var clock = Stopwatch.StartNew();
        var bindTime = TimeSpan.Zero;
        var solveTime = TimeSpan.Zero;
        long solveBytes = 0;
        var library = false;
        foreach (var file in Directory.GetFiles(directory, "Milestone*.kimi").OrderBy(x => x, StringComparer.Ordinal))
        {
            var c = Bind(File.ReadAllText(file));
            var bindClock = Stopwatch.StartNew();
            c.Bind(); // a warm rebind: today's eager supply included
            bindTime += bindClock.Elapsed;
            var roots = library ? new[] { c.Kotonoha.RootKoto } : new[] { c.Kotonoha.RootKoto, c.Binding.Library.Kotonoha.RootKoto };
            library = true;
            var source = new BindingSource(c);
            foreach (var root in roots)
            {
                foreach (var f in Walk(root).OfType<FunctionKoto>().ToArray())
                {
                    var scoped = new BindingSource(c);
                    scoped.Inputs(f);
                    if (scoped.Model.Inputs.Count == 0)
                    {
                        continue;
                    }

                    scopes++;
                    var model = scoped.Finish();
                    var solver = new Solver(model);
                    var solveClock = new Stopwatch();
                    Answer Timed(Func<Answer> request)
                    {
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        solveClock.Restart();
                        var answer = request();
                        solveTime += solveClock.Elapsed;
                        solveBytes += GC.GetAllocatedBytesForCurrentThread() - before;
                        return answer;
                    }

                    void Count(Answer a, string kind)
                    {
                        requests++;
                        var key = kind + " " + a.Outcome + (a.Outcome == Outcome.Resource ? " " + a.Reason : string.Empty);
                        outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
                        v.Add(a.V);
                        e.Add(a.E);
                        j.Add(a.J);
                        if (a.Outcome == Outcome.Resource)
                        {
                            o.Append($"  RESOURCE in {Path.GetFileName(file)} {f.Name}: {kind} {a}\n");
                        }
                    }

                    foreach (var input in model.Inputs.Where(x => x.IsAtom))
                    {
                        Count(Timed(() => solver.Supply(input, View.Ordinary, new Limits { Rounds = 2000, Size = 100_000 })), "input");
                    }

                    foreach (var subject in model.Inputs.Where(x => x.IsAtom).Select(x => x.Subject).Distinct())
                    {
                        Count(Timed(() => solver.Enumerate(subject, View.Ordinary, new Limits { Rounds = 2000, Size = 100_000 })), "enumerate");
                    }

                    foreach (var parameter in f.Parameters)
                    {
                        if (parameter.Type.TypeOf() is not { } t)
                        {
                            continue;
                        }

                        t = t.Kind == BoundTypeKind.Semantics && t.Components.Count == 1 ? t.Components[0] : t;
                        if (t.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection))
                        {
                            continue;
                        }

                        var goal = new Formula(FK.Is, scoped.Type(t), scoped.Ref(c.Binding.Library.Copy, []));
                        var a = Timed(() => solver.Supply(goal, View.Ordinary, new Limits { Rounds = 2000, Size = 100_000 }));
                        Count(a, "copy");
                        var today = c.Binding.ProveCopy(t, f);
                        var key = $"today {today} / C {(a.Outcome == Outcome.Resource ? "Resource" : a.Positive ? "evidence" : "no evidence")}";
                        copy[key] = copy.GetValueOrDefault(key) + 1;
                        if (today == ConstraintProof.Proven && !a.Positive)
                        {
                            o.Append($"  COPY today Proven, C without evidence: {Path.GetFileName(file)} {f.Name} {Binding.HoverTypeName(t)} inputs [{string.Join("; ", model.Inputs)}]\n");
                        }
                    }
                }
            }
        }

        string Dist(List<long> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s.Count == 0 ? "-" : $"median {s[s.Count / 2]}, p95 {s[(int)(s.Count * 0.95)]}, max {s[^1]}";
        }

        o.Append($"scopes {scopes}, requests {requests}, {clock.Elapsed.TotalSeconds:F1} s in all; warm rebinds of the 44 programs {bindTime.TotalMilliseconds:F0} ms; the {requests} C requests {solveTime.TotalMilliseconds:F0} ms, {solveBytes / 1024} KB\n");
        o.Append($"outcomes: {string.Join(", ", outcomes.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))}\n");
        o.Append($"Copy of abstract parameters: {string.Join(", ", copy.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))}\n");
        o.Append($"V {Dist(v)}; E {Dist(e)}; J {Dist(j)}\n");
    }

    private static string Value(int depth, string qualifier = "(Tree).") => "T" + string.Concat(Enumerable.Repeat("." + qualifier + "Child", depth));

    private static string Stronger(int depth) => "ref/T.(Tree).Child" + string.Concat(Enumerable.Repeat(".(CopyTree).Child", depth - 1));

    private static string Inspect(string parameters) => Trees + "func inspect<T>(" + parameters + ")\n    T is CopyTree\n    let pair = (value, value)\n" + Tail;

    private static void G170(StringBuilder o)
    {
        o.Append("== G170: lexical Copy of value under T is CopyTree; Binding today vs C over Binding's own clauses\n");
        foreach (var depth in new[] { 1, 2, 4, 8 })
        {
            foreach (var hint in new[] { "absent", "before", "after" })
            {
                if (depth == 1 && hint != "absent")
                {
                    continue;
                }

                var parameters = hint switch
                {
                    "absent" => "value: " + Value(depth),
                    "before" => "hint: " + Stronger(depth) + ", value: " + Value(depth),
                    _ => "value: " + Value(depth) + ", hint: " + Stronger(depth),
                };
                var c = Bind(Inspect(parameters));
                var f = Function(c, "inspect");
                var value = f.Parameters.Single(x => x.InternalName == "value" || x.ExternalName == "value").Type.TypeOf()!;
                var source = new BindingSource(c);
                source.Inputs(f);
                var model = source.Finish();
                var goal = new Formula(FK.Is, source.Type(value), source.Ref(c.Binding.Library.Copy, []));
                var answer = new Solver(model).Supply(goal, View.Ordinary);
                var reference = Reference(model, goal, depth + 2);
                o.Append($"d{depth} {hint}: Binding ProveCopy {c.Binding.ProveCopy(value, f)}; C {answer}; reference(bound {depth + 2}) {reference}; model {source.Summary}\n");
            }
        }

        // Generated queries (never spelled): T.Child^k interned by the model, not by TyCtxt.
        {
            var c = Bind(Inspect("value: " + Value(2)));
            var f = Function(c, "inspect");
            var value = f.Parameters.Single(x => x.InternalName == "value").Type.TypeOf()!;
            var source = new BindingSource(c);
            source.Inputs(f);
            var model = source.Finish();
            var one = source.Type(value).Kids[0];
            var t = one.Kids[0];
            var solver = new Solver(model);
            foreach (var k in new[] { 3, 8, 16 })
            {
                var term = t;
                for (var i = 0; i < k; i++)
                {
                    term = model.Terms.Make(one.Head, term, one.Kids[1]);
                }

                var answer = solver.Supply(new Formula(FK.Is, term, source.Ref(c.Binding.Library.Copy, [])), View.Ordinary);
                o.Append($"generated depth {k}: C {answer}\n");
            }
        }
    }

    private static void NonRecursive(StringBuilder o)
    {
        o.Append("== Non-recursive variant (R4's nonrec)\n");
        const string Outer = "contract Holder\n    associate Item\n\ncontract CopyHolder: Holder\n    Self.(Holder).Item is Copy\n\ncontract Outer\n    associate Inner is Holder\n\ncontract CopyOuter: Outer\n    Self.(Outer).Inner is CopyHolder\n\n";
        foreach (var hint in new[] { "absent", "before" })
        {
            var parameters = hint == "absent" ? "value: T.(Outer).Inner.(Holder).Item" : "hint: ref/T.(Outer).Inner.(CopyHolder).Item, value: T.(Outer).Inner.(Holder).Item";
            var c = Bind(Outer + "func inspect<T>(" + parameters + ") -> i32\n    T is CopyOuter\n    let a = value\n    let b = value\n    return 0\n" + Tail);
            var f = Function(c, "inspect");
            var value = f.Parameters.Single(x => x.InternalName == "value").Type.TypeOf()!;
            var source = new BindingSource(c);
            source.Inputs(f);
            var model = source.Finish();
            var goal = new Formula(FK.Is, source.Type(value), source.Ref(c.Binding.Library.Copy, []));
            o.Append($"{hint}: Binding ProveCopy {c.Binding.ProveCopy(value, f)}; C {new Solver(model).Supply(goal, View.Ordinary)}; reference {Reference(model, goal, 5)}\n");
        }
    }

    private static void G180(StringBuilder o)
    {
        o.Append("== G180: Self inside a Contract reference's Type arguments (BU's 2b-S5 review probes)\n");
        var cases = new (string Name, string Source, string Function, Func<BindingSource, Compilation, FunctionKoto, Formula> Goal)[]
        {
            ("stale0 (D<i32>, control)", "contract D<U>\n    associate N\ncontract C\n    associate X is D<i32>\nfunc needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\nfunc probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, i32>(value)\n", "probe",
                (s, c, f) => new Formula(FK.Is, s.Type(Target(f, 0)), s.Ref(Contract(c, "D"), [s.Type(BoundType.I32)]))),
            ("stale1 (associate X is D<Self>)", "contract D<U>\n    associate N\ncontract C\n    associate X is D<Self>\nfunc needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\nfunc probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, T>(value)\n", "probe",
                (s, c, f) => new Formula(FK.Is, s.Type(Target(f, 0)), s.Ref(Contract(c, "D"), [s.Type(Target(f, 0).Components[0])]))),
            ("stale2 (contract C: D<Self>)", "contract D<U>\n    associate N\ncontract C: D<Self>\nfunc probe<T>(value: T.(D<T>).N)\n    T is C\n    ()\n", "probe",
                (s, c, f) => new Formula(FK.Is, s.Type(f.Parameters[0].Type.TypeOf()!.Components[0]), s.Ref(Contract(c, "D"), [s.Type(f.Parameters[0].Type.TypeOf()!.Components[0])]))),
            ("h4 (X is D<Self.Y>): the required V is D<W>", "contract E\n    func e(self: ref/Self) -> i32\ncontract D<U>\n    U is E\ncontract C\n    associate Y is E\n    associate X is D<Self.Y>\nfunc needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\nfunc probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, T.(C).Y>(value)\n", "probe",
                (s, c, f) => GoalH4Required(s, c, f)),
            ("h4, D's argument clause U is E", "contract E\n    func e(self: ref/Self) -> i32\ncontract D<U>\n    U is E\ncontract C\n    associate Y is E\n    associate X is D<Self.Y>\nfunc probe<T>(value: ref/T.(C).X)\n    T is C\n    ()\n", "probe",
                (s, c, f) => GoalH4(s, c, f)),
            ("h6 (N is D<Self.(D<U>).M>)", "contract E\ncontract D<U>\n    U is E\n    associate N is D<Self.(D<U>).M>\n    associate M is E\ncontract C\n    associate X is D<Self.(C).Y>\n    associate Y is E\nfunc probe<T>(value: T.(C).X)\n    T is C\n    ()\n", "probe",
                (s, c, f) => GoalH6(s, c, f)),
        };
        foreach (var (name, text, function, goal) in cases)
        {
            var c = Bind(text + Tail);
            var f = Function(c, function);
            var source = new BindingSource(c);
            source.Inputs(f);
            var model = source.Finish();
            var g = goal(source, c, f);
            var issues = string.Join(", ", c.Binding.Issues.Select(x => x.Code.ToString()).Distinct());
            o.Append($"{name}: Binding issues [{issues}]; C {g} => {new Solver(model).Supply(g, View.Ordinary)}; reference {Reference(model, g, 5)}\n");
        }

        static BoundType Target(FunctionKoto f, int i) => f.Parameters[i].Type.TypeOf()!.Components.Count != 0 && f.Parameters[i].Type.TypeOf()!.Kind == BoundTypeKind.Semantics ? f.Parameters[i].Type.TypeOf()!.Components[0] : f.Parameters[i].Type.TypeOf()!;

        static Formula GoalH4(BindingSource s, Compilation c, FunctionKoto f)
        {
            var x = Target(f, 0);
            var t = s.Type(x.Components[0]);
            var cRef = s.Type(x.Components[1]);
            var y = s.Model.Terms.Make(s.ProjectionHead(Contract(c, "C"), "Y"), t, cRef);
            return new Formula(FK.Is, y, s.Ref(Contract(c, "E"), []));
        }

        static Formula GoalH4Required(BindingSource s, Compilation c, FunctionKoto f)
        {
            var x = Target(f, 0);
            var t = s.Type(x.Components[0]);
            var y = s.Model.Terms.Make(s.ProjectionHead(Contract(c, "C"), "Y"), t, s.Type(x.Components[1]));
            return new Formula(FK.Is, s.Type(x), s.Ref(Contract(c, "D"), [y]));
        }

        static Formula GoalH6(BindingSource s, Compilation c, FunctionKoto f)
        {
            var x = s.Type(f.Parameters[0].Type.TypeOf()!);
            var t = x.Kids[0];
            var y = s.Model.Terms.Make(s.ProjectionHead(Contract(c, "C"), "Y"), t, x.Kids[1]);
            var d = s.Ref(Contract(c, "D"), [y]);
            var m = s.Model.Terms.Make(s.ProjectionHead(Contract(c, "D"), "M"), x, d);
            var n = s.Model.Terms.Make(s.ProjectionHead(Contract(c, "D"), "N"), x, d);
            return new Formula(FK.Is, n, s.Ref(Contract(c, "D"), [m]));
        }
    }

    private static void Views(StringBuilder o)
    {
        o.Append("== Frozen inference views over real bindings (the owner's state after Bind; admissibility as today's owner publishes it)\n");
        // A completed external value (AssociatedInferenceTest.IndependentExternalTypeValuesDoNotDependOnDeclarationOrder's shape).
        const string External = "contract P\n    associate X\n    func x(self: ref/Self) -> Self.X\ncontract Q\n    associate W\n    func w(self: ref/Self) -> Self.W\n";
        foreach (var order in new[] { "D first", "E first" })
        {
            var d = "struct D\n    Self is P\n    public func x(self: ref/Self) -> i32 => 0\n";
            var e = "struct E\n    Self is Q\n    public func w(self: ref/Self) -> D.(P).X => 0\n";
            var c = Bind(External + (order == "D first" ? d + e : e + d) + Tail);
            var source = new BindingSource(c);
            var dType = Container(c, "D").BoundSymbol!.Type!;
            var eType = Container(c, "E").BoundSymbol!.Type!;
            source.Conformance(dType, Contract(c, "P"));
            source.Conformance(eType, Contract(c, "Q"));
            var model = source.Finish();
            var dx = source.BindingOf(dType, "X");
            var ew = source.BindingOf(eType, "W");
            o.Append($"external, {order}: bindings {source.Describe(dx)}; {source.Describe(ew)}; Binding verified D {c.Binding.GetConformanceDefinition(dType, Contract(c, "P"))?.IsVerified}, E {c.Binding.GetConformanceDefinition(eType, Contract(c, "Q"))?.IsVerified}\n");
            var dxProjection = model.Bindings[dx].Projection;
            var ewProjection = model.Bindings[ew].Projection;
            var i32 = source.Type(BoundType.I32);
            var viewD = new View("frozen D", source.InferredIn(Container(c, "D")), wantsReads: true);
            var viewE = new View("frozen E", source.InferredIn(Container(c, "E")), wantsReads: true);
            var solver = new Solver(model);
            var inE = solver.Supply(new Formula(FK.Eq, dxProjection, i32), viewE);
            o.Append($"  in E's view, D.(P).X is i32: {inE} reads {Reads(inE)} (a completed external value; its read returns to the owner)\n");
            var inD = solver.Supply(new Formula(FK.Eq, dxProjection, i32), viewD);
            o.Append($"  in D's view, D.(P).X is i32: {inD} (D's own inferred value is excluded)\n");
            // Dependencies flow back: E.W's published dependencies are the reads of the normalization E's inference ran.
            var flowed = new View("frozen D + dependents", source.InferredIn(Container(c, "D")).Concat(inE.PositiveReads.Any(r => r.Contains(dx)) ? [ew] : []), wantsReads: true);
            var ewInD = solver.Supply(new Formula(FK.Eq, ewProjection, i32), flowed);
            var ewInDToday = solver.Supply(new Formula(FK.Eq, ewProjection, i32), viewD);
            o.Append($"  in D's view, E.(Q).W is i32: with E.W's returned dependency {ewInD}; with today's owner state (no dependency published) {ewInDToday}\n");
            var ordinary = solver.Supply(new Formula(FK.Eq, ewProjection, i32), new View("ordinary+reads", [], wantsReads: true));
            o.Append($"  ordinary view, E.(Q).W is i32: {ordinary} reads {Reads(ordinary)}\n");
        }

        // The same-definition chain (SameNominalUnitNeverChainsInference's shape): two conformances of one definition.
        {
            const string Same = "contract P\n    associate X\n    func x(self: ref/Self) -> Self.X\ncontract Q\n    associate W\n    func w(self: ref/Self) -> Self.W\nstruct S\n    Self is P\n    Self is Q\n    public func x(self: ref/Self) -> i32 => 0\n    public func w(self: ref/Self) -> Self.(P).X => 0\n";
            var c = Bind(Same + Tail);
            var source = new BindingSource(c);
            var sType = Container(c, "S").BoundSymbol!.Type!;
            source.Conformance(sType, Contract(c, "P"));
            source.Conformance(sType, Contract(c, "Q"));
            var model = source.Finish();
            var issues = string.Join(", ", c.Binding.Issues.Select(x => x.Code.ToString()).Distinct());
            o.Append($"same definition: bindings {string.Join("; ", model.Bindings.Values.Select(source.Describe))}; Binding issues [{issues}]\n");
            if (model.Bindings.Values.FirstOrDefault(b => b.Name == "X") is { } x)
            {
                var viewS = new View("frozen S", source.InferredIn(Container(c, "S")), wantsReads: true);
                var a = new Solver(model).Supply(new Formula(FK.Eq, x.Projection, source.Type(BoundType.I32)), viewS);
                o.Append($"  in S's own view, S.(P).X is i32: {a} (no value inferred for S enters its frozen premises)\n");
            }
        }
    }

    private static void Cost(StringBuilder o)
    {
        o.Append("== Cost on the G170 programs (warm, same process; Binding's ProveCopy reads today's materialized facts)\n");
        foreach (var depth in new[] { 2, 8 })
        {
            foreach (var hint in new[] { "absent", "before" })
            {
                var parameters = hint == "absent" ? "value: " + Value(depth) : "hint: " + Stronger(depth) + ", value: " + Value(depth);
                var c = Bind(Inspect(parameters));
                var f = Function(c, "inspect");
                var value = f.Parameters.Single(x => x.InternalName == "value").Type.TypeOf()!;
                var source = new BindingSource(c);
                source.Inputs(f);
                var model = source.Finish();
                var goal = new Formula(FK.Is, source.Type(value), source.Ref(c.Binding.Library.Copy, []));
                var solver = new Solver(model);
                for (var i = 0; i < 20; i++)
                {
                    c.Binding.ProveCopy(value, f);
                    solver.Supply(goal, View.Ordinary);
                }

                // Interleaved: one timed ProveCopy, then one timed prototype request, 41 times; medians and the last allocation.
                var todayTimes = new List<double>();
                var protoTimes = new List<double>();
                long todayBytes = 0;
                long protoBytes = 0;
                for (var i = 0; i < 41; i++)
                {
                    var t1 = Time(() => c.Binding.ProveCopy(value, f), 1);
                    var t2 = Time(() => solver.Supply(goal, View.Ordinary), 1);
                    todayTimes.Add(t1.Us);
                    protoTimes.Add(t2.Us);
                    todayBytes = t1.Bytes;
                    protoBytes = t2.Bytes;
                }

                todayTimes.Sort();
                protoTimes.Sort();
                var bindTime = Time(() => c.Bind(), 5);
                o.Append($"d{depth} {hint}: Binding ProveCopy {todayTimes[20]:F2} us {todayBytes} B; C prototype {protoTimes[20]:F1} us {protoBytes} B (interleaved medians of 41); whole warm Bind {bindTime.Us / 1000:F1} ms; model templates {model.Templates.Count}\n");
            }
        }
    }

    private static (double Us, long Bytes) Time(Action action, int count = 41)
    {
        var times = new List<double>();
        long bytes = 0;
        for (var i = 0; i < count; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            action();
            times.Add(sw.Elapsed.TotalMicroseconds);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        times.Sort();
        return (times[times.Count / 2], bytes);
    }

    private static string Reads(Answer a) => "{" + string.Join("},{", a.PositiveReads.Select(x => string.Join(",", x))) + "}";

    private static string Reference(ProgramModel model, Formula goal, int bound)
    {
        var r = new Reference(model, View.Ordinary, false, bound);
        var seeds = new List<Term>();
        goal.Terms(seeds);
        r.Run(seeds);
        var (p, n) = r.Answer(goal);
        return r.Overflow ? "overflow" : (p ? "+" : "") + (n ? "-" : "") + (!p && !n ? "none" : "");
    }

    private static Compilation Bind(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Main.kimi", source));
        c.Bind();
        return c;
    }

    private static FunctionKoto Function(Compilation c, string name)
        => Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.Name == name && x.CodeContext?.SourceDocument?.Path == "Main.kimi");

    private static DeclarationContainerKoto Container(Compilation c, string name) => c.Kotonoha.RootKoto.NestedContainers.OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static BindingSymbol Contract(Compilation c, string name) => Container(c, name).BoundSymbol!;

    internal static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>The production source adapter: Binding's existing facts and declaration clauses as the prototype's model, read-only.</summary>
internal sealed class BindingSource
{
    private readonly Compilation c;
    private readonly Dictionary<object, int> ids = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, ContractDecl> decls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, (BoundType Type, BindingSymbol Contract)> conformanceOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, (Koto Definition, bool Inferred)> provenance = new();
    private int skipped;
    private int clauses;
    private int bindingIds;

    public BindingSource(Compilation c)
    {
        this.c = c;
        foreach (var root in new[] { c.Binding.Library.Kotonoha.RootKoto, c.Kotonoha.RootKoto })
        {
            foreach (var k in G170C1AdapterTest.Walk(root).OfType<ContractKoto>())
            {
                if (k.BoundSymbol?.Contract is not null && !this.decls.ContainsKey(k))
                {
                    var arity = k.BoundSymbol.Schema?.GenericSlots.Count ?? 0;
                    this.decls[k] = this.Model.AddContract(k.Name, arity, k.BoundSymbol);
                }
            }
        }

        foreach (var (k, decl) in this.decls)
        {
            var contract = k.BoundSymbol!.Contract!;
            decl.Status = k.BindingState == BindingState.Invalid ? Status.Invalid : Status.Valid;
            foreach (var ancestor in contract.Ancestors)
            {
                decl.Parents.Add(this.Reference(ancestor, k));
            }

            foreach (var clause in contract.ClauseStorage)
            {
                this.clauses++;
                if (clause.BoundConstraint is { } constraint && this.Constraint(constraint, k) is { } formula && HasVar(formula))
                {
                    decl.Clauses.Add(formula);
                }
                else
                {
                    this.skipped++;
                }
            }
        }
    }

    public ProgramModel Model { get; } = new();

    public string Summary => $"{this.decls.Count} Contracts, {this.clauses} clauses ({this.skipped} not dependent or not modeled), {this.Model.Inputs.Count} inputs";

    /// <summary>The finite input assumptions of a scope chain: each environment's DirectFacts (never DerivedFacts).</summary>
    public void Inputs(Koto context)
    {
        for (var scope = this.c.Binding.ConstraintScope(context); scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is { } environment)
            {
                foreach (var fact in environment.DirectFacts)
                {
                    if (this.Constraint(fact, null) is { } formula)
                    {
                        this.Model.Inputs.Add(formula);
                    }
                }
            }
        }
    }

    /// <summary>A verified conformance path's evidence: the conformance and its completed associated bindings with provenance.</summary>
    public void Conformance(BoundType type, BindingSymbol contract)
    {
        if (this.c.Binding.GetConformanceDefinition(type, contract) is not { } definition)
        {
            return;
        }

        foreach (var path in definition.Paths)
        {
            var conformance = new Conformance
            {
                Type = this.Type(type),
                Ref = this.Reference(path.Contract, null),
                VarCount = 1,
                Status = path.IsVerified ? Status.Valid : Status.Pending,
            };
            foreach (var (requirement, value) in path.AssociatedTypes)
            {
                var inferred = path.InferenceEvidence.Any(e => e.Identity.Equals(requirement));
                var id = this.bindingIds++;
                conformance.Bindings.Add(new AssocBinding
                {
                    Id = id,
                    Name = requirement.Symbol.Name,
                    Projection = this.Model.Terms.Make(this.ProjectionHead(requirement.Symbol), conformance.Type, this.Reference(requirement.Contract, null)),
                    Value = this.Type(value),
                    Status = Status.Valid,
                    Inferred = inferred,
                    Definition = this.Id(type.Symbol!.Declaration),
                });
                this.provenance[id] = (type.Symbol!.Declaration, inferred);
            }

            this.Model.Conformances.Add(conformance);
        }
    }

    public ProgramModel Finish()
    {
        this.Model.Prepare();
        return this.Model;
    }

    public int BindingOf(BoundType type, string name)
        => this.Model.Bindings.Values.Single(b => b.Name == name && b.Definition == this.Id(type.Symbol!.Declaration)).Id;

    public IEnumerable<int> InferredIn(Koto definition)
        => this.provenance.Where(x => ReferenceEquals(x.Value.Definition, definition) && x.Value.Inferred).Select(x => x.Key).ToList();

    public string Describe(int id) => this.Describe(this.Model.Bindings[id]);

    public string Describe(AssocBinding b) => $"#{b.Id} {b.Projection} = {b.Value}{(b.Inferred ? " (inferred)" : " (explicit)")}";

    public Term Ref(BindingSymbol contract, Term[] args) => this.Model.Terms.Make(this.decls[contract.Declaration].RefHead, args);

    public Head ProjectionHead(BindingSymbol contract, string name)
        => this.ProjectionHead(contract.Contract!.AssociatedTypes.First(x => x.Symbol.Name == name).Symbol);

    public Head ProjectionHead(BindingSymbol associated) => new SymHead(SymKind.Proj, associated.Name, associated);

    public Term Type(BoundType type) => this.Type(type, null);

    private Term Reference(BindingSymbol contract, Koto? context)
        => contract.Type is { } reference && reference.Symbol?.Declaration is ContractKoto ? this.Type(reference, context) : this.Model.Terms.Make(this.decls[contract.Declaration].RefHead);

    private Term Type(BoundType type, Koto? context)
    {
        if (context is ContractKoto contract)
        {
            if (Binding.IsContractSelf(type) && ReferenceEquals(type.Symbol!.SelfOf!.Declaration, contract))
            {
                return this.Model.Terms.Var(0);
            }

            if (type.Kind == BoundTypeKind.Parameter && type.Symbol is { } p && Binding.ContainerSlot(contract, p) is var slot && slot >= 0)
            {
                return this.Model.Terms.Var(1 + slot);
            }
        }

        var kids = type.Components.Select(x => this.Type(x, context)).ToArray();
        if (type.Symbol?.Declaration is ContractKoto declaration && this.decls.TryGetValue(declaration, out var decl))
        {
            return this.Model.Terms.Make(decl.RefHead, kids);
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            var origins = string.Join(",", type.OriginArguments.Select(this.Id));
            return this.Model.Terms.Make(new SymHead(SymKind.Proj, type.Symbol!.Name, origins.Length == 0 ? type.Symbol : (type.Symbol, origins)), kids);
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            return this.Model.Terms.Make(new SymHead(SymKind.Param, type.Name, type.Symbol), kids);
        }

        var key = $"{type.Kind}|{(type.Symbol is null ? type.Name : this.Id(type.Symbol))}|{type.Semantics}|{type.Length}|{(type.Origin is null ? 0 : this.Id(type.Origin))}|{string.Join(",", type.OriginArguments.Select(this.Id))}";
        return this.Model.Terms.Make(new SymHead(type.Kind == BoundTypeKind.Primitive ? SymKind.Prim : SymKind.Con, type.Name, key), kids);
    }

    private Formula? Constraint(BoundConstraint f, Koto? context)
    {
        switch (f.Kind)
        {
            case ConstraintKind.Contract when f.Subject is { } s && f.Contract is { } contract && this.decls.ContainsKey(contract.Declaration):
                return new(FK.Is, this.Type(s, context), this.Reference(contract, context));
            case ConstraintKind.TypeIdentity when f.Subject is { } s && f.RequiredType is { } r:
                return new(FK.Eq, this.Type(s, context), this.Type(r, context));
            case ConstraintKind.Not when this.Constraint(f.Left!, context) is { } l:
                return new(FK.Not, l: l);
            case ConstraintKind.And or ConstraintKind.Or when this.Constraint(f.Left!, context) is { } l && this.Constraint(f.Right!, context) is { } r:
                return new(f.Kind == ConstraintKind.And ? FK.And : FK.Or, l: l, r: r);
            default:
                return null; // Semantics, Callable, Error, Unresolved: not premise-supply rows of this model
        }
    }

    private static bool HasVar(Formula f)
    {
        var terms = new List<Term>();
        f.Terms(terms);
        return terms.Any(t => t.HasVar);
    }

    private int Id(object o)
    {
        if (!this.ids.TryGetValue(o, out var id))
        {
            this.ids[o] = id = this.ids.Count + 1;
        }

        return id;
    }
}
