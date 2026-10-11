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
    // V9: builds every body of the corpus and counts the bodies, those with an Opaque value and the records per HirOp; with KIMI_MIR_CENSUS
    // naming a file, writes the census there as TSV. Every built body is valid, and every static a body reaches through staticEnsure, every
    // Closure it creates and every default template it calls has its body (plan 5.6.2, 5.6.6).
    [Fact]
    public void CorpusBuildsValidBodies()
    {
        var rows = new List<string> { "case\tbodies\topaque\tunsupported\tderived" };
        var sites = new SortedDictionary<string, int>(StringComparer.Ordinal);
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
        output.WriteLine(string.Join('\n', rows));
        if (Environment.GetEnvironmentVariable("KIMI_MIR_CENSUS") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllLines(path, rows, new UTF8Encoding(false));
        }

        Assert.True(totals[0] > 500, $"{totals[0]} bodies");
    }

    // The same compilation builds the same MIR, fresh or after a warm rebind, and a fresh compilation of the same source does too; Milestone38
    // keeps Opaque values where Binding rejected it.
    [Fact]
    public void BuildIsDeterministic()
    {
        var first = Dump(SemanticSnapshotTest.Bind("milestone/Milestone38").Compilation);
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone38");
        Assert.Equal(first, Dump(c));
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(first, Dump(c));
        Assert.Contains("opaque", first, StringComparison.Ordinal);
    }

    // Plan 6.2: a form without a building rule is one record at its location, however many bodies meet it (a Field initializer's, built
    // into each constructor), and an Opaque value naming the locals its syntax touches; Binding's Error is a derived Opaque without a
    // record; building goes on after both. The unbuilt form is a fill of a length its instance decides, which no R3 unit builds.
    [Fact]
    public void UnsupportedFormsAreOpaqueAtOneRecord()
    {
        var c = Compile("struct Grid<length N>\n    public var cells: [N of i32] = [N of 7]\n    public init() => ()\n    public init(y: i32) => ()\n" +
            "func f<length N>(a: i32, b: i32) -> i32\n    let c: [N of i32] = [N of (a + b)]\n    return b\nfunc g() -> i32 => missing\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "g").ToDictionary(static x => x.Key.Decl!.Name);
        var fill = bodies["f"];
        Assert.True(fill.HasOpaque);
        Assert.Equal([1, 2], fill.OpaqueRoots.ToArray());
        Assert.Contains(fill.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Return);

        var field = c.SyntaxOf(Assert.Single(program.Records, x => x.Subject.Module == 0 && c.SyntaxOf(x.Subject).ToString() == "[N of 7]").Subject);
        Assert.Equal(2, program.Bodies.ToArray().Count(x => x.OpaqueSites.ToArray().Any(y => ReferenceEquals(c.SyntaxOf(y.Source), field))));
        var error = bodies["g"];
        Assert.Equal(HirOp.Error, HirTables.Op(c.SyntaxOf(error.OpaqueSites[0].Source)));
        Assert.DoesNotContain(program.Records, x => x.Subject == error.OpaqueSites[0].Source);
    }

    // V11: publishing, building and validating an unchanged compilation again allocates nothing, owning interpolations (Milestone41) included.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("milestone/Milestone26")]
    [InlineData("milestone/Milestone41")]
    public void WarmRebuildAllocatesNothing(string name)
    {
        var (c, _) = SemanticSnapshotTest.Bind(name);
        var (bodies, version) = (c.BuildMir().Bodies.Length, c.BuildMir().Version);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
        Assert.Equal((bodies, version + 16), (c.BuildMir().Bodies.Length, c.BuildMir().Version)); // Eight warm-up and eight measured rebuilds.
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

    // IMPL 21.3.5, plan 5.6.2: a copy that would take a body past 65,536 MIR statements is not made. Defaults that copy the one below
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
}
