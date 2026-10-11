// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
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
    // V9: builds every body of the corpus and counts the bodies, those with an Opaque value and the Unsupported records per HirOp; with
    // KIMI_MIR_CENSUS naming a file, writes the census there as TSV. Every built body is valid, and every static a body reaches through
    // staticEnsure and every Closure it creates has its body (plan 5.6.6).
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

            foreach (var site in program.Unsupported.Where(x => x.Module == module.Syntax.Module))
            {
                counts[2]++;
                var key = Key(c.SyntaxOf(site));
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
    // record; building goes on after both. The omitted default stays unbuilt until the default templates (R3 U14).
    [Fact]
    public void UnsupportedFormsAreOpaqueAtOneRecord()
    {
        var c = Compile("group G\n    public func scale(x: i32, factor: i32 = 2) -> i32 => x * factor\n" +
            "struct Point\n    public var x: i32 = G.scale(7)\n    public init() => ()\n    public init(y: i32) => ()\n" +
            "func f(a: i32, b: i32) -> i32\n    let c = G.scale(a + b)\n    return b\nfunc g() -> i32 => missing\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "g").ToDictionary(static x => x.Key.Decl!.Name);
        var compare = bodies["f"];
        Assert.True(compare.HasOpaque);
        Assert.Equal([1, 2], compare.OpaqueRoots.ToArray());
        Assert.Contains(compare.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Return);

        var field = c.SyntaxOf(Assert.Single(program.Unsupported, x => x.Module == 0 && c.SyntaxOf(x).ToString() == "G.scale(7)"));
        Assert.Equal(2, program.Bodies.ToArray().Count(x => x.OpaqueSites.ToArray().Any(y => ReferenceEquals(c.SyntaxOf(y.Source), field))));
        var error = bodies["g"];
        Assert.Equal(HirOp.Error, HirTables.Op(c.SyntaxOf(error.OpaqueSites[0].Source)));
        Assert.DoesNotContain(error.OpaqueSites[0].Source, program.Unsupported);
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
