// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The MIR build (docs/dev/COMPILER_ARCHITECTURE.md, R3): every body of the Kimi library and the milestone programs builds and
/// validates without a finding, a form the builder does not build yet is an Opaque value at its one located record, the build is
/// deterministic and a warm rebuild allocates nothing. The census measures what the builder covers (V9).</summary>
public sealed class MirBuildTest(ITestOutputHelper output)
{
    // V9: builds every body of the corpus and counts the bodies, those with an Opaque value, the records per HirOp and the bodies per number
    // of Semantics case views; with KIMI_MIR_CENSUS naming a file, writes the census there as TSV. Every built body is valid and within the
    // case bound (plan 5.6.1), and every static a body reaches through staticEnsure, every Closure it creates and every default template it
    // calls has its body (plan 5.6.2, 5.6.6).
    [Fact]
    public void CorpusBuildsValidBodies()
    {
        var rows = new List<string> { "case\tbodies\topaque\tunsupported\tderived" };
        var sites = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var views = new SortedDictionary<long, int>();
        var totals = new int[4];
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            var program = c.BuildMir();
            Assert.True(program.Findings.Count == 0, $"{name}: {string.Join(", ", program.Findings.Take(20))}");
            int[] counts = [0, 0, 0, 0];
            foreach (var body in program.Bodies)
            {
                foreach (var block in body.Blocks)
                {
                    var ensured = block.Terminator is not { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } } ensure ||
                        program[new(body.Statics[(int)body.Consts[body.Operands[ensure.Start].Arg].Bits].Symbol, MirBodyKind.StaticInit)] is not null;
                    Assert.True(ensured, $"{name}: a static without its initializer");
                    var templated = block.Terminator is not { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: > 0 } template } ||
                        program[new(template.Symbol, MirBodyKind.DefaultTemplate, template.Arg - 1)] is not null;
                    Assert.True(templated, $"{name}: a call of a default template without its body");
                }

                foreach (var statement in body.Statements)
                {
                    var defined = statement.Rvalue is not { Kind: MirRvalueKind.Aggregate, Op: (byte)MirAggregateKind.Closure, Type: var closure } ||
                        program[new(closure!.Symbol, MirBodyKind.Closure)] is not null;
                    Assert.True(defined, $"{name}: a Closure without its body");
                }

