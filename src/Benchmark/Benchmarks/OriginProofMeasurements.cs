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

// Origin proof measurements (PLAN G74). Each case runs in its own process with a time limit, so a pathological search is recorded as
// censored evidence instead of stopping the run; a limit is a measurement control, never a language rule.
internal static class OriginProofMeasurements
{
    private const int Warmup = 4;
    private const int Samples = 5;
    private const int CaseLimitMilliseconds = 30_000;
    private static readonly int[] Sizes = [1, 2, 4, 6, 8, 12, 16];

    internal static void Run()
    {
        var self = typeof(OriginProofMeasurements).Assembly.Location;
        var results = new List<JsonElement>();
        foreach (var (family, _) in OriginProofWorkloads.Families)
        {
            var censored = false;
            foreach (var size in Sizes)
            {
                if (censored)
                {
                    // A family that exceeded the limit at a smaller size is not retried larger; that is recorded as censored too.
                    results.Add(JsonSerializer.SerializeToElement(new { family, size, censored = true, skipped = true }));
                    continue;
                }

                using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{self}\" --origin-proof-case {family} {size}") { RedirectStandardOutput = true, UseShellExecute = false })!;
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(CaseLimitMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    censored = true;
                    results.Add(JsonSerializer.SerializeToElement(new { family, size, censored = true, limitMilliseconds = CaseLimitMilliseconds }));
                    continue;
                }

                results.Add(JsonDocument.Parse(output.Result).RootElement.Clone());
            }
        }

        var report = new
        {
            compiler = Compilation.CompilerVersion,
            compilerModule = typeof(Compilation).Module.ModuleVersionId,
            benchmarkModule = typeof(OriginProofMeasurements).Module.ModuleVersionId,
            configuration = typeof(OriginProofMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
            caseLimitMilliseconds = CaseLimitMilliseconds,
            warmupIterations = Warmup,
            samples = Samples,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    // One case: a cold check with its proof counters per phase, then warm repetitions while each stays short.
    internal static void RunCase(string family, int size)
    {
        var source = OriginProofWorkloads.Create(family, size);
        var compilation = Compilation.CreateForTest();
        if (!compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("The Origin proof workload target must be prepared.");
        }

        compilation.Kotonoha.AddSource(new SourceDocument("origin-proof.kimi", source));
        var metrics = new OriginProofMetrics();
        compilation.Binding.OriginProofMetrics = metrics;
        var start = Stopwatch.GetTimestamp();
        compilation.Bind();
        compilation.Binding.CheckStartup(OutputKind.Application);
        var binding = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var bindingRequests = metrics.Requests;
        var bindingClosures = metrics.Closures;
        start = Stopwatch.GetTimestamp();
        var verified = compilation.Ownership.Analyze().IsVerified;
        var ownership = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var codes = compilation.Binding.Issues.Select(static x => x.Code.ToString()).Concat(compilation.Ownership.Issues.Select(static x => x.Code.ToString())).ToArray();
        var cold = new
        {
            bindingMilliseconds = binding,
            ownershipMilliseconds = ownership,
            bindingRequests,
            bindingClosures,
            ownershipRequests = metrics.Requests - bindingRequests,
            ownershipClosures = metrics.Closures - bindingClosures,
            entries = metrics.Entries,
            nestedClosures = metrics.NestedClosures,
            nodes = metrics.Nodes,
            edges = metrics.Edges,
            meetIncidences = metrics.MeetIncidences,
            maxNodes = metrics.MaxNodes,
            maxEdges = metrics.MaxEdges,
            insertions = metrics.Insertions,
            activations = metrics.Activations,
            decrements = metrics.Decrements,
            boundViolations = metrics.BoundViolations,
            edgesByRule = Enum.GetValues<OriginPremiseRule>().Where(static x => x >= OriginPremiseRule.Declaration).ToDictionary(static x => x.ToString(), metrics.EdgesOf),
            distinctQueries = metrics.DistinctQueries,
        };

        // Warm repetitions only while one check stays below a second, under the fixed counts above.
        double[]? warm = null;
        if (binding + ownership < 1000)
        {
            compilation.Binding.OriginProofMetrics = null;
            for (var i = 0; i < Warmup; i++)
            {
                Check();
            }

            warm = new double[Samples];
            for (var i = 0; i < Samples; i++)
            {
                GC.Collect();
                start = Stopwatch.GetTimestamp();
                Check();
                warm[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            family,
            size,
            censored = false,
            verified,
            codes,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            cold,
            warmMilliseconds = warm,
        }));

        void Check()
        {
            compilation.Bind();
            compilation.Binding.CheckStartup(OutputKind.Application);
            compilation.Ownership.Analyze();
        }
    }
}
