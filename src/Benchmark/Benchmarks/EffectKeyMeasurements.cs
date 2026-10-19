// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Mir;

namespace Benchmark;

// G93: the first A4 over a built program with its A1/A2 inputs prepared, with no preceding A4 or warm-up. N functions form a forward call chain; F mutable
// statics are written once each, distributed over the chain. F also adds initializer bodies, which the report counts separately.
// A fresh process per sample, fixed affinity and interleaved input order belong to the caller. Reflection only reads storage counts
// after measurement; no production hooks or mutated private state. Run includes Spread; its time is not isolated.
internal static class EffectKeyMeasurements
{
    internal static void Run(int count, int fields)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(fields);
        var source = Source(count, fields);
        var c = Compilation.CreateForTest();
        Require(c.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        c.Kotonoha.AddSource(new SourceDocument("EffectKeys.kimi", source));
        Require(c.Bind().IsComplete, "Binding failed.");
        Require(c.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        var program = new MirBuilder(c).Build();
        Require(program.Findings.Count == 0, "MIR validation failed.");
        PrepareDrops(c, program);
        var result = Measure(c, program, fields);
        var output = new
        {
            compiler = Compilation.CompilerVersion,
            compilerModuleId = typeof(Compilation).Module.ModuleVersionId,
            compilerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Compilation).Assembly.Location))),
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount,
            affinity = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? Process.GetCurrentProcess().ProcessorAffinity.ToInt64() : (long?)null,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            precedingA4Runs = 0,
            count,
            fields,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            bodies = program.Bodies.Length,
            staticInitializers = program.Bodies.ToArray().Count(static b => b.Key.Kind == MirBodyKind.StaticInit),
            result,
        };
        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    }

    // The A1/A2 rows of MirAnalysis.Body, without its A4 Local or the driver's Run. A4 consumes their closed Drop table.
    private static void PrepareDrops(Compilation c, MirProgram program)
    {
        var cases = new MirCases(c.Types);
        var flow = new MirDataflow(new MirReach());
        var moves = new MirMoveData();
        var drops = new MirDrops(moves, cases);
        var init = new MirInit(flow, moves, cases, drops);
        foreach (var body in program.Bodies)
        {
            flow.Begin(body);
            moves.Build(body, flow);
            drops.Begin(body, program);
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                cases.Select(body, view);
                Require(init.Run(body, view, program.Problems), "A1 could not complete an input view.");
                drops.Judge(view);
            }

            drops.End();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Measure(Compilation c, MirProgram program, int expectedKeys)
    {
        var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var cases = new MirCases(c.Types);
        var flow = new MirDataflow(new MirReach());
        var report = new MirReport();
        var effects = new MirEffects(c.Types, cases, flow);
        foreach (var body in program.Bodies)
        {
            flow.Begin(body);
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                cases.Select(body, view);
                effects.Local(program, body, view);
            }
        }

        var localEnd = Stopwatch.GetTimestamp();
        var localBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        effects.Run(program, report);
        var end = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var retained = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;
        Require(report.All.Length == 0, "A4 rejected the input.");
        var nodes = Field<int>(effects, "nodeCount");
        var words = Field<int>(effects, "keyWords");
        var keyCapacity = Field<ulong[]>(effects, "keys").Length;
        var result = new
        {
            localMilliseconds = Stopwatch.GetElapsedTime(start, localEnd).TotalMilliseconds,
            runMilliseconds = Stopwatch.GetElapsedTime(localEnd, end).TotalMilliseconds,
            totalMilliseconds = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds,
            localAllocatedBytes = localBytes,
            runAllocatedBytes = allocated - localBytes,
            allocatedBytes = allocated,
            retainedBytes = retained,
            nodes,
            evaluations = effects.Evaluations,
            dependencies = Field<int>(effects, "dependentCount"),
            localKeys = Field<int>(effects, "keyCount"),
            keyWords = words,
            keyUsedBytes = (long)nodes * words * sizeof(ulong),
            keyCapacityBytes = (long)keyCapacity * sizeof(ulong),
        };
        var head = program.Bodies.ToArray().Single(static b => b.Key is { Kind: MirBodyKind.Fn, Decl.Name: "chainHead" });
        flow.Begin(head);
        var call = Enumerable.Range(0, head.Blocks.Length).Single(b => head.Blocks[b].Terminator is
            { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "step0" });
        var keys = effects.Keys(head, 0, call, out var unknown);
        Require(!unknown && keys.Length == expectedKeys, "The chain head did not publish exactly F static keys.");
        foreach (var key in keys)
        {
            Require(key.Write, "A written key was published as read-only.");
        }

        GC.KeepAlive(effects);
        GC.KeepAlive(report);
        GC.KeepAlive(c);
        return result;
    }

    private static T Field<T>(MirEffects effects, string name)
        => (T)(typeof(MirEffects).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(effects)
            ?? throw new InvalidOperationException($"Missing A4 storage: {name}."));

    private static string Source(int count, int fields)
    {
        var text = new StringBuilder();
        if (fields > 0)
        {
            text.Append("group G\n");
            for (var f = 0; f < fields; f++)
            {
                text.Append(CultureInfo.InvariantCulture, $"    public var v{f}: i32 = 0\n");
            }
        }

        text.Append("group H\n");
        for (var n = 0; n < count; n++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    public func step{n}()\n");
            for (var f = n; f < fields; f += count)
            {
                text.Append(CultureInfo.InvariantCulture, $"        G.v{f} = 1\n");
            }

            var next = n + 1 == count ? "()" : $"H.step{n + 1}()";
            text.Append(CultureInfo.InvariantCulture, $"        {next}\n");
        }

        return text.Append("func chainHead() => H.step0()\npublic func main() => chainHead()\n").ToString();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
