// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Verification;

namespace Benchmark;

internal static class AssociatedInferenceMeasurements
{
    internal static void Run()
    {
        const int Warmup = 16;
        const int Iterations = 16;
        const int Samples = 5;
        _ = Create("let ready = 1").Bind();
        var results = new List<object>();
        foreach (var axis in new[] { "declarations", "requirements", "candidates", "depth", "shared", "refinement", "external", "unrelated" })
        {
            foreach (var size in axis is "depth" or "shared" ? new[] { 2, 4, 6 } : new[] { 1, 8, 24 })
            {
                foreach (var explicitBinding in new[] { true, false })
                {
                    var source = AssociatedInferenceWorkloads.Create(axis, size, explicitBinding);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    var compilation = Create(source);
                    Check();
                    var coldMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var coldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    for (var i = 0; i < Warmup; i++)
                    {
                        Check();
                    }

                    var milliseconds = new double[Samples];
                    var bytes = new long[Samples];
                    for (var sample = 0; sample < Samples; sample++)
                    {
                        GC.Collect();
                        before = GC.GetAllocatedBytesForCurrentThread();
                        start = Stopwatch.GetTimestamp();
                        for (var i = 0; i < Iterations; i++)
                        {
                            Check();
                        }

                        milliseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Iterations;
                        bytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                    }

                    double? editMilliseconds = null;
                    long? editBytes = null;
                    if (axis == "unrelated")
                    {
                        before = GC.GetAllocatedBytesForCurrentThread();
                        start = Stopwatch.GetTimestamp();
                        compilation.Kotonoha.AddSource(new("unrelated.kimi", "func unrelated() -> i32 => 1"));
                        Check();
                        editMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        editBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    }

                    results.Add(new { axis, size, explicitBinding, sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))), coldMilliseconds, coldBytes, millisecondsPerIteration = milliseconds, bytesPerSample = bytes, editMilliseconds, editBytes, storage = compilation.Binding.AssociatedMetrics });

                    void Check()
                    {
                        if (!compilation.Bind().IsComplete)
                        {
                            throw new InvalidOperationException($"Associated inference workload failed: {axis}/{size}/{explicitBinding}: {string.Join(';', compilation.Binding.Issues)}");
                        }
                    }
                }
            }
        }

        // R7-C4 (design §6, D2-C3): the cost per held state on one component (rings) and on a chain of one-definition components; a cold
        // Bind, then three warm ones, each with its states (InferenceStates' growth) and the time per state.
        var components = new List<object>();
        foreach (var (shape, size) in new[] { ("ring", 4), ("ring", 6), ("ring", 8), ("chain", 30), ("chain", 1000) })
        {
            var source = shape == "ring" ? AssociatedInferenceWorkloads.TwoValueRing(size) : AssociatedInferenceWorkloads.OneValueChain(size);
            var compilation = Create(source);
            var warm = new double[4];
            var states = new int[4];
            for (var sample = 0; sample < warm.Length; sample++)
            {
                var counted = compilation.Binding.InferenceStates;
                var start = Stopwatch.GetTimestamp();
                if (!compilation.Bind().IsComplete)
                {
                    throw new InvalidOperationException($"Associated component workload failed: {shape}/{size}: {string.Join(';', compilation.Binding.Issues)}");
                }

                warm[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                states[sample] = compilation.Binding.InferenceStates - counted;
            }

            components.Add(new { shape, size, millisecondsPerBind = warm, statesPerBind = states, microsecondsPerState = warm.Select((ms, i) => states[i] == 0 ? 0 : ms * 1000 / states[i]).ToArray() });
        }

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                compiler = Compilation.CompilerVersion,
                compilerModule = typeof(Compilation).Assembly.ManifestModule.ModuleVersionId,
                benchmarkModule = typeof(AssociatedInferenceMeasurements).Assembly.ManifestModule.ModuleVersionId,
                configuration = typeof(AssociatedInferenceMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                warmupIterations = Warmup,
                iterationsPerSample = Iterations,
                samples = Samples,
                results,
                components,
            },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Compilation Create(string source)
    {
        var compilation = Compilation.CreateForTest();
        if (!compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Compiler target must be prepared.");
        }

        compilation.Kotonoha.AddSource(new("associated-inference.kimi", source));
        return compilation;
    }
}
