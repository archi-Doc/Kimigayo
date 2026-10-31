// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The MIR build (docs/dev/COMPILER_ARCHITECTURE.md, R3): every body of the Kimi library and the milestone programs builds and
/// validates without a finding, a form the builder does not build yet is an Opaque value at its one located record, the build is
/// deterministic and a warm rebuild allocates nothing. The census measures what the builder covers (V9). This file holds the checks of the
/// build as a whole (the corpus, determinism, scaling and allocation) and the helpers the other files share; they hold the tests by what the
/// builder builds: Values, Defaults, Cases (Semantics case views), Calls (reservations and evaluation order) and Places.</summary>
public sealed partial class MirBuildTest(ITestOutputHelper output)
{
    // PLAN G308's forms over the compared Tuple of its DAG family P_n, each a format of the paths {0} = a.x…x and {1} = b.x…x: the tuple
    // operator (S1), a generic equality whose T is the Tuple, its Contract mapping (S4), and a Dictionary keyed by the Tuple (S6).
    internal const string DagOperator = "func f(a: ref/D1<i32>, b: ref/D1<i32>) -> bool => {0} == {1}\n";

    internal const string DagContract = "func same<T>(a: ref/T, b: ref/T) -> bool\n    T is Equatable\n    return a == b\n" +
        "func f(a: ref/D1<i32>, b: ref/D1<i32>) -> bool => same({0}, {1})\n";

    internal const string DagKey = "func fill<K>(key: K)\n    K is Copy and Equatable\n    var table: Dictionary<K, i32> = [:]\n" +
        "    _ = table.tryInsert(key, 1)\n    _ = table.tryGet(key)\nfunc f(a: ref/D1<i32>, b: ref/D1<i32>) => fill({0})\n";

    // A struct with an Array Field, an inspection of it and the start of a function f that binds a holder (SPEC 15.6.7).
    private const string Holder = "struct Holder\n    public var items: Array<i32>\n    public var count: i32\n    public init()\n" +
        "        self.items = []\n        self.count = 0\nfunc inspect(values: ref/Array<i32>) => ()\nfunc see(value: ref/i32) => ()\n" +
        "func f(x: i32)\n    var holder = Holder.init()\n";

    // The callees of the operand order tests and the start of their function f, which binds x (SPEC 12.2).
    private const string Operations = "func take(a: i32, b: ()) -> i32 => a\nfunc pair(t: (i32, ())) => ()\nfunc reset(p: uniq/i32) -> i32 => 0\n" +
        "func consume(a: string, b: ()) => ()\nfunc touch(p: uniq/Slice<i32>) -> isize => 0\nfunc jump(p: uniq/raw/i32) -> isize => 0\n" +
        "func f(view: Slice<i32>, raw: raw/i32)\n    var x: i32 = 1\n";

    // The callees of the evaluation order tests and the start of their function f, whose first parameter is x (SPEC 10.1.1, 7.3.4, 13.7.1).
    private const string Order = "func give2(a: i32, b: i32) => ()\nstruct Box\n    public var v: i32 = 0\n    public init() => ()\n" +
        "    public func put(a: i32, self: ref/Self) => ()\nstruct Gauge\n    public var level: i32 = 0\n        set(value: i32) -> ()\n" +
        "            storage = value\n    public init() => ()\nfunc f(x: i32, box: ref/Box)\n    var y: i32 = 0\n    var gauge = Gauge.init()\n";

    // An unbuilt form of a valid program: a tuple operator over two values of Tuple, whose leaf's storage nests past Binding's bound, has
    // no comparison (a Limit of TyCtxt.Comparison, DECISIONS R5-M75), so the MIR records one located Unsupported_Kd at the operator.
    // Declarations declares the leaf's Type. Since PLAN G167 the other unbuilt form is the second limit past that bound, a default template
    // the call's substitution cannot instantiate (MirBuilder.Defaults.cs, R4-M127); both are PD30's (e) row until R7.
    internal static (string Declarations, string Tuple) Unbuilt { get; } = (
        "struct Wrap<V>\n    Self is Equatable\n    public init() => ()\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n",
        "(" + string.Concat(Enumerable.Repeat("Wrap<", 80)) + "i32" + new string('>', 80) + ", i32)");

