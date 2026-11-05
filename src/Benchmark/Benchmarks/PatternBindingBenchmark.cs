// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures positional Pattern Binding, coverage and the check without parsing.</summary>
[Config(typeof(BenchmarkConfig))]
public class PatternBindingBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of independently bound matches.</summary>
    [Params(1, 32, 128)]
    public int Matches { get; set; }

    /// <summary>Builds and warms the nested Pattern workload used by allocation regressions.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder();
        for (var i = 0; i < this.Matches; i++)
        {
            source.Append("func matchExample").Append(i).Append("(x: Option<(i32, bool)>) -> i32 => match x\n    .Some((let n, _)) => n\n    .None => 0\n");
        }

        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark target must be prepared.");
        }

        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, source.ToString());
        if (this.compilation.Diagnostics.HasErrors || !this.compilation.Bind().IsComplete || !this.compilation.Analyze().IsVerified)
        {
            throw new InvalidOperationException("Benchmark source must bind and pass the check.");
        }
    }

    /// <summary>Rebinds patterns, arm scopes, acquisitions and coverage with retained storage.</summary>
    /// <returns>The Binding summary.</returns>
    [Benchmark]
    public BindingResult Bind() => this.compilation.Bind();

    /// <summary>Rebinds and checks the bodies (<see cref="Compilation.Analyze"/>) using the committed Pattern coverage.</summary>
    /// <returns>The analysis summary.</returns>
    [Benchmark]
    public AnalysisResult BindAndAnalyze()
    {
        this.compilation.Bind();
        return this.compilation.Analyze();
    }
}
