// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures owned match dispatch, recursive payload decomposition and cleanup.</summary>
[Config(typeof(BenchmarkConfig))]
public class MatchOwnershipBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of matches with nested enum payloads.</summary>
    [Params(1, 32, 128)]
    public int Matches { get; set; }

    /// <summary>Builds and warms the allocation-regression workload without timed parsing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder("func f()\n");
        for (var i = 0; i < this.Matches; i++)
        {
            source.Append("    match Option<Option<string>>.Some(.Some(\"text\"))\n        .Some(.Some(let s)) =>\n            writeLine(s)\n        .Some(_) =>\n            ()\n        .None =>\n            ()\n");
        }

        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark target must be prepared.");
        }

        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, source.ToString());
        if (this.compilation.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !this.compilation.Bind().IsComplete || !this.compilation.Ownership.Analyze().IsVerified)
        {
            throw new InvalidOperationException("Benchmark source must pass ownership verification.");
        }
    }

    /// <summary>Rebuilds and solves ownership plus result flow using retained Binding.</summary>
    /// <returns>The verification summary.</returns>
    [Benchmark]
    public OwnershipResult Analyze() => this.compilation.Ownership.Analyze();

    /// <summary>Rebinds and runs both analyses without reparsing.</summary>
    /// <returns>The verification summary.</returns>
    [Benchmark]
    public OwnershipResult BindAndAnalyze()
    {
        this.compilation.Bind();
        return this.compilation.Ownership.Analyze();
    }
}