    // V9: builds every body of the corpus and counts the bodies, those with an Opaque value, the records per HirOp and the bodies per number
    // of Semantics case views; with KIMI_MIR_CENSUS naming a file, writes the census there as TSV. Every built body is valid and within the
    // case bound (plan 5.6.1), and every Closure it creates and every default template it calls has its body (plan 5.6.2; each
    // staticEnsure's initializer is the validator's, DECISIONS R4-M121).
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

    // SPEC 3.6, 16.2.1 (DECISIONS R4-M129): every temp with storage of its own, of a Type that may need drop and written whole, is made, so
    // its body drops it (a held temp that no Made registered has no Drop, and its value would leak), over the goldens, the library and the
    // milestone programs. A transfer's result temp has no storage of its own (DECISIONS R5-M50).
    [Fact]
    public void EveryWrittenTempIsMade()
    {
        var programs = MirDumpTest.Programs.Select(static x => ("golden/" + x.Data, MirDumpTest.Compile(x.Data)))
            .Concat(HirColumnsTest.Corpus.Select(static x => (x.Data, SemanticSnapshotTest.Bind(x.Data).Compilation)));
        foreach (var (name, c) in programs)
        {
            foreach (var body in c.BuildMir().Bodies)
            {
                var unmade = Unmade(body);
                Assert.True(unmade is null, $"{name}: {body.Key.Decl?.Name}'s _{unmade} is never dropped");
            }
        }
    }

    // SPEC 16.2.2 (DECISIONS R5-M50, R4-M132): the temp a transfer delivers whole into _0 after the Scope Exit is the body's result in
    // transit (MirLocalRole.Result): a return leaving a scope with a cleanup has one, a body's tail value writes _0 and has none, a
    // constructor's return none (its target is a Unit temp), a default template whose tail construct a transfer leaves one, at the
    // construct, and that template's copy in a caller none (it delivers into the caller's slot). Each is valid MIR.
    [Fact]
    public void TheResultInTransitIsTheTempDeliveredIntoTheResult()
    {
        var c = Compile("""
            func leave(x: ref/i32, c: bool) -> ref/i32 during x
                if c
                    let local = 1
                    var r = local@ref
                    return r
                return x
            func plain(x: i32) -> i32 => x
            struct Res
                public var v: i32 = 0
                public init(c: bool)
                    if c
                        let t = 1
                        return
                    self.v = 2
            func evaluate<T>(sample: T, marker: i32 = label result: do
                let pending: Option<T> = .None
                match pending@move
                    .Some(let value) => exit to result 1
                    .None => exit to result 0
            ) -> i32 => marker
            func caller() -> i32 => evaluate("held")
            """);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(
            "DefaultTemplate evaluate 1, Fn caller 0, Fn evaluate 0, Fn init 0, Fn leave 1, Fn plain 0",
            string.Join(", ", program.Bodies.ToArray().Where(static x => x.Source.Module == 0 && x.Key.Decl is not null)
                .Select(static x => $"{x.Key.Kind} {x.Key.Decl!.Name} {x.Locals.ToArray().Count(static y => y.Role == MirLocalRole.Result)}")
                .Order(StringComparer.Ordinal)));
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

    // G308 S1 (==), S2 (<, >=; V16): the program of a comparison over P_n's Tuple at n = 8, 12, 16 has a MIR (Blocks + Statements of every
    // body BuildMir returns) linear in n, the distinct Types (DropTreesScaleLinearly's ratio rule). Fails before G308: the comparison builds
    // each of its 2^n leaves, so the second increment is about 16 times the first.
    [Theory]
    [InlineData("==")]
    [InlineData("<")]
    [InlineData(">=")]
    public void ADagComparisonsMirGrowsWithItsDistinctTypes(string op)
    {
        var sizes = new[] { 8, 12, 16 };
        var work = sizes.Select(n =>
        {
            var program = Compile(Dag(n, DagOperator.Replace("==", op, StringComparison.Ordinal))).BuildMir();
            Assert.Empty(program.Findings);
            return program.Bodies.ToArray().Sum(static x => (long)x.Blocks.Length + x.Statements.Length);
        }).ToArray();
        output.WriteLine($"{op} N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"{op}: MIR size {string.Join(", ", work)}");
    }

    // G136 N2: a malformed fixture must fail before Binding can silently change the program under test.
    [Theory]
    [InlineData("func loop() => ()\n")]
    [InlineData("func f() => \"unterminated\n")]
    public void CompileRejectsSyntaxErrors(string source)
        => Assert.ThrowsAny<XunitException>(() => Compile(source));

    [Fact]
    public void CompileAllowsBindingErrors()
        => Assert.NotEmpty(Compile("func f() => missing\n").Binding.Issues);

    // A body's TestRecordValue calls, in block order.
    internal static MirTerminator[] Records(MirBody body) => [.. body.Blocks.ToArray().Select(static x => x.Terminator)
        .Where(static x => x is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.TestRecordValue } })];

