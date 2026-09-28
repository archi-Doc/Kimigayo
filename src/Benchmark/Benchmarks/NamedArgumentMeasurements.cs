// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Benchmark;

/// <summary>Bounded lookup and real Binding measurements, including cold allocation.</summary>
internal static class NamedArgumentMeasurements
{
    public static void Run()
    {
        var results = new List<object>();
        foreach (var count in new[] { 2, 4, 8, 16, 32, 64 })
        {
            var parameters = string.Join(", ", Enumerable.Range(0, count).Select(i => "p" + i + ": i32"));
            var arguments = string.Join(", ", Enumerable.Range(0, count).Reverse().Select(i => "p" + i + ": " + i));
            var source = "func f(! " + parameters + ") => ()\n" + string.Concat(Enumerable.Repeat("f(" + arguments + ")\n", 32));
            var c = Compile(source);
            var function = (FunctionKoto)c.Kotonoha.GeneratedFunction!.Body!.Items[0];
            var names = function.Parameters.Select(p => new string(p.ExternalName.AsSpan())).Append("missing").ToArray();
            var indices = function.Parameters.Select((p, i) => (p.ExternalName, i)).ToDictionary(x => x.ExternalName, x => x.i, StringComparer.Ordinal);
            var lookupCount = 0;
            var checksum = 0;
            results.Add(new
            {
                parameters = count,
                // Each lookup sample cycles over all labels plus one unknown label.
                linear = Measure(
                    () =>
                    {
                        var name = names[lookupCount++ % names.Length];
                        var found = -1;
                        for (var i = 0; i < function.Parameters.Count; i++)
                        {
                            if (function.Parameters[i].ExternalName == name)
                            {
                                found = i;
                                break;
                            }
                        }

                        checksum ^= found;
                    },
                    200000),
                dictionary = Measure(() => checksum ^= indices.GetValueOrDefault(names[lookupCount++ % names.Length], -1), 200000),
                actualLookup = Measure(() => checksum ^= function.FindParameter(names[lookupCount++ % names.Length]), 200000),
                warmRebind32Calls = Measure(
                    () =>
                    {
                        if (!c.Binding.Bind(BindingMode.Final).IsComplete)
                        {
                            throw new InvalidOperationException("Rebind failed.");
                        }
                    },
                    32),
                coldParseAndBind32Calls = Measure(() => Compile(source), 8),
            });
            GC.KeepAlive(checksum);
        }

        Console.WriteLine(JsonSerializer.Serialize(new { compiler = Compilation.CompilerVersion, language = Compilation.CurrentLanguageVersion, runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Measure(Action action, int iterations)
    {
        for (var i = 0; i < Math.Min(iterations, 256); i++)
        {
            action();
        }

        var nanoseconds = new double[5];
        var allocations = new double[5];
        for (var sample = 0; sample < nanoseconds.Length; sample++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            nanoseconds[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
        }

        Array.Sort(nanoseconds);
        Array.Sort(allocations);
        return new { nanoseconds = nanoseconds[2], bytes = allocations[2] };
    }

    private static Compilation Compile(string source)
    {
        var c = Compilation.CreateForTest();
        if (!c.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Preparation failed.");
        }

        c.Kotonoha.AddSource(new SourceDocument("bench.kimi", source));
        if (!c.Bind().IsComplete)
        {
            throw new InvalidOperationException("Benchmark source failed to bind.");
        }

        return c;
    }
}
