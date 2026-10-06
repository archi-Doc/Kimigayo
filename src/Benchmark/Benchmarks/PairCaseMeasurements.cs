// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using Kimi.Compiler;
using Verification;

namespace Benchmark;

// Fixed workload and warm-up for the Semantics-case runs of generic definitions (SPEC 8.10; PLAN G75); the zero-allocation
// assertion lives in GenericCaseAllocationTest, the family outcomes in GenericOwnershipCaseTest.
internal static class PairCaseMeasurements
{
    internal static void Run()
    {
        var results = new List<object>();
        foreach (var (name, source) in new[] { ("pair-case-families", VerificationWorkloads.PairCaseFamilies), ("borrow-dependency-reference", VerificationWorkloads.InspectionLoans(32, true)) })
        {
            var c = Compilation.CreateForTest();
            if (!c.Prepare("x86_64-pc-windows-msvc"))
            {
                throw new InvalidOperationException("Measurement target must be prepared.");
            }

            c.Kotonoha.AddSource(new SourceDocument(name + ".kimi", source));
            if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete)
            {
                throw new InvalidOperationException("The workload must bind.");
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            Analyze();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            for (var warm = 0; warm < 32; warm++)
            {
                Analyze();
            }

            var warmAllocated = GC.GetAllocatedBytesForCurrentThread();
            Analyze();
            warmAllocated = GC.GetAllocatedBytesForCurrentThread() - warmAllocated;
            var samples = new double[5];
            for (var sample = 0; sample < samples.Length; sample++)
            {
                var start = Stopwatch.GetTimestamp();
                for (var iteration = 0; iteration < 8; iteration++)
                {
                    Analyze();
                }

                samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 8;
            }

            var places = 0;
            var operations = 0;
            foreach (var body in c.Ownership.Bodies)
            {
                places += body.Places.Count;
                operations += body.Operations.Count;
            }

            var bodies = c.Ownership.Bodies.Count;
            for (var warm = 0; warm < 32; warm++)
            {
                if (!c.Bind().IsComplete)
                {
                    throw new InvalidOperationException("The workload must rebind.");
                }

                c.Binding.CheckStartup(OutputKind.Application);
            }

            var bindSamples = new double[5];
            for (var sample = 0; sample < bindSamples.Length; sample++)
            {
                var start = Stopwatch.GetTimestamp();
                for (var iteration = 0; iteration < 8; iteration++)
                {
                    if (!c.Bind().IsComplete)
                    {
                        throw new InvalidOperationException("The workload must rebind.");
                    }
                }

                bindSamples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 8;
                c.Binding.CheckStartup(OutputKind.Application);
            }

            results.Add(new { workload = name, bodies, places, operations, firstAnalysisBytes = allocated, warmAnalysisBytes = warmAllocated, warmAnalysisMilliseconds = samples, warmBindMilliseconds = bindSamples });

            void Analyze()
            {
                if (!c.Ownership.Analyze().IsVerified)
                {
                    throw new InvalidOperationException("The workload must pass ownership verification.");
                }
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
