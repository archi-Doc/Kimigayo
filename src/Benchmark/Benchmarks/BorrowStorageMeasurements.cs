// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using Kimi.Compiler;
using Verification;

namespace Benchmark;

// Fixed workload and warm-up; regression assertions live in BorrowDependencyStorageTest.
internal static class BorrowStorageMeasurements
{
    internal static void Run()
    {
        var results = new List<object>();
        foreach (var workload in new[] { (1, false), (32, false), (128, false), (1, true), (32, true), (128, true) })
        {
            var (count, stored) = workload;
            var c = Compilation.CreateForTest();
            if (!c.Prepare("x86_64-pc-windows-msvc"))
            {
                throw new InvalidOperationException("Measurement target must be prepared.");
            }

            c.Kotonoha.AddSource(new SourceDocument("inspection.kimi", VerificationWorkloads.InspectionLoans(count, stored)));
            if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete)
            {
                throw new InvalidOperationException("Inspection workload must bind.");
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            Analyze();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            for (var warm = 0; warm < 8; warm++)
            {
                Analyze();
            }

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

            var body = c.Ownership.Bodies.Single(static x => x.Function.Name == "check");
            results.Add(new { count, stored, places = body.Places.Count, operations = body.Operations.Count, firstAnalysisBytes = allocated, dependencyCells = body.BorrowDependencyCapacity, tableBytes = body.BorrowStorageBytes, milliseconds = samples });

            void Analyze()
            {
                if (!c.Ownership.Analyze().IsVerified)
                {
                    throw new InvalidOperationException("Inspection workload must pass ownership verification.");
                }
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
