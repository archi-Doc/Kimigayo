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
    // KIMI_MIR_CENSUS naming a file, writes the census there as TSV. Every built body is valid.
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

    // The same compilation builds the same MIR, fresh or after a warm rebind, and a fresh compilation of the same source does too.
    [Fact]
    public void BuildIsDeterministic()
    {
        var first = Dump(SemanticSnapshotTest.Bind("milestone/Milestone26").Compilation);
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone26");
        Assert.Equal(first, Dump(c));
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(first, Dump(c));
        Assert.Contains("opaque", first, StringComparison.Ordinal);
    }

    // Plan 6.2: a form without a building rule is one record at its location, however many bodies meet it, and an Opaque value naming the
    // locals its syntax touches; Binding's Error is a derived Opaque without a record; building goes on after both.
    [Fact]
    public void UnsupportedFormsAreOpaqueAtOneRecord()
    {
        var c = Compile("struct Point\n    public var x: i32 = 7\n    public init() => ()\n    public init(y: i32) => ()\n" +
            "func f(a: i32, b: i32) -> i32\n    let c = a < b\n    return b\nfunc g() -> i32 => missing\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "g").ToDictionary(static x => x.Key.Decl!.Name);
        var compare = bodies["f"];
        Assert.True(compare.HasOpaque);
        Assert.Equal([1, 2], compare.OpaqueRoots.ToArray());
        Assert.Equal(MirTerminatorKind.Return, compare.Blocks.ToArray().Last(static x => x.Terminator.Kind != MirTerminatorKind.Unreachable).Terminator.Kind);

        var field = c.SyntaxOf(Assert.Single(program.Unsupported, x => x.Module == 0 && c.SyntaxOf(x) is PropertyKoto));
        Assert.Equal(2, program.Bodies.ToArray().Count(x => x.OpaqueSites.ToArray().Any(y => ReferenceEquals(c.SyntaxOf(y.Source), field))));
        var error = bodies["g"];
        Assert.Equal(HirOp.Error, HirTables.Op(c.SyntaxOf(error.OpaqueSites[0].Source)));
        Assert.DoesNotContain(error.OpaqueSites[0].Source, program.Unsupported);
    }

    // V11: publishing, building and validating an unchanged compilation again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRebuildAllocatesNothing()
    {
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone26");
        var (bodies, version) = (c.BuildMir().Bodies.Length, c.BuildMir().Version);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
        Assert.Equal((bodies, version + 16), (c.BuildMir().Bodies.Length, c.BuildMir().Version)); // Eight warm-up and eight measured rebuilds.
    }

    internal static Compilation Compile(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
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