                if (body.Source.Module == module.Syntax.Module)
                {
                    counts[0]++;
                    counts[1] += body.HasOpaque ? 1 : 0;
                    views[MirCases.Count(body)] = views.GetValueOrDefault(MirCases.Count(body)) + 1;
                    foreach (var site in body.OpaqueSites)
                    {
                        counts[3] += HirTables.Op(c.SyntaxOf(site.Source)) == HirOp.Error ? 1 : 0;
                    }
                }
            }

            foreach (var record in program.Records.Where(x => x.Subject.Module == module.Syntax.Module))
            {
                counts[2]++;
                var key = Key(c.SyntaxOf(record.Subject));
                sites[key] = sites.GetValueOrDefault(key) + 1;
            }

            rows.Add($"{name}\t{string.Join('\t', counts)}");
            for (var i = 0; i < counts.Length; i++)
            {
                totals[i] += counts[i];
            }
        }

        rows.Add($"total\t{string.Join('\t', totals)}");
        rows.Add("site\tcount");
        rows.AddRange(sites.OrderByDescending(static x => x.Value).ThenBy(static x => x.Key, StringComparer.Ordinal).Select(static x => $"{x.Key}\t{x.Value}"));
        rows.Add("views\tbodies");
        rows.AddRange(views.Select(static x => $"{x.Key}\t{x.Value}"));
        output.WriteLine(string.Join('\n', rows));
        if (Environment.GetEnvironmentVariable("KIMI_MIR_CENSUS") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllLines(path, rows, new UTF8Encoding(false));
        }

        Assert.True(totals[0] > 500, $"{totals[0]} bodies");
        Assert.True(views.Keys.Max() <= 64, $"{views.Keys.Max()} views");
    }

    // The same compilation builds the same MIR, fresh or after a warm rebind, and a fresh compilation of the same source does too; Milestone43
    // keeps Opaque values where Binding rejected it.
    [Fact]
    public void BuildIsDeterministic()
    {
        var first = Dump(SemanticSnapshotTest.Bind("milestone/Milestone43").Compilation);
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone43");
        Assert.Equal(first, Dump(c));
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(first, Dump(c));
        Assert.Contains("opaque", first, StringComparison.Ordinal);
    }

    // Plan 6.2: a form without a building rule is one record at its location, however many times a body meets it (a defer body, built at
    // each way out of its scope), and an Opaque value naming the locals its syntax touches. A node the lexer or Binding rejected is a derived
    // Opaque without a record (SPEC 23.1.2): Binding's Error, a use of a local whose declaration is an Error, and the Field initializer of a
    // declaration Binding did not bind (a struct with a misplaced `length` slot), in each constructor. Building goes on after each. The
    // unbuilt form is a fill of a length its instance decides, which no R3 unit builds.
    [Fact]
    public void UnsupportedFormsAreOpaqueAtOneRecord()
    {
        var c = Compile("struct Grid<length N>\n    public var cells: [N of i32] = [N of 7]\n    public init() => ()\n    public init(y: i32) => ()\n" +
            "func f<length N>(a: i32, b: i32) -> i32\n    let c: [N of i32] = [N of (a + b)]\n    return b\nfunc g() -> i32 => missing\n" +
            "func h<length N>(a: i32, c: bool) -> i32\n    defer\n        let d: [N of i32] = [N of a]\n    if c => return 1\n    return a\n" +
            "func k() -> i32\n    let x = missing\n    return x\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "g" or "h" or "k").ToDictionary(static x => x.Key.Decl!.Name);
        var fill = bodies["f"];
        Assert.True(fill.HasOpaque);
        Assert.Equal([1, 2], fill.OpaqueRoots.ToArray());
        Assert.Contains(fill.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Return);
        var records = program.Records.Where(static x => x.Subject.Module == 0).Select(x => c.SyntaxOf(x.Subject).ToString()).Order(StringComparer.Ordinal);
        Assert.Equal(["[N of (a + b)]", "[N of a]"], records);
        Assert.Equal(2, bodies["h"].OpaqueSites.ToArray().Count(x => c.SyntaxOf(x.Source).ToString() == "[N of a]"));

        Assert.Equal(2, program.Bodies.ToArray().Count(x => x.OpaqueSites.ToArray().Any(y => c.SyntaxOf(y.Source).ToString() == "[N of 7]")));
        Assert.Equal(HirOp.Error, HirTables.Op(c.SyntaxOf(bodies["g"].OpaqueSites[0].Source)));
        Assert.Equal(["let x = missing", "x"], bodies["k"].OpaqueSites.ToArray().Select(x => c.SyntaxOf(x.Source).ToString()));
    }

    // V11: publishing, building and validating an unchanged compilation again, and selecting every Semantics case view of its bodies with
    // their locals' case Types, allocates nothing, owning interpolations (Milestone30) and pair-generic bodies (Milestone28) included.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("milestone/Milestone34")]
    [InlineData("milestone/Milestone28")]
    [InlineData("milestone/Milestone30")]
    public void WarmRebuildAllocatesNothing(string name)
    {
        var (c, _) = SemanticSnapshotTest.Bind(name);
        var (bodies, version) = (c.BuildMir().Bodies.Length, c.BuildMir().Version);
        var cases = new MirCases(c.Types);
        var rebuilds = 0UL;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            rebuilds++;
            c.Binding.Bind(BindingMode.Final);
            foreach (var body in c.BuildMir().Bodies)
            {
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    cases.Select(body, view);
                    foreach (var local in body.Locals)
                    {
                        _ = cases.Type(local.Type);
                    }
                }
            }
        }));
        Assert.Equal((bodies, version + rebuilds), (c.BuildMir().Bodies.Length, c.BuildMir().Version)); // Warm-up and measured rebuilds.
    }

    // V11, plan 5.6.4: N defers, then N returns that each leave all of them, build each defer once per target (one drop tree shared by
    // the returns, and the normal end), so the MIR and its cold build's allocation grow linearly with N, and a warm rebuild allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DropTreesScaleLinearly()
    {
        var (sizes, work, allocated) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        Compilation c = null!;
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("func touch(n: i32) => ()\nfunc work(flag: bool) -> i32\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append("    defer => touch(").Append(n).Append(")\n");
            }

            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append("    if flag => return ").Append(n).Append('\n');
            }

            c = Compile(text.Append("    return -1\n").ToString());
            var before = GC.GetAllocatedBytesForCurrentThread();
            var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "work");
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            work[i] = body.Blocks.Length + body.Statements.Length;
            Assert.Equal(sizes[i] * 2, body.Blocks.ToArray().Count(static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "touch" }));
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }

    // Plan 5.6.2: a default whose template reaches itself through the defaults its calls omit, alone (tests/mir/defaults) or with another
    // (a component of the omission graph), is a call of its template wherever it is omitted, never a copy. A default Binding rejected fails
    // its template, and a call that omits it takes an Opaque value at the call, reading the earlier slots, with no record of its own.
    [Fact]
    public void RecursiveDefaultsAreTemplateCallsAndFailedOnesOpaque()
    {
        var c = Compile("func f(n: i32, value: i32 = if n == 0 => 1 else => g(n - 1)) -> i32 => value\n" +
            "func g(n: i32, value: i32 = if n == 0 => 2 else => f(n - 1)) -> i32 => value\nfunc h(x: i32, y: i32 = missing) -> i32 => y\n" +
            "let a = f(2)\nlet b = h(1)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var bodies = program.Bodies.ToArray();
        Assert.Equal(["g#1"], Templates(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "f" })));
        Assert.Equal(["f#1"], Templates(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "g" })));
        Assert.True(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "h" }).HasOpaque);
        var top = bodies.Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        Assert.Equal(["f#1"], Templates(top));
        Assert.DoesNotContain(top.Scopes.ToArray(), static x => x.Kind == MirScopeKind.Inlined);
        var failed = Assert.Single(top.OpaqueSites.ToArray());
        Assert.Equal("h(1)", c.SyntaxOf(failed.Source).ToString());
        Assert.Equal(MirLocalRole.PendingSlot, top.Locals[Assert.Single(top.OpaqueRoots.Slice(failed.Start, failed.Count).ToArray())].Role);

        static string[] Templates(MirBody body) => body.Blocks.ToArray().Where(static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: > 0 } })
            .Select(static x => $"{x.Terminator.Callee.Symbol!.Name}#{x.Terminator.Callee.Arg - 1}").ToArray();
    }

    // IMPL 21.3.2, plan 5.6.2: a copy that would take a body past 65,536 MIR statements is not made. Defaults that copy the one below
    // twice at each level report DefaultExpansionLimit_Kd once, at the omitting call where the limit is met; that template fails, so the
    // calls that omit it take an Opaque value without another record.
    [Fact]
    public void DefaultExpansionStopsAtTheLimit()
    {
        var text = new StringBuilder("func f0(x: i32 = 1) -> i32 => x\n");
        for (var i = 1; i <= 16; i++)
        {
            text.Append("func f").Append(i).Append("(x: i32 = f").Append(i - 1).Append("() + f").Append(i - 1).Append("()) -> i32 => x\n");
        }

        var c = Compile(text.Append("let value = f16()\n").ToString());
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var record = Assert.Single(program.Records);
        Assert.Equal(DiagnosticCode.DefaultExpansionLimit_Kd, record.Code);
        Assert.Matches(@"^f\d+\(\)$", c.SyntaxOf(record.Subject).ToString());
        Assert.All(program.Bodies.ToArray(), static x => Assert.InRange(x.Statements.Length, 0, 65_536));
        Assert.True(program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0).HasOpaque);
    }

    // V11, plan 5.6.2: N calls that omit a generic default whose template holds a copy of another grow the body by two Inlined scopes each,
    // the MIR and the cold build's allocation linearly with N, and a warm rebuild allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DefaultCopiesScaleLinearly()
    {
        var (sizes, work, allocated) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        Compilation c = null!;
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("func next(x: i32, y: i32 = x + 1) -> i32 => y\nfunc scale<T>(x: T, factor: i32 = next(2)) -> i32\n    T is Copy\n    return factor\n");
            text.Append("func work() -> i32\n    var total = 0\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append("    total += scale(").Append(n).Append(")\n");
            }

            c = Compile(text.Append("    return total\n").ToString());
            var before = GC.GetAllocatedBytesForCurrentThread();
            var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "work" });
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            work[i] = body.Blocks.Length + body.Statements.Length;
            Assert.Equal(sizes[i] * 2, body.Scopes.ToArray().Count(static x => x.Kind == MirScopeKind.Inlined));
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }

    // V11: a body of N statement groups of the common forms (a local from a call, its case adaptation, a condition with an update, a match
    // with a binding, a loop with a transfer, an interpolation) grows its MIR and its cold build's allocation linearly with N, and a warm
    // rebuild, whose validation resolves each adaptation under both case views, allocates nothing. The build analyzes (R4-M17); the output
    // gives A1's dense arena words for the body, which grow with heads × flow paths (R4-M20, V16's reading).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BodiesScaleLinearly()
    {
        var (sizes, work, allocated, arena) = (new[] { 16, 32, 64 }, new long[3], new long[3], new long[3]);
        Compilation c = null!;
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("enum Shape\n    Dot\n    Line(i32)\nfunc touch(n: i32) -> i32 => n\nfunc work<s/T>(flag: bool, shape: Shape) -> i32\n");
            text.Append("    s is owner or ref\n    var total = 0\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append($"    let v{n} = touch({n})\n    _ = v{n}@s\n    if flag and v{n} > 0 => total += v{n}\n    match shape\n        .Line(let x) => total += x\n        _ => ()\n");
                text.Append("    while total > 1000\n        total -= 1\n        if flag => exit\n    Console.writeLine(\"\\(total)\")\n");
            }

            c = Compile(text.Append("    return total\n").ToString());
            var before = GC.GetAllocatedBytesForCurrentThread();
            var program = c.BuildMir();
            var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "work" });
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            work[i] = body.Blocks.Length + body.Statements.Length;
            Assert.True(!body.HasOpaque && program.Records.Count == 0 && program.Findings.Count == 0, $"{sizes[i]} groups");
            var (flow, moves) = (new MirDataflow(new MirReach()), new MirMoveData());
            flow.Begin(body);
            moves.Build(body, flow);
            arena[i] = MirInitTest.ArenaWords(body, flow, moves);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}, " +
            $"A1 arena words {string.Join(", ", arena)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }

    // Plan 5.6.1, SPEC 8.14, 8.15, 13.5.5: one body serves every Semantics case. Under each view of tests/mir/cases (one binder, two
    // binders, a Closure, which has its enclosing function's binders, a default template and its copy, which names the omitting caller's
    // binder), a PairLayer step is its Place itself in the owner case and the referent of the case's reference otherwise, and a
    // PairAcquire Reborrows in the uniq case and Copies in the others.
    [Fact]
    public void ViewsResolvePairLayersAndAcquisitions()
    {
        var c = Compile(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "cases.kimi")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var cases = new MirCases(c.Types);
        var lines = new List<string>();
        foreach (var body in program.Bodies)
        {
            for (var view = 0L; body.Binders.Count > 0 && view < MirCases.Count(body); view++)
            {
                var (resolved, selected) = (new List<string>(), cases.Select(body, view).ToArray());
                var resolver = new Resolver(body.Projections.ToArray(), cases, resolved);
                for (var block = 0; block < body.Blocks.Length; block++)
                {
                    for (var i = 0; i <= body.Blocks[block].Count; i++)
                    {
                        MirUses.ForEach(body, new(block, i), ref resolver);
                    }
                }

                var name = body.Key.Decl?.Name is { Length: > 0 } declared ? $"{body.Key.Kind} {declared}" : body.Key.Kind.ToString();
                lines.Add($"{name} {view} [{string.Join(", ", selected.Select(static x => x.Semantics))}]: {string.Join(", ", resolved.Distinct())}");
            }
        }

        output.WriteLine(string.Join('\n', lines));
        Assert.Equal(
            [
                "DefaultTemplate peek 0 [Owner]: @b0 itself: U",
                "DefaultTemplate peek 1 [Uniq]: @b0 *uniq/U: U",
                "Fn keep 0 [Owner]: acquire _3 copy, acquire _4 copy, @b0 itself: T",
                "Fn keep 1 [Uniq]: acquire _3 reborrow, acquire _4 reborrow, @b0 *uniq/T: T",
                "Fn both 0 [Owner, Owner]: @b0 itself: T, acquire _2 copy, acquire _7 copy",
                "Fn both 1 [Owner, Ref]: @b0 itself: T, acquire _2 copy, acquire _7 copy",
                "Fn both 2 [Uniq, Owner]: @b0 *uniq/T: T, acquire _2 copy, acquire _7 copy",
                "Fn both 3 [Uniq, Ref]: @b0 *uniq/T: T, acquire _2 copy, acquire _7 copy",
                "Closure 0 [Owner, Owner]: @b1 itself: U",
                "Closure 1 [Owner, Ref]: @b1 *ref/U: U",
                "Closure 2 [Uniq, Owner]: @b1 itself: U",
                "Closure 3 [Uniq, Ref]: @b1 *ref/U: U",
                "Fn peek 0 [Owner]: ",
                "Fn peek 1 [Uniq]: ",
                "Fn caller 0 [Owner]: acquire _1 copy, @b0 itself: T",
                "Fn caller 1 [Uniq]: acquire _1 reborrow, @b0 *uniq/T: T",
            ],
            lines);
    }

    // SPEC 8.14, 8.15, 13.5.3: an explicit case adaptation is one Adapt, which each Semantics case view of tests/mir/adaptations resolves to
    // the operation Binding selects in that case, as the operation's ordinary rvalue: a Place Copied, a temporary moved or an object
    // created from either (Use), an address or a borrow of the operand's Place, or for an object view of the whole object below its pair
    // layer (Ref; an objuniq upcast's Reborrow too), an owning or objref upcast and a pointer conversion (Cast); in a Closure under its
    // enclosing function's binder, in a default template under its own and in the template's copy under the omitting caller's. An
    // adaptation Binding rejected is a derived Opaque, without an Adapt or a record.
    [Fact]
    public void ViewsResolveAdaptations()
    {
        var c = Compile(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "adaptations.kimi")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var cases = new MirCases(c.Types);
        var lines = new List<string>();
        foreach (var body in program.Bodies)
        {
            var name = body.Key.Decl?.Name is { Length: > 0 } declared ? $"{body.Key.Kind} {declared}" : body.Key.Kind.ToString();
            foreach (var adapt in body.Statements.ToArray().Select(static x => x.Rvalue).Where(static x => x.Kind == MirRvalueKind.Adapt))
            {
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    var selected = cases.Select(body, view).ToArray();
                    var resolved = cases.Adapt(adapt, out var operation);
                    lines.Add($"{name} [{string.Join(", ", selected.Select(static x => x.Semantics))}]: {operation} {Form(body, resolved)}");
                }
            }
        }

        output.WriteLine(string.Join('\n', lines));
        Assert.Equal(
            [
                "DefaultTemplate take [Owner]: Identity Move _1",
                "DefaultTemplate take [Obj]: ObjectCreation Move _1",
                "DefaultTemplate take [Rc]: ObjectCreation Move _1",
                "DefaultTemplate take [Arc]: ObjectCreation Move _1",
                "Fn discard [Owner]: Identity Copy _1",
                "Fn discard [Ref]: Borrow Shared _1: ref/i32",
                "Fn discard [Obj]: ObjectCreation Copy _1",
                "Fn discard [Rc]: ObjectCreation Copy _1",
                "Fn discard [Arc]: ObjectCreation Copy _1",
                "Fn address [Obj]: ObjectCreation Copy _1",
                "Fn address [Raw]: Address Raw _1: raw/i32",
                "Fn widen [Obj]: ObjectUpcast Upcast: obj/Base",
                "Fn widen [Rc]: ObjectUpcast Upcast: rc/Base",
                "Fn widen [Arc]: ObjectUpcast Upcast: arc/Base",
                "Fn inspect [Obj]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn inspect [Rc]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn inspect [Arc]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn mixed [Ref]: Borrow Shared _3: ref/ref/T",
                "Fn mixed [Obj]: Identity Move _3",
                "Closure [Owner]: Identity Copy _2",
                "Closure [Obj]: ObjectCreation Copy _2",
                "Closure [Rc]: ObjectCreation Copy _2",
                "Closure [Arc]: ObjectCreation Copy _2",
                "Fn give [Owner]: Identity Move _3",
                "Fn give [Obj]: ObjectCreation Move _3",
                "Fn give [Rc]: ObjectCreation Move _3",
                "Fn give [Arc]: ObjectCreation Move _3",
                "Fn peek [Obj]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn peek [ObjRef]: ObjectUpcast Upcast: objref/Base",
                "Fn poke [Obj]: ObjectUpcast Uniq _2.PairLayer: objuniq/Base",
                "Fn poke [ObjUniq]: ObjectUpcast Uniq _2.PairLayer: objuniq/Base",
                "Fn cast [Raw]: Pointer Pointer: raw/u8",
                "Fn cast [Raw]: Pointer IntToPointer: raw/u8",
            ],
            lines);

        var rejected = Compile("func wrong<s/T>() -> s/i64\n    s is object\n    return 1@s/i64\n");
        var built = rejected.BuildMir();
        Assert.Empty(built.Findings);
        Assert.Empty(built.Records);
        var wrong = built.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "wrong");
        Assert.True(wrong.HasOpaque);
        Assert.DoesNotContain(wrong.Statements.ToArray(), static x => x.Rvalue.Kind == MirRvalueKind.Adapt);

        static string Form(MirBody body, in MirRvalue rvalue) => rvalue.Kind switch
        {
            MirRvalueKind.Use => $"{body.Operands[rvalue.Start].Kind} {Place(body, body.Operands[rvalue.Start].Place)}",
            MirRvalueKind.Ref => $"{(MirRefKind)rvalue.Op} {Place(body, rvalue.Place)}: {TyCtxt.Name(rvalue.Type!)}",
            _ => $"{(MirCastKind)rvalue.Op}: {TyCtxt.Name(rvalue.Type!)}",
        };

        static string Place(MirBody body, MirPlace place) => $"_{place.Root}{(place.Projection == 0 ? string.Empty : "." + body.Projections[place.Projection].Kind)}";
    }

    // SPEC 8.15, 23.1.1: Binding decides the case bound once per definition (Binding.CaseLimits), so no built body has more than 64 views.
    // Six binders of two cases give 64 distinct views of the body, its Closure and its default's template, in the old analysis's order (the
    // last binder fastest). A seventh puts the definition beyond the bound: Binding binds its parts but publishes none of them, so no body,
    // Closure or default template of it is built, also where another default omits its default, and a call that omits it takes an Opaque
    // value without a record or finding.
    [Fact]
    public void TheDefinitionCheckDecidesTheCaseBound()
    {
        foreach (var count in new[] { 6, 7 })
        {
            var names = "abcdegh"[..count].ToCharArray();
            var text = new StringBuilder("func f<").AppendJoin(", ", names.Select(static x => $"{x}/{char.ToUpperInvariant(x)}")).Append(">(");
            text.AppendJoin(", ", names.Select(static x => $"p{x}: {x}/{char.ToUpperInvariant(x)}")).Append(", k: i32 = 1 + 2) -> i32\n");
            text.AppendJoin(string.Empty, names.Select(static x => $"    {x} is owner or uniq\n"));
            text.Append("    let add = func (x: i32) -> i32 => x + k\n    _ = pa@move\n    return add(k)\npublic func main() -> ()\n    var n: i32 = 1\n");
            text.Append("    _ = f(n, n@uniq").Append(string.Concat(Enumerable.Repeat(", n", count - 2))).Append(")\n");
            text.Append("func g(x: i32 = f(1").Append(string.Concat(Enumerable.Repeat(", 1", count - 1))).Append(")) -> i32 => x\n");
            var c = Compile(text.ToString());
            Assert.Equal(count == 6, c.Binding.Result.IsComplete);
            Assert.Equal(count == 6 ? [] : new[] { DiagnosticCode.OwnershipCaseLimit_Kd }, c.Binding.Issues.Select(static x => x.Code));
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            var parts = program.Bodies.ToArray().Where(static x => x.Source.Module == 0 && (x.Key.Decl?.Name == "f" || x.Key.Kind == MirBodyKind.Closure)).ToArray();
            var main = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "main");
            Assert.Equal(count == 7, program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "g" && x.Key.Kind == MirBodyKind.DefaultTemplate).HasOpaque);
            if (count == 7)
            {
                Assert.Empty(parts);
                Assert.Single(main.OpaqueSites.ToArray());
            }
            else
            {
                Assert.Equal([MirBodyKind.DefaultTemplate, MirBodyKind.Fn, MirBodyKind.Closure], parts.Select(static x => x.Key.Kind));
                Assert.All(parts, static x => Assert.Equal(64L, MirCases.Count(x)));
                Assert.False(main.HasOpaque);
                var (cases, views) = (new MirCases(c.Types), new List<string>());
                for (var view = 0L; view < MirCases.Count(parts[1]); view++)
                {
                    views.Add(string.Concat(cases.Select(parts[1], view).ToArray().Select(static x => x.Semantics == SemanticsKind.Owner ? 'o' : 'u')));
                }

                Assert.Equal(64, views.Distinct().Count());
                Assert.Equal(new[] { "oooooo", "ooooou", "uuuuuu" }, new[] { views[0], views[1], views[^1] });
            }

            c.Analyze();
            Assert.DoesNotContain(c.Problems, static x => x.Code is DiagnosticCode.OwnershipCaseLimit_Kd or DiagnosticCode.InternalInvariant_Kd);
        }
    }

    // SPEC 8.15, 23.1.1: a definition whose only part is a parameter default is bounded too, and beyond the bound the default has no
    // template. Seven unconstrained binders give 9^7 cases; a requirement's default is itself rejected (MisplacedSyntax_Kd).
    [Fact]
    public void ADefaultBeyondTheCaseBoundHasNoTemplate()
    {
        var c = Compile("contract C\n    func need<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(self: ref/Self, p: a/A, q: b/B, r: c/C, u: d/D, v: e/E, w: g/G, y: h/H, k: i32 = 1 + 2) -> ()\n" +
            "public func main() -> ()\n    ()\n");
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.OwnershipCaseLimit_Kd);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Bodies.ToArray(), static x => x.Source.Module == 0 && x.Key.Kind == MirBodyKind.DefaultTemplate);
    }

    // SPEC 8.15, 23.1.1: an accessor's body is a part of its Property, so beyond the bound neither it nor a Field initializer, a method or
    // the synthesized constructor of a seven-binder struct is built, although Binding binds them.
    [Fact]
    public void NoMemberOfAContainerBeyondTheCaseBoundIsBuilt()
    {
        var c = Compile("struct S<a/A, b/B, c/C, d/D, e/E, g/G, h/H>\n" + string.Concat("abcdegh".Select(static x => $"    {x} is owner or uniq\n")) +
            "    var count: i32 = 1 + 2\n    computed size: i32\n        get(self: ref/Self) -> i32 => self.count + 1\n" +
            "    func touch(self: ref/Self) -> i32 => self.count + 2\npublic func main() -> ()\n    ()\n");
        Assert.Equal(4, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.OwnershipCaseLimit_Kd));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.All(program.Bodies.ToArray().Where(static x => x.Source.Module == 0), static x => Assert.True(x.Key.Decl is null || x.Key.Decl.Name == "main", x.Key.ToString()));
    }

    // SPEC 22.2, 16.3.2 (InlineStorage's contract in Storage.kimi): the compiler never destroys an InlineStorage's contents, so the
    // destructor body of a fixed array's owning remainder, which destroys the remaining elements itself, ends without a Drop of its
    // receiver or of any of its parts.
    [Fact]
    public void AnInlineStorageIsNeverDestroyed()
    {
        var program = Compile("public func main()\n    let values: [3 of string] = [\"a\", \"b\", \"c\"]\n    for value in values@move => ()\n").BuildMir();
        Assert.Empty(program.Findings);
        var drop = program.Bodies.ToArray()
            .Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "drop" } && x.Locals[1].Type.Symbol?.Name == "FixedOwnedRemainder");
        Assert.DoesNotContain(drop.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Drop && x.Terminator.Place.Root == 1);
    }

    // SPEC 15.6.7: every exclusive borrow that produces an invocation's argument or receiver is reserved (TwoPhaseUniq): a written @uniq,
    // also in parentheses, the Reborrow of a stored uniq at a uniq parameter and a synthesized receiver (an Array literal's reserve and
    // appends), and a written @uniq on a receiver's path; a borrow saved in storage or a control-flow result is live when formed (Uniq).
    [Fact]
    public void ReservedCallInputsAreTwoPhase()
    {
        var source = "func take(a: uniq/i32, b: i32) => ()\nfunc f()\n    var p: i32 = 1\n    take(p@uniq, p + 1)\n    take((p@uniq), 2)\n" +
            "    var r = p@uniq\n    take(r, 3)\n    let values = [1, 2]\nstruct Counter\n    var count: i32\n    public init() => self.count = 0\n" +
            "    public func bump(self: uniq/Self, by: i32) => self.count = self.count + by\nstruct Holder\n    public var c: Counter\n" +
            "    public init() => self.c = Counter.init()\nfunc g(h: uniq/Holder)\n    h@follow@uniq.c.bump(1)\n    h@follow@uniq.c@uniq.bump(2)\nfunc chosen()\n    var p: i32 = 1\n" +
            "    take(if p > 0 => p@uniq else => p@uniq, 4)\n";
        Assert.Equal(
            [MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq, MirRefKind.Uniq, MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq,
                MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq],
            Refs(Body(source, "f")));
        Assert.Equal([MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq], Refs(Body(source, "g")));
        Assert.Equal([MirRefKind.Uniq, MirRefKind.Uniq], Refs(Body(source, "chosen")));

        static IEnumerable<MirRefKind> Refs(MirBody body) => body.Statements.ToArray()
            .Where(static x => x.Kind == MirStatementKind.Assign && x.Rvalue.Kind == MirRvalueKind.Ref).Select(static x => (MirRefKind)x.Rvalue.Op);
    }

    // SPEC 15.6.7, 14.10.3: a call whose argument does not complete is still built where that argument's code continues for the checks,
    // so its activation is checked there, and the reservation the transfer abandons ends on the transfer's runtime edge only. The argument
    // has no value, so it is of Type Never, also where its own Type is a Tuple with a Never element.
    [Fact]
    public void ADivergingArgumentContinuesItsCallForTheChecks()
    {
        var source = "func three(a: uniq/i32, b: i32, c: uniq/i32) => ()\nfunc f() -> i32\n    var p: i32 = 1\n    three(p@uniq, (return 1), p@uniq)\n" +
            "    return 2\nfunc pair(x: (i32, i32)) => ()\nfunc g() -> i32\n    pair((1, (return 1)))\n    return 2\n";
        var paired = Body(source, "g");
        var operand = Assert.Single(paired.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "pair" })
            .Terminator.Start;
        Assert.Same(BoundType.Never, paired.TypeOf(paired.Operands[operand].Place));
        var body = Body(source, "f");
        var reach = new MirReach();
        reach.Clear(body.Blocks.Length);
        reach.Walk(body, 0, checks: false);
        var blocks = body.Blocks.ToArray();
        var call = Assert.Single(
            Enumerable.Range(0, blocks.Length),
            b => blocks[b].Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "three" });
        Assert.False(reach[call]);
        var ends = Enumerable.Range(0, blocks.Length).Where(b => body.Statements.Slice(blocks[b].Start, blocks[b].Count).ToArray()
            .Any(static x => x.Kind == MirStatementKind.EndBorrow)).ToArray();
        Assert.NotEmpty(ends);
        Assert.All(ends, b => Assert.True(reach[b]));
        var transfers = Enumerable.Range(0, blocks.Length).Where(b => blocks[b].Terminator is { Kind: MirTerminatorKind.Goto, CheckTarget: >= 0 }).ToArray();
        Assert.NotEmpty(transfers);
        Assert.DoesNotContain(transfers, b => ends.Contains(b)); // The branching block, which the checks' edge leaves too, ends nothing.
    }

    // SPEC 4.6.3, 4.8.1, 5.6 (DECISIONS R4-M40): a range selection's RawSlice takes what the Slice retains as its backing, the third
    // operand: a shared borrow of an array's whole Place, or a Slice receiver's handle, copied first and never borrowed (SPEC 4.7.4);
    // nothing backs a written Raw.slice, so its backing is Unit.
    [Fact]
    public void RangeSelectionsPassTheirBacking()
    {
        const string Source = "func middle(values: ref/[5 of i32]) -> Slice<i32> during values => values[1..^1]\n" +
            "func reslice(view: Slice<i32>) -> Slice<i32> during view.source => view[1..]\n" +
            "func empty() -> Slice<i32>\n    unsafe => return Raw.slice(null@raw/i32, 0)\n";
        var middle = Body(Source, "middle");
        var backing = middle.Operands[Slice(middle)];
        var borrow = Assert.Single(
            middle.Statements.ToArray(),
            x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref } && x.Place == backing.Place);
        Assert.Equal((MirRefKind.Shared, 1), ((MirRefKind)borrow.Rvalue.Op, borrow.Rvalue.Place.Root));
        var reslice = Body(Source, "reslice");
        var handle = reslice.Operands[Slice(reslice)];
        var copy = Assert.Single(reslice.Statements.ToArray(), x => x.Kind == MirStatementKind.Assign && x.Place == handle.Place);
        Assert.Equal((MirOperandKind.Move, MirRvalueKind.Use), (handle.Kind, copy.Rvalue.Kind));
        Assert.Equal((MirOperandKind.Copy, 1), (reslice.Operands[copy.Rvalue.Start].Kind, reslice.Operands[copy.Rvalue.Start].Place.Root));
        Assert.DoesNotContain(
            reslice.Statements.ToArray(),
            static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref, Rvalue.Place.Root: 1 });
        var empty = Body(Source, "empty");
        Assert.Equal(MirConstKind.Unit, empty.Consts[empty.Operands[Slice(empty)].Arg].Kind);

        // The third operand of the body's one RawSlice.
        static int Slice(MirBody body)
        {
            var call = body.Blocks.ToArray().Single(static x => x.Terminator is
            {
                Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.RawSlice },
            });
            Assert.Equal(3, call.Terminator.Count);
            return call.Terminator.Start + 2;
        }
    }

    // SPEC 7.7.3, 15.6.4 (DECISIONS R4-M41): a Value callee names the Callable signature its callee declares, a TypeToken constant: the
    // Function Type with its own Origins, not the call's, whose result is bound to the argument.
    [Fact]
    public void ValueCalleesNameTheirSignature()
    {
        var body = Body("func apply(f: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 during x => f(x)\n", "apply");
        var call = Assert.Single(body.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value });
        var signature = body.Consts[call.Terminator.Callee.Arg];
        Assert.Equal(MirConstKind.TypeToken, signature.Kind);
        Assert.Same(body.TypeOf(new MirPlace(1)), signature.Type);
    }

    // DECISIONS R4-M61: the publisher gives a node an Op only with the facts its building rule reads, so a node Binding resolved without
    // them (a call's, an indexer's or a view selection's plan, a local's symbol, a closure's definition, a case adaptation's plan, a loop's
    // iterator symbol, a write of an interpolation) is published Error and recorded as InternalInvariant_Kd at it, with an Opaque value
    // in its place, which the validator accepts, never a fault. A node Binding rejected that lacks the same fact is an Error derived from
    // its error, without a record.
    [Fact]
    public void AResolvedNodeWithoutItsFactsIsABrokenContract()
    {
        var c = Compile(
            "func one() -> i32 => 1\nfunc called() -> i32 => one()\nfunc rejected() -> i32 => one()\nstruct Pair\n    Self is UniqIndexable<isize>\n" +
            "    associate Element is i32\n    var first: i32 = 1\n    public init() => ()\n" +
            "    public func index(self, key: ref/isize) -> ref/i32 during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self => self.first@uniq\n" +
            "func read(pair: ref/Pair) -> i32 => pair[0]\nfunc view(items: ref/UniqSlice<i32>) -> Slice<i32> during items => items[1..^1]\n" +
            "func local() -> i32\n    let x = 1\n    return x\n" +
            "func makeAdder(offset: i32) -> (i32) -> i32\n    return func [offset] (value) => value + offset\n" +
            "func discard<s/T>(value: i32) -> ()\n    s is owner or ref or object\n    _ = value@s\n" +
            "func sum(values: ref/Array<i32>) -> i32\n    var total = 0\n    for value in values\n        total += value\n    return total\n" +
            "func text(x: i32) -> string => \"a\\(x)\"\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.ToString() == "one()").ToArray();
        var call = calls[0];
        var indexes = nodes.OfType<IndexKoto>().ToArray();
        var local = nodes.OfType<VariableKoto>().Single(static x => x.ToString().StartsWith("let x", StringComparison.Ordinal));
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous);
        var adaptation = nodes.OfType<ConversionKoto>().Single(static x => x.ToString() == "value@s"); // A case adaptation (SPEC 8.14).
        var loop = nodes.OfType<ForKoto>().Single();
        var text = nodes.OfType<InterpolatedStringKoto>().Single();
        Assert.Equal((2, 2), (calls.Length, indexes.Length));
        HirTables.PlanFor(call, written: false).Plan = null; // After Binding and before the first publish, the facts go missing.
        HirTables.PlanFor(calls[1], written: false).Plan = null;
        HirTables.Of(calls[1]).Failures[calls[1].SyntaxId] = BindingFailure.TypeMismatch; // Rejected by Binding as well.
        HirTables.PlanFor(c.Binding.IndexerCall(indexes[0], exclusive: false)!, written: false).Plan = null;
        HirTables.PlanFor(c.Binding.ViewRangeCall(indexes[1])!, written: false).Plan = null;
        HirTables.Of(local).Symbols[local.SyntaxId] = null;
        HirTables.PlanFor(closure, written: false).Plan = null;
        HirTables.PlanFor(adaptation, written: false).Plan = null;
        HirTables.Of(loop.Iteration!.Receiver).Symbols[loop.Iteration.Receiver.SyntaxId] = null;
        HirTables.PlanFor(text.Formatting!.Writes[1], written: false).Plan = null;
        var program = c.BuildMir();
        Koto[] broken = [call, indexes[0], indexes[1], local, closure, adaptation, loop, text];
        Assert.Empty(program.Findings);
        Assert.All(broken.Append(calls[1]), static x => Assert.Equal(HirOp.Error, HirTables.Op(x)));
        Assert.Equal(
            broken.Select(SourceRef.Of),
            program.Records.Where(static x => x.Code == DiagnosticCode.InternalInvariant_Kd).Select(static x => x.Subject));
        Assert.DoesNotContain(SourceRef.Of(calls[1]), program.Records.Select(static x => x.Subject));
    }

    // SPEC 15.1.3: a fixed-array element at a literal position is projected at the position its index keeps on its own plan row, so the
    // literal's own row, here an Error, is never read.
    [Fact]
    public void AConstIndexReadsItsOwnPosition()
    {
        Assert.Equal(Built(rejected: false), Built(rejected: true));

        static string Built(bool rejected)
        {
            var c = Compile("func second(values: [3 of i32]) -> i32 => values[(1)]\n");
            var literal = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<NumberLiteralKoto>().Single(static x => x.ToString() == "1");
            if (rejected)
            {
                HirTables.Of(literal).Failures[literal.SyntaxId] = BindingFailure.TypeMismatch;
            }

            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            Assert.Equal(HirPlaceKind.ConstIndex, HirTables.PlaceOf(literal.Parent!.Parent!).Kind);
            var dump = new StringBuilder();
            MirDump.Write(program, 0, dump);
            return dump.ToString();
        }
    }

    // SPEC 3.4, 13.7: Binding designates a Property target through its parentheses, so an update or assignment of a parenthesized
    // Property builds as the unparenthesized one does (its syntax ids aside), whichever of its accessors is custom.
    [Theory]
    [InlineData("        set(self: uniq/Self, value: i32) -> () => storage = value", "s.item += 1")]
    [InlineData("        get(self: ref/Self) -> i32 => storage\n        set(self: uniq/Self, value: i32) -> () => storage = value", "s.item += 1")]
    [InlineData("        set(self: uniq/Self, value: i32) -> () => storage = value", "s.item = 2")]
    [InlineData("        get(self: ref/Self) -> i32 => storage + 1", "s.item++")]
    public void AParenthesizedPropertyTargetIsItsProperty(string accessors, string statement)
    {
        Assert.Equal(Built(statement), Built(statement.Replace("s.item", "(s.item)")));

        string Built(string written)
        {
            var program = Compile($"struct S\n    public var item: i32 = 0\n{accessors}\n\nfunc f(s: uniq/S)\n    {written}\n").BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            var text = new StringBuilder();
            MirDump.Write(program, 0, text);
            return string.Join('\n', text.ToString().Split('\n').Select(static x => x.Split(" @")[0]));
        }
    }

    // SPEC 13.7.2: an in-place compound update reserves its located destination (TwoPhaseUniq) and reads and writes it through that
    // reservation, so the write activates it.
    [Fact]
    public void CompoundUpdatesWriteThroughTheirReservation()
    {
        var body = Body("func f()\n    var x: i32 = 1\n    x += 2\n    var t: (i32, i32) = (1, 2)\n    t.0 *= 3\n", "f");
        var statements = body.Statements.ToArray();
        var reservations = statements.Where(static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref })
            .Select(static x => ((MirRefKind)x.Rvalue.Op, x.Place.Root)).ToArray();
        Assert.Equal(2, reservations.Length);
        foreach (var (kind, root) in reservations)
        {
            Assert.Equal(MirRefKind.TwoPhaseUniq, kind);
            Assert.Contains(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.BinaryOp } && x.Place.Root == root &&
                body.Projections[x.Place.Projection].Kind == MirProjectionKind.Deref);
        }
    }

    // SPEC 14.8.3: a guard candidate's guard-local Origin ends at the guard's cleanup on every path, a transfer out of the guard
    // included: each StorageDead of a candidate follows its EndBorrow.
    [Fact]
    public void GuardCandidatesEndAtEveryCleanup()
    {
        var body = Body(
            "func f(o: Option<i32>) -> i32\n    match o\n        .Some(let v) if v@follow > 3 or (return 9) => return v\n        _ => return 0\n",
            "f");
        var candidates = Enumerable.Range(0, body.Locals.Length).Where(x => body.Locals[x].Role == MirLocalRole.GuardCandidate).ToArray();
        Assert.NotEmpty(candidates);
        var (ends, deaths) = (0, 0);
        foreach (var block in body.Blocks.ToArray())
        {
            var statements = body.Statements.Slice(block.Start, block.Count).ToArray();
            for (var i = 0; i < statements.Length; i++)
            {
                if (statements[i] is { Kind: MirStatementKind.StorageDead } death && candidates.Contains(death.Place.Root))
                {
                    deaths++;
                    Assert.True(i > 0 && statements[i - 1] is { Kind: MirStatementKind.EndBorrow } end && end.Place == death.Place);
                }

                ends += statements[i].Kind == MirStatementKind.EndBorrow && candidates.Contains(statements[i].Place.Root) ? 1 : 0;
            }
        }

        Assert.True(deaths >= 2 && ends == deaths, $"{ends} EndBorrows, {deaths} StorageDeads");
    }

    internal static Compilation Compile(string source, bool test = false)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = test;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        c.Bind();
        _ = test ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(OutputKind.Application);
        return c;
    }

    // The body of a function of a source, built without validator findings.
    private static MirBody Body(string source, string name)
    {
        var program = Compile(source).BuildMir();
        Assert.Empty(program.Findings);
        return program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
    }

    // The census key of a record: its Op, a Name's Res, a call's callee kind or the part of the call not built yet, and an Adjust the
    // builder does not take yet.
    private static string Key(Koto node)
    {
        var op = HirTables.Op(node);
        var key = op.ToString();
        if (op == HirOp.Name)
        {
            key += "." + HirTables.ResOf(node);
        }
        else if (op == HirOp.Call && CallPlan.Of(node) is { } call)
        {
            var disposition = call.Kind == CalleeKind.Value ? default : IntrinsicDisposition.Of(call.Target.CompilerFunction);
            key += "." + (call.Kind != CalleeKind.Function ? call.Kind.ToString() : disposition.Kind != DispositionKind.Body ? disposition.Kind.ToString()
                : CallPlan.Of(node, 1) is not null ? "Expansion" : !call.DefaultArguments.IsEmpty ? "Default" : CallPlan.Parameter(call.Target, -1) is not null ? "Constructor" : "Receiver");
        }

        var adjust = HirTables.AdjustOf(node);
        return adjust.Adaptation == HirAdaptation.None && adjust.Acquire is HirAcquire.Unset or HirAcquire.Copy or HirAcquire.Move or HirAcquire.Temporary or HirAcquire.None
            ? key : $"{key} {adjust.Acquire}/{adjust.Adaptation}";
    }

    private static string Dump(Compilation c)
    {
        var text = new StringBuilder();
        var program = c.BuildMir();
        MirDump.Write(program, 0, text);
        MirDump.Write(program, c.Library.Kotonoha.Syntax.Module, text);
        return text.ToString();
    }

    // What a location's PairAcquire operands and PairLayer steps are under the selected view, each step with its selected Place's case Type.
    private readonly struct Resolver(MirProjection[] projections, MirCases cases, List<string> log) : IMirUses
    {
        public void Operand(in MirOperand operand)
        {
            if (operand.Kind == MirOperandKind.PairAcquire)
            {
                log.Add($"acquire _{operand.Place.Root} {(cases.Reborrows(operand) ? "reborrow" : "copy")}");
            }
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            for (var id = place.Projection; id != 0; id = projections[id].Prefix)
            {
                if (projections[id].Kind == MirProjectionKind.PairLayer)
                {
                    var layer = cases.Layer(place with { Projection = id });
                    log.Add($"@b{projections[id].Arg} {(layer is null ? "itself" : "*" + TyCtxt.Name(layer))}: {TyCtxt.Name(cases.Type(projections[id].Type!))}");
                }
            }
        }

        public void Effect(MirEffect effects, int site)
        {
        }
    }
}
