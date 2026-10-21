// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Mir;

namespace Benchmark;

// A4's static keys on large programs (PLAN G93, DECISIONS R4-M38): N members of a call chain and F mutable static Fields. A cold A4 is a
// fresh MirEffects over a built program whose case views and dataflow are warm (A1-A3 warm them in the driver): Local per view, Run (the
// fixed point, then Spread), then a Keys query at every Call and Drop, as A5 reads them. The MIR build is outside every interval.
// "--static-keys" reports each phase's allocation (three fresh instances), the managed heap a fresh instance retains and a warm re-run's
// allocation; "--static-keys timing" reports pinned times per phase, the F programs of one shape and size interleaved.
internal static class StaticKeyMeasurements
{
    private const int Fresh = 3;
    private const int Warmups = 4;
    private const int Samples = 7;
    private const int Cpu = 2;
    private static readonly int[] Sizes = [256, 512, 1024, 2048];
    private static readonly int[] FieldCounts = [0, 16, 64, 256, 1024];
    private static readonly string[] Shapes = ["chain", "ring", "star"];

    internal static void Run(bool timing)
    {
        if (timing)
        {
            Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0", "Timing needs DOTNET_TieredCompilation=0: a pinned process otherwise measures tier-0 code.");
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.GetCurrentProcess();
                process.PriorityClass = ProcessPriorityClass.High;
                process.ProcessorAffinity = 1 << Cpu;
            }
        }

        var results = new List<object>();
        foreach (var shape in Shapes)
        {
            foreach (var size in Sizes)
            {
                results.AddRange(timing ? Times(shape, size) : FieldCounts.Select(fields => Allocations(shape, size, fields)));
            }
        }

