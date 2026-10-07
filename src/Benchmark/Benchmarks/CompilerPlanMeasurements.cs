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

// Fixed workloads are shared with regression tests; timing is opt-in, never a regression assertion.
internal static class CompilerPlanMeasurements
{
    private const int Warmup = 32;
    private const int Iterations = 64;
    private const int Samples = 7;

    internal static void Run(bool callable = false, bool views = false, bool regions = false, bool properties = false, bool inheritance = false, bool adaptations = false, bool arithmetic = false)
    {
        var allPhases = callable || views || regions || properties || inheritance || adaptations || arithmetic;
        var results = new List<object>();
        var workloads = arithmetic ? ArithmeticWorkloads.Names.Select(static name => (name, 0)).ToArray() : adaptations
            ? new[] { ("adaptation-cases", 1), ("adaptation-cases", 8), ("adaptation-cases", 32) }
            : inheritance
            ? new[] { ("milestone25", 0), ("inheritance-depth", 1), ("inheritance-depth", 8), ("inheritance-depth", 32), ("inheritance-width", 8), ("inheritance-width", 32) }
            : regions
            ? new[] { ("candidates", 4), ("candidates", 8), ("candidates", 16), ("results", 4), ("results", 8), ("results", 16), ("regions", 4), ("regions", 8), ("regions", 16) }
            : (properties ? new[] { "milestone24" } : views ? new[] { "exclusive-views", "slice-copies" } : callable ? new[] { "fixed-reference", "ranked-reference", "borrowed-reference", "nested-universal" } : new[] { "direct", "stored", "rc-clone", "arc-clone" }).Select(static name => (name, 0)).ToArray();
        foreach (var (name, size) in workloads)
        {
            var stored = name == "stored";
            var source = arithmetic ? ArithmeticWorkloads.Create(name) : name switch
            {
                "milestone24" => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/milestones/Milestone24.kimi")),
                "milestone25" => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/milestones/Milestone25.kimi")),
                "adaptation-cases" => size == 1 ? AdaptationWorkloads.Single : AdaptationWorkloads.Create(size),
                "inheritance-depth" => VerificationWorkloads.InheritedPlans(size, 1),
                "inheritance-width" => VerificationWorkloads.InheritedPlans(4, size),
                "candidates" or "results" or "regions" => VerificationWorkloads.CallableRegions(name, size),
                "exclusive-views" => VerificationWorkloads.ExclusiveViews,
                "slice-copies" => VerificationWorkloads.SliceCopies,
                "fixed-reference" => VerificationWorkloads.ContextualFunctionReference,
                "ranked-reference" => VerificationWorkloads.FunctionReferenceRanking(false),
                "borrowed-reference" => VerificationWorkloads.FunctionReferenceRanking(true),
                "nested-universal" => VerificationWorkloads.NestedInferenceAndUniversalErasure,
                "rc-clone" or "arc-clone" => VerificationWorkloads.SharedClone(name == "arc-clone"),
                _ => VerificationWorkloads.ObjectView(stored),
            };
            var c = Compilation.CreateForTest();
            if (!c.Prepare("x86_64-pc-windows-msvc"))
            {
                throw new InvalidOperationException("Compiler workload target must be prepared.");
            }

            c.Kotonoha.AddSource(new SourceDocument(arithmetic ? "arithmetic-plans.kimi" : adaptations ? "adaptation-plans.kimi" : inheritance ? "inheritance.kimi" : properties ? "Milestone24.kimi" : regions ? "local-regions.kimi" : views ? "view-plans.kimi" : callable ? "callable-plans.kimi" : "object-plans.kimi", source));
            if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete || !c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Compiler workload must bind and verify.");
            }

            foreach (var phase in allPhases ? new[] { "binding", "ownership", "emission" } : new[] { "ownership", "emission" })
            {
                if (allPhases && phase == "ownership" && (!c.Binding.CheckStartup(OutputKind.Application).IsComplete || !c.Ownership.Analyze().IsVerified))
                {
                    throw new InvalidOperationException("Rebound workload must pass startup and ownership before measurement.");
                }

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

                var packedTableBytes = c.Ownership.Bodies.Sum(static body => body.BorrowStorageBytes);
                var regionPayloadBytes = c.Ownership.Bodies.Sum(static body => body.LocalRegionStorageBytes);
                var regionIndexCapacity = c.Ownership.Bodies.Sum(static body => body.LocalRegionIndexCapacity);
                var peakRegionCells = c.Ownership.Bodies.Sum(static body => body.PeakLocalLoanCells);
                results.Add(new { name, size, stored, phase, packedTableBytes, regionPayloadBytes, regionIndexCapacity, peakRegionCells, sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))), millisecondsPerIteration = milliseconds, bytesPerSample = bytes });

                void RunOnce()
                {
                    var valid = phase switch
                    {
                        "binding" => c.Bind().IsComplete,
                        "emission" => c.Emission.WriteIr(TextWriter.Null, out _),
                        _ => c.Ownership.Analyze().IsVerified,
                    };
                    if (!valid)
                    {
                        throw new InvalidOperationException("Every measured compiler plan must remain valid.");
                    }
                }
            }
        }

        var report = new
        {
            compiler = Compilation.CompilerVersion,
            compilerModule = typeof(Compilation).Module.ModuleVersionId,
            benchmarkModule = typeof(CompilerPlanMeasurements).Module.ModuleVersionId,
            configuration = typeof(CompilerPlanMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
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