    // PLAN G308's DAG family P_n with a form: D1 to Dn, D_k<T>'s Field x a D_{k+1}<(T, T)> and Dn's the pair (T, T), so a.x…x (n steps)
    // of a D1<i32> is a Tuple of 2^n leaves but n distinct composite Types; each init builds its value from one leaf.
    internal static string Dag(int n, string form)
    {
        var text = new StringBuilder();
        for (var k = 1; k <= n; k++)
        {
            var (field, value) = k < n ? ($"D{k + 1}<(T, T)>", $"D{k + 1}<(T, T)>.init((value, value))") : ("(T, T)", "(value, value)");
            text.Append($"struct D{k}<T>\n    T is Copy\n    public var x: {field}\n    public init(value: T) => self.x = {value}\n");
        }

        var steps = string.Concat(Enumerable.Repeat(".x", n));
        return text.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, form, "a" + steps, "b" + steps)).ToString();
    }

    internal static Compilation Compile(string source, bool test = false)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = test;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
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

    // The locations of a body's runtime path from its entry, as far as each block has one runtime successor: its statements, then its
    // terminator.
    private static IEnumerable<MirLocation> RuntimePath(MirBody body)
    {
        for (var block = 0; block >= 0; block = MirUses.Successors(body.Blocks[block].Terminator) == 1 ? body.Blocks[block].Terminator.Target : -1)
        {
            for (var i = 0; i <= body.Blocks[block].Count; i++)
            {
                yield return new(block, i);
            }
        }
    }

    // A Place as its root and its projections in order: a Deref as *, a Field as its position, so (*_1).0 is _1*.0.
    private static string Steps(MirBody body, MirPlace place) => place.Projection == 0 ? $"_{place.Root}"
        : Steps(body, place with { Projection = body.Projections[place.Projection].Prefix }) + body.Projections[place.Projection] switch
        {
            { Kind: MirProjectionKind.Deref } => "*",
            { Kind: MirProjectionKind.Field } row => $".{row.Arg}",
            var row => $"?{row.Kind}{row.Arg}",
        };

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

    // The first temp of a body with storage of its own, of a Type that may need drop, written whole and never dropped, or null.
    private static int? Unmade(MirBody body)
    {
        var (live, written, dropped) = (new HashSet<int>(), new HashSet<int>(), new HashSet<int>());
        foreach (var statement in body.Statements)
        {
            if (statement.Kind == MirStatementKind.StorageLive)
            {
                live.Add(statement.Place.Root);
            }
            else if (statement is { Kind: MirStatementKind.Assign, Place.Projection: 0 })
            {
                written.Add(statement.Place.Root);
            }
        }

        foreach (var block in body.Blocks)
        {
            if (block.Terminator is { Kind: MirTerminatorKind.Call or MirTerminatorKind.Drop, Place.Projection: 0 } terminator)
            {
                (terminator.Kind == MirTerminatorKind.Call ? written : dropped).Add(terminator.Place.Root);
            }
        }

        for (var local = 0; local < body.Locals.Length; local++)
        {
            if (body.Locals[local].Kind == MirLocalKind.Temp && live.Contains(local) && written.Contains(local) && !dropped.Contains(local) &&
                MirTypes.MayNeedDrop(body.Locals[local].Type, []))
            {
                return local;
            }
        }

        return null;
    }

    private static string Dump(Compilation c)
    {
        var text = new StringBuilder();
        var program = c.BuildMir();
        MirDump.Write(program, 0, text);
        MirDump.Write(program, c.Library.Kotonoha.Syntax.Module, text);
        return text.ToString();
    }
}
