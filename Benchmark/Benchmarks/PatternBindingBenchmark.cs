// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures positional Pattern Binding, coverage and result flow without parsing.</summary>
[Config(typeof(BenchmarkConfig))]
public class PatternBindingBenchmark
{
    private Compilation compilation = null!;
    private ControlFlowAnalysis flow = null!;

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
        if (this.compilation.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !this.compilation.Bind().IsComplete)
        {
            throw new InvalidOperationException("Benchmark source must bind successfully.");
        }

        this.flow = this.compilation.AnalyzeControlFlow(this.compilation.Binding.TypeSystem);
        if (this.flow.PendingBinding.Count != 0 || this.flow.Issues.Count != 0)
        {
            throw new InvalidOperationException("Benchmark source must pass result-flow checking.");
        }
    }

    /// <summary>Rebinds patterns, arm scopes, acquisitions and coverage with retained storage.</summary>
    /// <returns>The Binding summary.</returns>
    [Benchmark]
    public BindingResult Bind() => this.compilation.Bind();

    /// <summary>Rebinds and checks control flow using the committed Pattern coverage.</summary>
    /// <returns>The Binding summary.</returns>
    [Benchmark]
    public BindingResult BindAndAnalyze()
    {
        var result = this.compilation.Bind();
        this.flow.Reanalyze(this.compilation.Kotonoha.RootKoto);
        return result;
    }
}
