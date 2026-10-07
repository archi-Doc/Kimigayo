// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Verification;

namespace Benchmark;

// Fresh snapshots complement the existing warm phase measurements. Process/library caches are deliberately shared.
internal static class InheritanceLifecycleMeasurements
{
    private const int Samples = 7;
    private const int Warmup = 32;
    private const int Iterations = 64;

    internal static void Run()
    {
        var results = new List<object>();
        foreach (var (depth, width) in new[] { (1, 1), (8, 1), (32, 1), (4, 32) })
        {
            var source = VerificationWorkloads.InheritedPlans(depth, width);
            var edited = source + "\ngroup MeasurementEdit\n    public func value() -> i32 => 37\n";
            var freshTimes = new double[Samples];
            var freshBytes = new long[Samples];
            var editTimes = new double[Samples];
            var editBytes = new long[Samples];
            Compilation current = null!;
            for (var sample = 0; sample < Samples; sample++)
            {
                Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                var original = Fresh(source);
                freshTimes[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                freshBytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;

                Collect();
                before = GC.GetAllocatedBytesForCurrentThread();
                start = Stopwatch.GetTimestamp();
                current = Fresh(edited);
                editTimes[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                editBytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(original); // The previous snapshot remains live while its replacement is checked.
            }

            for (var warm = 0; warm < Warmup; warm++)
            {
                Pipeline(current);
            }

            var storageBefore = Storage(current);
            var warmTimes = new double[Samples];
            var warmBytes = new long[Samples];
            for (var sample = 0; sample < Samples; sample++)
            {
                Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var iteration = 0; iteration < Iterations; iteration++)
                {
                    Pipeline(current);
                }

                warmTimes[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Iterations;
                warmBytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            var storageAfter = Storage(current);
            if (!storageBefore.Equals(storageAfter))
            {
                throw new InvalidOperationException("Unchanged inheritance checks grew retained ownership storage after the fixed warm-up.");
            }

            results.Add(new
            {
                depth,
                width,
                sourceSha256 = Hash(source),
                editedSourceSha256 = Hash(edited),
                freshMilliseconds = freshTimes,
                freshAllocatedBytes = freshBytes,
                editedMilliseconds = editTimes,
                editedAllocatedBytes = editBytes,
                warmMillisecondsPerIteration = warmTimes,
                warmBytesPerSample = warmBytes,
                storageBefore,
                storageAfter,
            });
        }

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                compiler = Compilation.CompilerVersion,
                compilerModule = typeof(Compilation).Module.ModuleVersionId,
                benchmarkModule = typeof(InheritanceLifecycleMeasurements).Module.ModuleVersionId,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processors = Environment.ProcessorCount,
                samples = Samples,
                warmupIterations = Warmup,
                iterationsPerWarmSample = Iterations,
                freshProcessPerSample = false,
                results,
            },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Compilation Fresh(string source)
    {
        var c = Compilation.CreateForTest();
        if (!c.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Measurement target must be prepared.");
        }

        c.Kotonoha.AddSource(new SourceDocument("inheritance-lifecycle.kimi", source));
        Pipeline(c);
        return c;
    }

    private static void Pipeline(Compilation c)
    {
        if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete ||
            !c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
        {
            throw new InvalidOperationException("Every inheritance snapshot must bind, verify and emit.");
        }
    }

    private static object Storage(Compilation c) => new
    {
        places = c.Ownership.Bodies.Sum(static body => body.Places.Count),
        operations = c.Ownership.Bodies.Sum(static body => body.Operations.Count),
        borrowTableBytes = c.Ownership.Bodies.Sum(static body => body.BorrowStorageBytes),
        regionPayloadBytes = c.Ownership.Bodies.Sum(static body => body.LocalRegionStorageBytes),
        regionIndexCapacity = c.Ownership.Bodies.Sum(static body => body.LocalRegionIndexCapacity),
    };

    private static string Hash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
