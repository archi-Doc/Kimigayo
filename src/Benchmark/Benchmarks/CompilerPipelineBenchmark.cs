// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures fresh compilation and retained-state phases on the same scalable, valid input.</summary>
[Config(typeof(BenchmarkConfig))]
public class CompilerPipelineBenchmark
{
    private string source = string.Empty;
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of helper functions and calls from main.</summary>
    [Params(1, 32, 128)]
    public int Functions { get; set; }

    /// <summary>Gets or sets a value indicating whether helpers contain conditional control flow.</summary>
    [Params(false, true)]
    public bool Branches { get; set; }

    /// <summary>Validates full emission and warms the retained-state compilation.</summary>
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
        this.compilation = this.FreshParse();
        this.RebindAndStartup();
        this.ReanalyzeOwnership();
        using var writer = new StringWriter();
        Require(this.compilation.Emission.WriteIr(writer, out var failure), failure);
        Require(writer.GetStringBuilder().Length > 0, "Emission produced no IR.");
    }

    /// <summary>Measures new compilation state, target preparation, source registration and parsing.</summary>
    /// <returns>The parsed compilation, without semantic analysis.</returns>
    [Benchmark]
    public Compilation FreshParse()
    {
        var result = Compilation.CreateForTest();
        Require(result.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        result.Kotonoha.AddSource(new SourceDocument("CompilerPipeline.kimi", this.source));
        Require(!result.Diagnostics.HasErrors, "Parsing failed.");
        return result;
    }

    /// <summary>Measures fresh state through checked IR serialization, without disk or native tools.</summary>
    [Benchmark]
    public void FreshCompileToIr()
    {
        var result = this.FreshParse();
        Require(result.Bind().IsComplete, "Binding failed.");
        Require(result.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        Require(result.Ownership.Analyze().IsVerified, "Ownership verification failed.");
        Require(result.Emission.WriteIr(TextWriter.Null, out var failure), failure);
    }

    /// <summary>Measures rebinding existing syntax and checking startup with retained storage.</summary>
    [Benchmark]
    public void RebindAndStartup()
    {
        Require(this.compilation.Bind().IsComplete, "Binding failed.");
        Require(this.compilation.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
    }

    /// <summary>Measures control-flow and ownership analysis on an already bound compilation.</summary>
    [Benchmark]
    public void ReanalyzeOwnership()
        => Require(this.compilation.Ownership.Analyze().IsVerified, "Ownership verification failed.");

    /// <summary>Measures checked lowering and IR serialization on an already verified compilation.</summary>
    [Benchmark]
    public void ReemitIr()
        => Require(this.compilation.Emission.WriteIr(TextWriter.Null, out var failure), failure);

    private static void Require(bool condition, string? failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure ?? "Compiler benchmark failed.");
        }
    }
}
