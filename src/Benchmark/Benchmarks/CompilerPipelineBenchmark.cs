// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures fresh compilation; the phase runner uses the same six-stage pipeline.</summary>
[Config(typeof(BenchmarkConfig))]
public class CompilerPipelineBenchmark
{
    /// <summary>The number of measurement boundaries: one before each stage and one after the last.</summary>
    internal const int Boundaries = 7;

    private string source = string.Empty;

    /// <summary>Gets or sets the number of helper functions and calls from main.</summary>
    [Params(1, 32, 128)]
    public int Functions { get; set; }

    /// <summary>Gets or sets a value indicating whether helpers contain conditional control flow.</summary>
    [Params(false, true)]
    public bool Branches { get; set; }

    internal string Source => this.source;

    /// <summary>Gets the number of MIR bodies each compilation builds: the source's and the library's.</summary>
    internal int MirBodies { get; private set; }

    /// <summary>Constructs the input outside measurement and validates full emission.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < this.Functions; i++)
        {
            builder.Append("func helper").Append(i).Append("(value: i32) -> i32\n");
            if (this.Branches)
            {
                builder.Append("    if value < 0\n        return 0\n");
            }

            builder.Append("    return value + 1\n\n");
        }

        builder.Append("public func main() -> ()\n    var result: i32 = 0\n");
        for (var i = 0; i < this.Functions; i++)
        {
            builder.Append("    result = helper").Append(i).Append("(result)\n");
        }

        builder.Append("    return\n");
        this.source = builder.ToString();
        using var writer = new StringWriter();
        this.MirBodies = this.Compile(writer).BuildMir().Bodies.Length;
        Require(writer.GetStringBuilder().Length > 0, "Emission produced no IR.");
    }

    /// <summary>Measures the complete fresh pipeline without per-stage instrumentation.</summary>
    [Benchmark]
    public void FreshCompileToIr() => this.Compile(TextWriter.Null);

    // Each operation owns a fresh Compilation. Boundaries are contiguous; lowering runs only once. The MIR is built as the mir pipeline's
    // Analyze builds it, before the old analysis, which still decides (R3-R5).
    internal Compilation Compile(TextWriter output, Span<long> timestamps = default, Span<long> allocations = default)
    {
        if (!(timestamps.IsEmpty && allocations.IsEmpty) && (timestamps.Length != Boundaries || allocations.Length != Boundaries))
        {
            throw new ArgumentException("Six-stage measurements require seven timestamp and allocation boundaries.");
        }

        Capture(0, timestamps, allocations);
        var result = Compilation.CreateForTest();
        Require(result.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        result.Kotonoha.AddSource(new SourceDocument("CompilerPipeline.kimi", this.source));
        Require(!result.Diagnostics.HasErrors, "Parsing failed.");
        Capture(1, timestamps, allocations);

        Require(result.Bind().IsComplete, "Binding failed.");
        Require(result.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        Capture(2, timestamps, allocations);

        var mir = result.BuildMir();
        Require(mir.Records.Count == 0 && mir.Findings.Count == 0, "MIR construction or validation failed.");
        Capture(3, timestamps, allocations);

        Require(result.Ownership.Analyze().IsVerified, "Ownership verification failed.");
        Capture(4, timestamps, allocations);

        Require(result.Emission.TryPrepare(out var module, out var failure), failure);
        Capture(5, timestamps, allocations);

        module.WriteIr(output);
        Capture(6, timestamps, allocations);
        return result;
    }

    private static void Capture(int boundary, Span<long> timestamps, Span<long> allocations)
    {
        if (!timestamps.IsEmpty)
        {
            allocations[boundary] = GC.GetAllocatedBytesForCurrentThread();
            timestamps[boundary] = Stopwatch.GetTimestamp();
        }
    }

    private static void Require(bool condition, string? failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure ?? "Compiler benchmark failed.");
        }
    }
}
