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

// Fixed workloads are shared with object regression tests; timing is opt-in, never a regression assertion.
internal static class ObjectPlanMeasurements
{
    private const int Warmup = 32;
    private const int Iterations = 64;
    private const int Samples = 7;

    internal static void Run()
    {
        var results = new List<object>();
        foreach (var name in new[] { "direct", "stored", "rc-clone" })
        {
            var stored = name == "stored";
            var source = name == "rc-clone" ? VerificationWorkloads.RcClone : VerificationWorkloads.ObjectView(stored);
            var c = Compilation.CreateForTest();
            if (!c.Prepare("x86_64-pc-windows-msvc"))
            {
                throw new InvalidOperationException("Object workload target must be prepared.");
            }

            c.Kotonoha.AddSource(new SourceDocument("object-plans.kimi", source));
            if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete || !c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Object workload must bind and verify.");
            }

            foreach (var emit in new[] { false, true })
            {
                for (var warm = 0; warm < Warmup; warm++)
                {
                    RunOnce();
                }

                var milliseconds = new double[Samples];
                var bytes = new long[Samples];
                for (var sample = 0; sample < Samples; sample++)
                {
                    // Every sample uses the same post-collection conditions; collection is outside the interval.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    for (var iteration = 0; iteration < Iterations; iteration++)
                    {
                        RunOnce();
                    }

                    var elapsed = Stopwatch.GetElapsedTime(start);
                    bytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                    milliseconds[sample] = elapsed.TotalMilliseconds / Iterations;
                }

                results.Add(new { name, stored, phase = emit ? "emission" : "ownership", sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))), millisecondsPerIteration = milliseconds, bytesPerSample = bytes });

                void RunOnce()
                {
                    if (emit ? !c.Emission.WriteIr(TextWriter.Null, out _) : !c.Ownership.Analyze().IsVerified)
                    {
                        throw new InvalidOperationException("Every measured object plan must remain valid.");
                    }
                }
            }
        }

        var report = new
        {
            compiler = Compilation.CompilerVersion,
            configuration = typeof(ObjectPlanMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
            warmupIterations = Warmup,
            iterationsPerSample = Iterations,
            samples = Samples,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
