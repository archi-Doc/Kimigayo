// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Verification;

namespace Benchmark;

// Fixed workload and warm-up for the Semantics-case runs of generic definitions (SPEC 8.15; PLAN G75); the zero-allocation
// assertion lives in GenericCaseAllocationTest, the family outcomes in GenericOwnershipCaseTest. Each workload names the check's
// expected verdict and fails on the other: the MIR rejects pair-case-families until PLAN G147 (8), as GenericCaseAllocationTest
// asserts (R6-check H9 OQ-2 (i)).
internal static class PairCaseMeasurements
{
    internal static void Run()
    {
        var results = new List<object>();
        foreach (var (name, source, verified) in new[] { ("pair-case-families", VerificationWorkloads.PairCaseFamilies, false), ("borrow-dependency-reference", VerificationWorkloads.InspectionLoans(32, true), true) })
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

            // The first Analyze builds, analyzes and collects this version's MIR; each later check is check's warm operation, Bind,
            // CheckStartup and Analyze (R6-check H9 D3).
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var first = c.Analyze().IsVerified;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Verdict(first);
            for (var warm = 0; warm < 32; warm++)
            {
                Check();
            }

            var warmAllocated = GC.GetAllocatedBytesForCurrentThread();
            Check();
            warmAllocated = GC.GetAllocatedBytesForCurrentThread() - warmAllocated;
            var samples = new double[5];
            for (var sample = 0; sample < samples.Length; sample++)
            {
                var start = Stopwatch.GetTimestamp();
                for (var iteration = 0; iteration < 8; iteration++)
                {
                    Check();
                }

                samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 8;
            }

            // The source's MIR bodies and their Semantics-case views.
            var (mirBodies, views) = (0, 0L);
            foreach (var body in c.BuildMir().Bodies)
            {
                if (!body.Source.IsLibrary)
                {
                    (mirBodies, views) = (mirBodies + 1, views + MirCases.Count(body));
                }
            }

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

            results.Add(new { workload = name, verified, mirBodies, views, firstAnalyzeBytes = allocated, warmCheckBytes = warmAllocated, warmCheckMilliseconds = samples, warmBindMilliseconds = bindSamples });

            void Check()
            {
                if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete)
                {
                    throw new InvalidOperationException("The workload must rebind.");
                }

                Verdict(c.Analyze().IsVerified);
            }

            void Verdict(bool actual)
            {
                if (actual != verified)
                {
                    throw new InvalidOperationException($"{name}: the check's verdict is {actual}, not the expected {verified}.");
                }
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
