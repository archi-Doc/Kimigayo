// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Benchmark;

/// <summary>Measures fresh compilation; the phase runner uses the same six-stage pipeline.</summary>
[Config(typeof(BenchmarkConfig))]
public class CompilerPipelineBenchmark
{
    /// <summary>The number of measurement boundaries: one before each stage and one after the last.</summary>
    internal const int Boundaries = 7;

    private string source = string.Empty;

    /// <summary>Gets or sets the input size: the helper functions of a scalar shape, each called from main, or the item Types
    /// of the generic shape.</summary>
    [Params(1, 32, 64, 128)]
    public int Size { get; set; }

    /// <summary>Gets or sets the input family.</summary>
    [ParamsAllValues]
    public PipelineShape Shape { get; set; }

    internal string Source => this.source;

    /// <summary>Gets the number of MIR bodies each compilation builds: the source's and the library's.</summary>
    internal int MirBodies { get; private set; }

    /// <summary>Constructs the input outside measurement and validates full emission.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.source = this.Shape == PipelineShape.Generic ? GenericSource(this.Size) : this.ScalarSource();
        using var writer = new StringWriter();
        this.MirBodies = this.Compile(writer).BuildMir().Bodies.Length;
        this.Require(writer.GetStringBuilder().Length > 0, "Emission produced no IR.");
    }

    /// <summary>Measures the complete fresh pipeline without per-stage instrumentation.</summary>
    [Benchmark]
    public void FreshCompileToIr() => this.Compile(TextWriter.Null);

    // Each item Type instantiates Array, Dictionary, Option, a two-parameter generic struct and two generic functions under a
    // Contract bound, so instance collection and code generation grow with the Type count.
    internal static string GenericSource(int types)
    {
        var builder = new StringBuilder("contract Weighted\n    func weight(self: ref/Self) -> i64\n\n");
        builder.Append("struct Pair<A, B>\n    public let first: A\n    public let second: B\n    public init(first: A, second: B)\n");
        builder.Append("        self.first = first@move\n        self.second = second@move\n\n");
        builder.Append("func total<T>(items: ref/Array<T>) -> i64\n    T is Weighted\n    var sum: i64 = 0\n    for item in items\n");
        builder.Append("        sum += item.weight()\n    return sum\n\n");
        builder.Append("func pairWeight<A, B>(pair: ref/Pair<A, B>) -> i64\n    A is Weighted\n    B is Weighted\n");
        builder.Append("    return pair.first.weight() + pair.second.weight()\n\n");
        for (var i = 0; i < types; i++)
        {
            var next = (i + 1) % types;
            builder.Append($"struct Item{i}\n    Self is Weighted\n    public let value: i64\n    public init(value: i64) => self.value = value\n");
            builder.Append($"    public func weight(self: ref/Self) -> i64 => self.value + {i}\n\n");
            builder.Append($"func use{i}() -> i64\n    var items: Array<Item{i}> = []\n    items.append(Item{i}.init(1))\n");
            builder.Append($"    var lookup: Dictionary<i64, Item{i}> = [:]\n    _ = lookup.tryInsert(1, Item{i}.init(2))\n");
            builder.Append($"    let pair = Pair<Item{i}, Item{next}>.init(Item{i}.init(3), Item{next}.init(4))\n");
            builder.Append("    var sum = total(items@ref) + pairWeight(pair@ref)\n    match lookup.tryGet(1)\n");
            builder.Append("        .Some(let found) => sum += found.weight()\n        .None => ()\n    return sum\n\n");
        }

        builder.Append("public func main() -> ()\n    var result: i64 = 0\n");
        for (var i = 0; i < types; i++)
        {
            builder.Append("    result += use").Append(i).Append("()\n");
        }

        return builder.Append("    return\n").ToString();
    }

    // Each operation owns a fresh Compilation. Boundaries are contiguous; lowering runs only once. Stage 3 builds, validates and analyzes
    // the MIR and collects its checked set (BuildMir), each memoized per program version, so stage 4's Analyze, which runs the same on
    // the legacy and the mir pipeline (DECISIONS R5-M156), reuses them and keeps them for the generation gate. The old analysis still
    // decides (R3-R6). Stage 5 is empty and stage 6 is the generation entry, MIR code generation (R5-M94, R5-M160).
    internal Compilation Compile(TextWriter output, Span<long> timestamps = default, Span<long> allocations = default)
    {
        if (!(timestamps.IsEmpty && allocations.IsEmpty) && (timestamps.Length != Boundaries || allocations.Length != Boundaries))
        {
            throw new ArgumentException("Six-stage measurements require seven timestamp and allocation boundaries.");
        }

        Capture(0, timestamps, allocations);
        var result = Compilation.CreateForTest();
        this.Require(result.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        result.Kotonoha.AddSource(new SourceDocument("CompilerPipeline.kimi", this.source));
        this.Require(!result.Diagnostics.HasErrors, "Parsing failed.");
        Capture(1, timestamps, allocations);

        this.Require(result.Bind().IsComplete, "Binding failed.");
        this.Require(result.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        Capture(2, timestamps, allocations);

        var mir = result.BuildMir();
        this.Require(mir.Records.Count == 0 && mir.Findings.Count == 0, "MIR construction or validation failed.");
        Capture(3, timestamps, allocations);

        this.Require(result.Analyze().IsVerified, "Analysis failed.");
        Capture(4, timestamps, allocations);

        // The collector laid out every by-value use in stage 3, so MIR code generation has no layout stage (DECISIONS R5-M94).
        Capture(5, timestamps, allocations);
        var generated = result.Generate(output);
        this.Require(generated, generated ? string.Empty : "Generation failed: " + string.Join(", ", result.Diagnostics
            .Finalize(DiagnosticPartition.Emission, DiagnosticPartition.Emission).Diagnostics.Select(static x => x.Code)));
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

    // A failure names its input, so a run that stops at one is identifiable without counting inputs.
    private void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"size={this.Size}, shape={this.Shape}: {failure}");
        }
    }

    private string ScalarSource()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < this.Size; i++)
        {
            builder.Append("func helper").Append(i).Append("(value: i32) -> i32\n");
            if (this.Shape == PipelineShape.Branching)
            {
                builder.Append("    if value < 0\n        return 0\n");
            }

            builder.Append("    return value + 1\n\n");
        }

        builder.Append("public func main() -> ()\n    var result: i32 = 0\n");
        for (var i = 0; i < this.Size; i++)
        {
            builder.Append("    result = helper").Append(i).Append("(result)\n");
        }

        return builder.Append("    return\n").ToString();
    }
}

/// <summary>The input families of the compiler pipeline measurements.</summary>
public enum PipelineShape
{
    /// <summary>Scalar helper functions.</summary>
    Scalar,

    /// <summary>Scalar helper functions with a conditional return.</summary>
    Branching,

    /// <summary>Item Types, each instantiating library and user generics.</summary>
    Generic,
}
