// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// G170 C0 probe (BU planning session, 2026-10-21): evidence at the exact head, not a regression. It is kept as data under
// draft/Sketches/G170/c0/ and never lands in the test project. Run: G170_C0_OUT=<file> xUnitTest.dll -method XunitTest.G170C0ProbeTest.Probe
// Per program and pipeline (old Analysis = Legacy, A1/A4 = Mir): Binding's issues; per parameter of the queried function the canonical
// lexical query (Binding.ProveCopy at the function) and the effect query (TyCtxt.Copy, ProveCopy under EffectPremises); generated
// queries (projections interned through TyCtxt, never spelled); the premise environments of the function's scope chain (D: DirectFacts,
// d: a DerivedFacts row and its source Contract); the old path's records (LegacyRecords) and the MIR's problems.
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class G170C0ProbeTest
{
    private const string Trees = "contract Tree\n    associate Child is Tree\n\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n\n";
    private const string Tail = "\npublic func main() -> ()\n    ()\n";

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("G170_C0_OUT") is not { } path)
        {
            return;
        }

        var text = new StringBuilder();
        string Value(int depth, string qualifier = "(Tree).") => "T" + string.Concat(Enumerable.Repeat("." + qualifier + "Child", depth));
        string Inspect(string parameters, string body = "    let pair = (value, value)\n") => Trees + "func inspect<T>(" + parameters + ")\n    T is CopyTree\n" + body + Tail;
        string Stronger(int depth) => "ref/T.(Tree).Child" + string.Concat(Enumerable.Repeat(".(CopyTree).Child", depth - 1));

        // G170 reconstructed: the original Type and scope, the stronger qualifier absent/before/after, depth 1/2/4/8.
        foreach (var depth in new[] { 1, 2, 4, 8 })
        {
            Run(text, $"d{depth} absent", Inspect("value: " + Value(depth)), "inspect", [depth, depth + 1, 8], facts: depth <= 2);
            if (depth > 1)
            {
                Run(text, $"d{depth} stronger before", Inspect("hint: " + Stronger(depth) + ", value: " + Value(depth)), "inspect", [depth + 1], facts: depth == 2);
                Run(text, $"d{depth} stronger after", Inspect("value: " + Value(depth) + ", hint: " + Stronger(depth)), "inspect", [depth + 1], facts: false);
            }
        }

        // Spelling variants of the hint and of the value at depth 2 (R4's a/b hints; the qualifier on the wrong receiver; no qualifier).
        Run(text, "d2 hint all-CopyTree after", Inspect("value: " + Value(2) + ", hint: ref/" + Value(2, "(CopyTree).")), "inspect", [], facts: false);
        Run(text, "d2 hint CopyTree-on-T-only after", Inspect("value: " + Value(2) + ", hint: ref/T.(CopyTree).Child.(Tree).Child"), "inspect", [], facts: true);
        Run(text, "d4 hint depth-2 stronger only", Inspect("value: " + Value(4) + ", hint: " + Stronger(2)), "inspect", [], facts: false);
        Run(text, "d2 value unqualified", Inspect("value: T.Child.Child"), "inspect", [], facts: false);
        Run(text, "d2 value written CopyTree", Inspect("value: " + Value(2, "(CopyTree).")), "inspect", [], facts: false);
        Run(text, "d2 hint in another function", Inspect("value: " + Value(2)) + "func other<T>(hint: " + Stronger(2) + ")\n    T is CopyTree\n    ()\n", "inspect", [], facts: false);
        Run(text, "d2 body () (PlanB's form)", Inspect("value: " + Value(2), "    ()\n"), "inspect", [], facts: false);

        // The member-access trigger (RequirementMember): a body call on a T.(Tree).Child receiver, before or after the Copy use.
        const string Walkable = "contract Tree\n    associate Child is Tree\n    func child(self: ref/Self) -> Option<Self.Child>\n\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n\n";
        const string Member = "func inspect<T>(value: T.(Tree).Child.(Tree).Child, node: ref/T.(Tree).Child)\n    T is CopyTree\n";
        Run(text, "d2 member call on T.Child after the use", Walkable + Member + "    let pair = (value, value)\n    let next = node.child()\n" + Tail, "inspect", [], facts: true);
        Run(text, "d2 member call on T.Child before the use", Walkable + Member + "    let next = node.child()\n    let pair = (value, value)\n" + Tail, "inspect", [], facts: false);
        Run(text, "d2 member call declared in CopyTree on T.Child", Walkable.Replace("    Self.(Tree).Child is CopyTree\n", "    Self.(Tree).Child is CopyTree\n    func touch(self: ref/Self)\n", StringComparison.Ordinal) + Member + "    node.touch()\n    let pair = (value, value)\n" + Tail, "inspect", [], facts: true);
        Run(text, "d2 member call declared in CopyTree on T.Child, after the use", Walkable.Replace("    Self.(Tree).Child is CopyTree\n", "    Self.(Tree).Child is CopyTree\n    func touch(self: ref/Self)\n", StringComparison.Ordinal) + Member + "    let pair = (value, value)\n    node.touch()\n" + Tail, "inspect", [], facts: false);
        Run(text, "d2 no member call (same declarations)", Walkable + Member + "    let pair = (value, value)\n" + Tail, "inspect", [], facts: false);

        // Query order: the same programs queried generated-first and in reverse parameter order in a fresh compilation.
        Run(text, "d2 absent, reverse query order", Inspect("value: " + Value(2)), "inspect", [8, 3], facts: false, reverse: true);
        Run(text, "d2 stronger before, reverse query order", Inspect("hint: " + Stronger(2) + ", value: " + Value(2)), "inspect", [3], facts: false, reverse: true);

        // The non-recursive Contract variant (R4's nonrec): Copy comes through CopyOuter's clause on Inner, then CopyHolder's on Item.
        const string Outer = "contract Holder\n    associate Item\n\ncontract CopyHolder: Holder\n    Self.(Holder).Item is Copy\n\ncontract Outer\n    associate Inner is Holder\n\ncontract CopyOuter: Outer\n    Self.(Outer).Inner is CopyHolder\n\n";
        const string OuterBody = "    T is CopyOuter\n    let a = value\n    let b = value\n    return 0\n";
        Run(text, "nonrec absent", Outer + "func inspect<T>(value: T.(Outer).Inner.(Holder).Item) -> i32\n" + OuterBody + Tail, "inspect", [], facts: true);
        Run(text, "nonrec stronger before", Outer + "func inspect<T>(hint: ref/T.(Outer).Inner.(CopyHolder).Item, value: T.(Outer).Inner.(Holder).Item) -> i32\n" + OuterBody + Tail, "inspect", [], facts: false);
        Run(text, "nonrec stronger after", Outer + "func inspect<T>(value: T.(Outer).Inner.(Holder).Item, hint: ref/T.(Outer).Inner.(CopyHolder).Item) -> i32\n" + OuterBody + Tail, "inspect", [], facts: false);

        // S5Probe's last three cases (PLAN G170's reproducers), with generated queries of T, T.Child and T.Child.Child in Holder.put's
        // scope (A4's query: ProveCopy under EffectPremises(put)).
        const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n";
        const string Tree = "contract Tree\n    associate Child is Tree\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\n";
        const string CopyTree = "contract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";
        string Helpers(string call) => Sink + Tree + CopyTree +
            "group Trees\n    public func two<T>(node: T)\n        T is Tree\n        match node.child()\n            .Some(let next) => Trees.one(next@move)\n" +
            "            .None => return\n    public func one<T>(node: T)\n        T is Tree\n        match node.child()\n            .Some(let next) => return\n" +
            "            .None => return\n" +
            "struct Holder<T>\n    T is CopyTree\n    Self is Sink\n    var node: T\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n" +
            $"        {call}\n        return .Ok(())\n";
        Run(text, "S5 helper chain to T.Child.Child (two)", Helpers("Trees.two(self.node)"), "put", [0, 1, 2, 3], facts: true, holder: true);
        Run(text, "S5 helper chain to T.Child (one)", Helpers("Trees.one(self.node)"), "put", [0, 1, 2], facts: false, holder: true);
        Run(text, "S5 twice", "contract Tree\n    associate Child is Tree\n" + CopyTree + "group H\n" +
            "    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n" +
            "        T is CopyTree\n        return (value, value)\n", "twice", [], facts: false);

        // The generation method: a projection interned from a Contract requirement's Self.Child equals the spelled one.
        Run(text, "generation check", Tree + CopyTree + "func inspect<T>(value: T.(Tree).Child)\n    T is CopyTree\n    ()\n" + Tail, "inspect", [], facts: false, holder: false, check: true);

        // G172 (Binding only): SPEC 8.5.5's PA/PB in both declaration orders and all requirement orders; alt, excl, cyc and tri (R4's probes).
        string Pair(string first, string second, bool reverse) => reverse ? second + first : first + second;
        foreach (var (pa, rp, rq) in new[] { (false, false, false), (false, false, true), (false, true, false), (false, true, true), (true, false, false), (true, false, true), (true, true, false), (true, true, true) })
        {
            var p = "contract P\n    associate X\n    associate Z\n" + Pair("    func x(self: ref/Self) -> Self.X\n", "    func z(self: ref/Self) -> Self.Z\n", rp);
            var q = "contract Q\n    associate Y\n    associate W\n" + Pair("    func y(self: ref/Self) -> Self.Y\n", "    func w(self: ref/Self) -> Self.W\n", rq);
            Inference(text, $"papb {(pa ? "PB first" : "PA first")}, P {(rp ? "z,x" : "x,z")}, Q {(rq ? "w,y" : "y,w")}", p + q + Pair(
                "struct PA\n    Self is P\n    public func x(self: ref/Self) -> PB.(Q).Y => 0\n    public func z(self: ref/Self) -> i32 => 0\n",
                "struct PB\n    Self is Q\n    public func y(self: ref/Self) -> i64 => 0\n    public func w(self: ref/Self) -> PA.(P).Z => 0\n", pa), [("PA", "P"), ("PB", "Q")]);
        }

        foreach (var (e, rr) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            Inference(text, $"alt {(e ? "E first" : "D first")}, P {(rr ? "b,a" : "a,b")}", "contract P\n    associate X\n" + Pair("    func a(self: ref/Self) -> Self.X\n", "    func b(self: ref/Self) -> Self.X\n", rr) +
                "contract Q\n    associate W\n    func w(self: ref/Self) -> Self.W\n" + Pair(
                "struct D\n    Self is P\n    public func a(self: ref/Self) -> i32 => 0\n    public func b(self: ref/Self) -> E.(Q).W => 0\n",
                "struct E\n    Self is Q\n    public func w(self: ref/Self) -> D.(P).X => 0\n", e), [("D", "P"), ("E", "Q")]);
        }

        const string Excl = "contract P\n    associate X\n    associate Z\n    func x(self: ref/Self) -> Self.X\n    func z(self: ref/Self) -> Self.Z\ncontract Q\n    associate Y\n    func y(self: ref/Self) -> Self.Y\n";
        const string ExclA = "struct A\n    Self is P\n    public func x(self: ref/Self) -> B.(Q).Y => 0\n    public func z(self: ref/Self) -> i32 => 0\n";
        foreach (var b in new[] { false, true })
        {
            Inference(text, $"excl {(b ? "B first" : "A first")}", Excl + Pair(ExclA, "struct B\n    Self is Q\n    public func y(self: ref/Self) -> A.(P).Z => 0\n", b), [("A", "P"), ("B", "Q")]);
        }

        foreach (var b in new[] { false, true })
        {
            Inference(text, $"cyc {(b ? "B first" : "A first")}", "contract P\n    associate X\n    func x(self: ref/Self) -> Self.X\ncontract Q\n    associate Y\n    func y(self: ref/Self) -> Self.Y\n" + Pair(
                "struct A\n    Self is P\n    public func x(self: ref/Self) -> B.(Q).Y => 0\n", "struct B\n    Self is Q\n    public func y(self: ref/Self) -> A.(P).X => 0\n", b), [("A", "P"), ("B", "Q")]);
        }

        var tri = new Dictionary<char, string>
        {
            ['A'] = ExclA,
            ['B'] = "struct B\n    Self is Q\n    public func y(self: ref/Self) -> C.(Q).Y => 0\n",
            ['C'] = "struct C\n    Self is Q\n    public func y(self: ref/Self) -> A.(P).Z => 0\n",
        };
        foreach (var order in new[] { "ABC", "ACB", "BAC", "BCA", "CAB", "CBA" })
        {
            Inference(text, $"tri {order}", Excl + string.Concat(order.Select(x => tri[x])), [("A", "P"), ("B", "Q"), ("C", "Q")]);
        }

        File.WriteAllText(path, text.ToString());
    }

    private static void Inference(StringBuilder text, string name, string source, (string Type, string Contract)[] definitions)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Main.kimi", source));
        c.Bind();
        var line = new StringBuilder($"INFER {name}: complete={c.Binding.Result.IsComplete}");
        foreach (var (typeName, contractName) in definitions)
        {
            var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == typeName);
            var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == contractName);
            var conformance = c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!);
            line.Append($"; {typeName}: verified={conformance?.IsVerified}");
            foreach (var pair in conformance?.AssociatedTypes ?? new Dictionary<BoundRequirement, BoundType>())
            {
                line.Append($" {pair.Key.Symbol.Name}={pair.Value.Name}");
            }
        }

        text.Append(line).Append('\n');
        foreach (var issue in c.Binding.Issues)
        {
            var start = Math.Min(issue.Node.Span.Start, source.Length);
            var column = start - source.LastIndexOf('\n', Math.Max(0, start - 1));
            text.Append($"  {issue.Code} | {1 + source.AsSpan(0, start).Count('\n')}:{column} `{issue.Node.ToString()?.Split('\n')[0].Trim()}`\n");
        }
    }

    private static void Run(StringBuilder text, string name, string source, string function, int[] generated, bool facts, bool reverse = false, bool holder = false, bool check = false)
    {
        text.Append("##### ").Append(name).Append('\n').Append(source.TrimEnd('\n')).Append("\n#####\n");
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            var c = Compilation.CreateForTest();
            c.Pipeline = pipeline;
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Main.kimi", source));
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
            c.Analyze();
            string Loc(Koto? node)
            {
                if (node is null || node.CodeContext?.SourceDocument?.Path != "Main.kimi")
                {
                    return node is null ? "?" : "lib";
                }

                var start = Math.Min(node.Span.Start, source.Length);
                var line = 1 + source.AsSpan(0, start).Count('\n');
                var column = start - source.LastIndexOf('\n', Math.Max(0, start - 1));
                return $"{line}:{column} `{node.ToString()?.Split('\n')[0].Trim()}`";
            }

            text.Append($"=== {pipeline}: Binding complete={c.Binding.Result.IsComplete}, issues={c.Binding.Issues.Count()}\n");
            foreach (var issue in c.Binding.Issues)
            {
                text.Append($"BIND {issue.Code} | {Loc(issue.Node)}\n");
            }

            if (pipeline == CompilerPipeline.Legacy)
            {
                var f = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.Name == function && x.CodeContext?.SourceDocument?.Path == "Main.kimi");
                var symbol = f.SymbolOf();
                var queries = new List<(string Label, BoundType Type)>();
                for (var i = 0; i < f.Parameters.Count; i++)
                {
                    if (f.Parameters[i].Type.TypeOf() is { } t)
                    {
                        queries.Add(($"param{i} `{f.Parameters[i].Type}`", t));
                    }
                }

                if (holder || check)
                {
                    // T of Holder (or of inspect) and the Child projection component of Tree's requirement `child`.
                    var tree = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Tree");
                    var child = tree.Members.OfType<FunctionKoto>().Single(x => x.Name == "child").ReturnType!.TypeOf()!.Components[0];
                    var root = holder ? ((IsKoto)c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Holder").ConstraintNodes[0]).Left.TypeOf()! : f.Parameters[0].Type.TypeOf()!.Components[0];
                    if (check)
                    {
                        var made = c.Types.Intern(BoundTypeKind.AssociatedProjection, child.Symbol, SemanticsKind.Owner, [root, child.Components[1]]);
                        text.Append($"GENERATED-EQUALS-SPELLED {ReferenceEquals(made, f.Parameters[0].Type.TypeOf())}\n");
                    }

                    foreach (var depth in generated)
                    {
                        var t = root;
                        for (var d = 0; d < depth; d++)
                        {
                            t = c.Types.Intern(BoundTypeKind.AssociatedProjection, child.Symbol, SemanticsKind.Owner, [t, child.Components[1]]);
                        }

                        queries.Add(($"generated depth {depth} `{Binding.HoverTypeName(t)}`", t));
                    }
                }
                else if (generated.Length != 0)
                {
                    var value = f.Parameters.Single(x => x.InternalName == "value" || x.ExternalName == "value").Type.TypeOf()!;
                    var one = value;
                    while (one.Components[0].Kind == BoundTypeKind.AssociatedProjection)
                    {
                        one = one.Components[0];
                    }

                    foreach (var depth in generated)
                    {
                        var t = one;
                        for (var d = 1; d < depth; d++)
                        {
                            t = c.Types.Intern(BoundTypeKind.AssociatedProjection, one.Symbol, SemanticsKind.Owner, [t, one.Components[1]]);
                        }

                        queries.Add(($"generated depth {depth}{(ReferenceEquals(t, value) ? " (== spelled value)" : string.Empty)} `{Binding.HoverTypeName(t)}`", t));
                    }
                }

                if (reverse)
                {
                    queries.Reverse();
                }

                foreach (var (label, t) in queries)
                {
                    text.Append($"PROVE Copy {function}.{label}: lexical {c.Binding.ProveCopy(t, f)}, effect {c.Types.Copy(symbol, t)} ({c.Binding.ProveCopy(t, c.Binding.EffectPremises(symbol))})\n");
                }

                if (facts && f.Body?.Items.LastOrDefault() is { } last && !ReferenceEquals(c.Binding.ConstraintScope(last), c.Binding.ConstraintScope(f)))
                {
                    text.Append($"LAST STATEMENT SCOPE differs from the function's: {c.Binding.ConstraintScope(last).Owner.GetType().Name}\n");
                    foreach (var (label, t) in queries)
                    {
                        text.Append($"PROVE Copy at the last statement {label}: {c.Binding.ProveCopy(t, last)}\n");
                    }
                }

                if (facts)
                {
                    for (var scope = c.Binding.ConstraintScope(f); scope is not null; scope = scope.Parent)
                    {
                        if (scope.Constraints is not { } environment)
                        {
                            continue;
                        }

                        text.Append($"FACTS of {scope.Owner.GetType().Name} {(scope.Owner as FunctionKoto)?.Name ?? (scope.Owner as DeclarationContainerKoto)?.Name}: {environment.Facts.Count} facts, {environment.DirectFacts.Count} direct, {environment.DerivedFacts.Count} derived rows\n");
                        foreach (var fact in environment.Facts)
                        {
                            var sources = string.Join(",", environment.DerivedFacts.Where(x => ReferenceEquals(x.Fact, fact)).Select(x => x.Source.Name));
                            text.Append($"  {(environment.DirectFacts.Contains(fact) ? "D" : " ")}{(sources.Length != 0 ? "d[" + sources + "]" : string.Empty)} {Print(fact)}\n");
                        }
                    }
                }

                foreach (var (requirement, code, node) in LegacyRecords.Of(c))
                {
                    if (requirement.Partition != DiagnosticPartition.Binding)
                    {
                        text.Append($"OLD  {code} [{requirement.Name}] | {Loc(node)}\n");
                    }
                }
            }
            else
            {
                var program = c.BuildMir();
                var problems = program.Problems.All.ToArray().Where(x => x.Subject.Module == 0).ToArray();
                foreach (var p in problems)
                {
                    text.Append($"MIR  {p.Code} [{p.Requirement.Name}] | {Loc(c.SyntaxOf(p.Subject))}\n");
                }

                text.Append($"MIR problems: {problems.Length}\n");
            }
        }

        text.Append('\n');
    }

    private static string Print(BoundConstraint fact) => fact.Kind switch
    {
        ConstraintKind.Contract => $"{Binding.HoverTypeName(fact.Subject!)} is {fact.Contract?.Name}",
        ConstraintKind.TypeIdentity => $"{Binding.HoverTypeName(fact.Subject!)} is {Binding.HoverTypeName(fact.RequiredType!)}",
        ConstraintKind.Not => "not (" + Print(fact.Left!) + ")",
        ConstraintKind.And or ConstraintKind.Or => "(" + Print(fact.Left!) + ") " + fact.Kind + " (" + Print(fact.Right!) + ")",
        _ => fact.Kind + (fact.Subject is { } s ? " " + Binding.HoverTypeName(s) : string.Empty) + " " + fact.Mask,
    };

    private static IEnumerable<Koto> Walk(Koto node)
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
