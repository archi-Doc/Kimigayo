// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;

namespace Benchmark;

// The cost of retaining the selected alias and its written-Origin witness (G209). All 128 aliases and runtime targets are legal
// both before and after the change. Function Types deliberately contain implicit Origins; raw Types contain no written Origin.
// Prepare and source construction are outside intervals. Cold Bind receives parsed syntax; retained samples keep one whole
// Compilation alive in a separate frame, with no compilation from another sample retained by this driver.
internal static partial class AnalyzeCostMeasurements
{
    internal static void RunAliases()
    {
        var source = AliasSource();
        for (var i = 0; i < Warmups; i++)
        {
            BindAliasWorkload(ParsedAliases(source));
        }

        var warm = ParsedAliases(source);
        BindAliasWorkload(warm);
        var parse = new object[Samples];
        var coldBind = new object[Samples];
        var warmBind = new object[Samples];
        var retained = new long[Samples];
        for (var i = 0; i < Samples; i++)
        {
            parse[i] = AliasCost(Measure(PreparedAliases, c => ParseAliases(c, source)));
            coldBind[i] = AliasCost(Measure(() => ParsedAliases(source), BindAliasWorkload));
            warmBind[i] = AliasCost(Measure(() => warm, BindAliasWorkload));
            retained[i] = RetainedAliases(source);
        }

        var report = new
        {
            measuredAtUtc = DateTimeOffset.UtcNow,
            source,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            compilerModule = typeof(Compilation).Module.ModuleVersionId,
            benchmarkModule = typeof(AnalyzeCostMeasurements).Module.ModuleVersionId,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            stopwatchFrequency = Stopwatch.Frequency,
            aliases = 128,
            warmups = Warmups,
            samples = Samples,
            operationsPerSample = Samples * Iterations,
            parse,
            coldBind,
            warmBind,
            retainedCompilationBytes = retained,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(warm);
    }

    private static object AliasCost((long Ticks, long Bytes) cost)
        => new { milliseconds = Milliseconds(cost.Ticks), bytes = (double)cost.Bytes / (Samples * Iterations) };

    private static string AliasSource()
    {
        var source = new StringBuilder();
        for (var i = 0; i < 128; i++)
        {
            source.Append("alias Box").Append(i).Append(i % 2 == 0 ? "<raw/i32>\n" : "<(ref/i32) -> ref/i32>\n");
        }

        source.Append("struct Animal\nstruct Slot<T>\n");
        for (var i = 0; i < 128; i++)
        {
            source.Append("struct Box").Append(i).Append("<T>\n    public struct Inner").Append(i).Append('\n');
            source.Append("func check").Append(i).Append("(x: objref/Animal) -> bool => x is Slot<Inner").Append(i).Append(">\n");
        }

        return source.ToString();
    }

    private static Compilation PreparedAliases()
    {
        var c = Compilation.CreateForTest();
        Require(c.Prepare("x86_64-pc-windows-msvc"), "Alias target preparation failed.");
        return c;
    }

    private static Compilation ParsedAliases(string source)
    {
        var c = PreparedAliases();
        ParseAliases(c, source);
        return c;
    }

    private static void ParseAliases(Compilation c, string source)
    {
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        if (c.Diagnostics.HasErrors)
        {
            throw new InvalidOperationException(JsonSerializer.Serialize(c.Diagnostics.Finalize().Diagnostics));
        }
    }

    private static void BindAliasWorkload(Compilation c)
        => Require(c.Bind().IsComplete, "Alias Binding failed.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RetainedAliases(string source)
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var c = ParsedAliases(source);
        BindAliasWorkload(c);
        var bytes = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(c);
        return bytes;
    }
}