        var report = new
        {
            schemaVersion = 1,
            mode = timing ? "timing" : "allocation",
            measuredAtUtc = DateTimeOffset.UtcNow,
            compiler = Compilation.CompilerVersion,
            compilerModule = typeof(Compilation).Module.ModuleVersionId,
            benchmarkModule = typeof(StaticKeyMeasurements).Module.ModuleVersionId,
            configuration = typeof(StaticKeyMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            pinnedCpu = timing && OperatingSystem.IsWindows() ? Cpu : -1,
            stopwatchFrequency = Stopwatch.Frequency,
            freshInstances = Fresh,
            warmups = timing ? Warmups : 0,
            samples = timing ? Samples : 0,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    // The workload: group G holds an immutable flag and the F Fields; member n of group H touches the Fields n mod F, then every N-th
    // (each Field touched), written by even members and read by odd ones. chain: each member calls the next and chainHead the first; ring:
    // the last member also calls the first under G.flag, so every member reaches every Field; star: chainHead calls each member.
    internal static string Source(string shape, int size, int fields)
    {
        var text = new StringBuilder("group G\n    public let flag: bool = false\n");
        for (var j = 0; j < fields; j++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    public var v{j}: i32 = 0\n");
        }

        var head = new StringBuilder(shape == "star" ? "func chainHead()\n" : "func chainHead() => H.step0()\n");
        text.Append("group H\n");
        for (var n = 0; n < size; n++)
        {
            var body = new StringBuilder();
            for (var j = n % Math.Max(fields, 1); j < fields; j += size)
            {
                body.Append(n % 2 == 0 ? "        G.v" : "        _ = G.v").Append(j).Append(n % 2 == 0 ? " = 1\n" : "\n");
            }

            if (shape == "star")
            {
                head.Append(CultureInfo.InvariantCulture, $"    H.step{n}()\n");
            }
            else if (n + 1 < size)
            {
                body.Append(CultureInfo.InvariantCulture, $"        H.step{n + 1}()\n");
            }
            else if (shape == "ring")
            {
                body.Append("        if G.flag => H.step0()\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"    public func step{n}(){(body.Length == 0 ? " => ()" : string.Empty)}\n").Append(body);
        }

        return text.Append(head).ToString();
    }

    // One program's allocation series: each phase's bytes over fresh instances (minimum, maximum), a warm re-run of the last one (a new
    // version of the same bodies, which Local starts), the heap a fresh instance retains after its pass, and the counts the key formula
    // reads.
    private static object Allocations(string shape, int size, int fields)
    {
        var w = Workload.Create(shape, size, fields);
        var cold = new Pass[Fresh];
        MirEffects? effects = null;
        for (var i = 0; i < Fresh; i++)
        {
            effects = w.Effects();
            cold[i] = Measure(effects, w);
        }

        w.Program.Version++;
        var warm = Measure(effects!, w);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var kept = w.Effects();
        _ = Measure(kept, w);
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(kept);
        var (bodies, nodes, statics) = (0, 0L, new HashSet<object>(ReferenceEqualityComparer.Instance));
        foreach (var body in w.Program.Bodies)
        {
            (bodies, nodes) = (bodies + 1, nodes + MirCases.Count(body));
            foreach (var item in body.Statics)
            {
                if (item.Mutable)
                {
                    _ = statics.Add(item.Symbol);
                }
            }
        }

        var words = ((2 * statics.Count) + 63) >> 6;
        return new
        {
            shape,
            size,
            fields,
            staticFields = statics.Count,
            bodies,
            nodes,
            words,
            formulaKeyWords = nodes * words,
            evaluations = effects!.Evaluations,
            dependencies = effects.Dependencies,
            sites = cold[0].Sites,
            unknownSites = cold[0].Unknown,
            keysReturned = cold[0].Keys,
            localBytes = MinMax(cold, static x => x.LocalBytes),
            runBytes = MinMax(cold, static x => x.RunBytes),
            queryBytes = MinMax(cold, static x => x.QueryBytes),
            warmBytes = warm.LocalBytes + warm.RunBytes + warm.QueryBytes,
            retainedBytes = retained,
        };
    }

    // One shape and size's timing series: the F programs alive together and interleaved round-robin, the first program rotating each
    // round, a full collection before each pass outside its intervals; the first rounds warm up.
    private static IEnumerable<object> Times(string shape, int size)
    {
        var workloads = FieldCounts.Select(fields => Workload.Create(shape, size, fields)).ToArray();
        var passes = workloads.Select(static _ => new Pass[Samples]).ToArray();
        for (var round = 0; round < Warmups + Samples; round++)
        {
            for (var i = 0; i < workloads.Length; i++)
            {
                var k = (i + round) % workloads.Length;
                var effects = workloads[k].Effects();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var pass = Measure(effects, workloads[k]);
                if (round >= Warmups)
                {
                    passes[k][round - Warmups] = pass;
                }
            }
        }

        return workloads.Select((w, k) => (object)new
        {
            shape,
            size,
            fields = w.Fields,
            localMilliseconds = Milliseconds(passes[k], static x => x.LocalTicks),
            runMilliseconds = Milliseconds(passes[k], static x => x.RunTicks),
            queryMilliseconds = Milliseconds(passes[k], static x => x.QueryTicks),
        }).ToArray();
    }

    // One pass of an instance, the driver's A4 rows as MirEffectsTest.Analyze runs them: each body's views' Local, then Run, then a Keys
    // query at every Call and Drop of every view A5 analyzes (A2's table); the dataflow is begun with each body outside the phases.
    private static Pass Measure(MirEffects effects, Workload w)
    {
        var (localBytes, localTicks) = (0L, 0L);
        foreach (var body in w.Program.Bodies)
        {
            w.Flow.Begin(body);
            var (bytes, start) = (GC.GetAllocatedBytesForCurrentThread(), Stopwatch.GetTimestamp());
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                w.Cases.Select(body, view);
                effects.Local(w.Program, body, view);
            }

            (localBytes, localTicks) = (localBytes + GC.GetAllocatedBytesForCurrentThread() - bytes, localTicks + Stopwatch.GetTimestamp() - start);
        }

        w.Report.Clear();
        var (runBytes, runTicks) = (GC.GetAllocatedBytesForCurrentThread(), Stopwatch.GetTimestamp());
        effects.Run(w.Program, w.Report);
        (runBytes, runTicks) = (GC.GetAllocatedBytesForCurrentThread() - runBytes, Stopwatch.GetTimestamp() - runTicks);
        var (queryBytes, queryTicks, sites, unknowns, keys) = (0L, 0L, 0, 0, 0);
        foreach (var body in w.Program.Bodies)
        {
            w.Flow.Begin(body);
            var (bytes, start) = (GC.GetAllocatedBytesForCurrentThread(), Stopwatch.GetTimestamp());
            for (var view = 0L; view < w.Program.Drops.ViewsOf(body.Ordinal); view++)
            {
                for (var block = 0; block < body.Blocks.Length; block++)
                {
                    if (body.Blocks[block].Terminator.Kind is MirTerminatorKind.Call or MirTerminatorKind.Drop)
                    {
                        keys += effects.Keys(body, view, block, out var unknown).Length;
                        (sites, unknowns) = (sites + 1, unknowns + (unknown ? 1 : 0));
                    }
                }
            }

            (queryBytes, queryTicks) = (queryBytes + GC.GetAllocatedBytesForCurrentThread() - bytes, queryTicks + Stopwatch.GetTimestamp() - start);
        }

        return new(localBytes, localTicks, runBytes, runTicks, queryBytes, queryTicks, sites, unknowns, keys);
    }

    private static long[] MinMax(Pass[] passes, Func<Pass, long> value) => [passes.Min(value), passes.Max(value)];

    private static object Milliseconds(Pass[] passes, Func<Pass, long> ticks)
    {
        var samples = passes.Select(x => ticks(x) * 1000.0 / Stopwatch.Frequency).ToArray();
        var sorted = samples.Order().ToArray();
        return new { min = sorted[0], median = sorted[sorted.Length / 2], samples };
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure);
        }
    }

    // A pass's phases (allocated bytes and ticks of Local, Run and the queries) and its queries' sites, unknown sites and keys.
    private readonly record struct Pass(long LocalBytes, long LocalTicks, long RunBytes, long RunTicks, long QueryBytes, long QueryTicks, int Sites, int Unknown, int Keys);

    // A built program of the workload with the case views and the dataflow A4 reads, warmed by one pass; its compilation stays alive.
    private sealed record Workload(int Fields, Compilation Compilation, MirProgram Program, MirCases Cases, MirDataflow Flow, MirReport Report)
    {
        internal static Workload Create(string shape, int size, int fields)
        {
            var c = Compilation.CreateForTest();
            Require(c.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
            c.Kotonoha.AddSource(new SourceDocument("StaticKeys.kimi", Source(shape, size, fields)));
            Require(c.Bind().IsComplete, "Binding failed.");
            var program = c.BuildMir();
            Require(program.Findings.Count == 0 && program.Records.Count == 0, "The MIR build failed.");
            var w = new Workload(fields, c, program, new(c.Types), new(new MirReach()), new());
            _ = Measure(w.Effects(), w);
            return w;
        }

        internal MirEffects Effects() => new(this.Compilation.Types, this.Cases, this.Flow);
    }
}
