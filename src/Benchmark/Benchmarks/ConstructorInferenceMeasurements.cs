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

internal static class ConstructorInferenceMeasurements
{
    private const int Warmup = 32;
    private const int Iterations = 64;
    private const int Samples = 7;

    internal static void Run()
    {
        // Cold means a new Compilation in this prepared process, not an empty OS/framework cache.
        _ = Create("let n: i32 = 0", false).Bind();
        var inputs = new List<(string Axis, int Size, bool Explicit, bool Reference)>();
        foreach (var axis in new[] { "candidates", "slots", "depth", "calls" })
        {
            var sizes = axis switch
            {
                "slots" => new[] { 1, 16, 65 },
                "depth" => new[] { 1, 4, 8 },
                "calls" => new[] { 1, 8, 32 },
                _ => new[] { 4, 8, 16 },
            };
            foreach (var size in sizes)
            {
                inputs.Add((axis, size, false, false));
                inputs.Add((axis, size, true, false));
            }
        }

        inputs.Add(("candidates", 16, false, true));
        foreach (var failure in new[] { "ambiguous", "unbound", "conflict", "correlation", "selection" })
        {
            inputs.Add((failure, 1, false, false));
            inputs.Add((failure, 1, false, true));
        }

        var results = new List<object>();
        foreach (var input in inputs)
        {
            var source = ConstructorInferenceWorkloads.Create(input.Axis, input.Size, input.Explicit);
            var expected = input.Axis is "candidates" or "slots" or "depth" or "calls";
            var coldBefore = GC.GetAllocatedBytesForCurrentThread();
            var coldStart = Stopwatch.GetTimestamp();
            var c = Create(source, input.Reference);
            if (c.Bind().IsComplete != expected)
            {
                throw new InvalidOperationException($"Unexpected cold outcome: {input}");
            }

            var coldMilliseconds = Stopwatch.GetElapsedTime(coldStart).TotalMilliseconds;
            var coldBytes = GC.GetAllocatedBytesForCurrentThread() - coldBefore;
            for (var i = 0; i < Warmup; i++)
            {
                Check();
            }

            var milliseconds = new double[Samples];
            var bytes = new long[Samples];
            for (var sample = 0; sample < Samples; sample++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < Iterations; i++)
                {
                    Check();
                }

                milliseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Iterations;
                bytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            results.Add(new
            {
                axis = input.Axis,
                size = input.Size,
                explicitType = input.Explicit,
                reference = input.Reference,
                accepted = expected,
                sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
                coldMilliseconds,
                coldBytes,
                millisecondsPerIteration = milliseconds,
                bytesPerSample = bytes,
                counters = c.Binding.InferenceMetrics,
            });

            void Check()
            {
                if (c.Bind().IsComplete != expected)
                {
                    throw new InvalidOperationException($"Unexpected warm outcome: {input}");
                }
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                compiler = Compilation.CompilerVersion,
                configuration = typeof(ConstructorInferenceMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                warmupIterations = Warmup,
                iterationsPerSample = Iterations,
                samples = Samples,
                results,
            },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Compilation Create(string source, bool reference)
    {
        var c = Compilation.CreateForTest();
        if (!c.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Compiler target must be prepared.");
        }

        c.Binding.MeasureCallInference = true;
        c.Binding.UseConstructorReferenceCheck = reference;
        c.Kotonoha.AddSource(new SourceDocument("construction.kimi", source));
        return c;
    }
}
