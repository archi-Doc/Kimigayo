// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures whole-Place CFG construction, fixed points and cleanup plans.</summary>
[Config(typeof(BenchmarkConfig))]
public class OwnershipAnalysisBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of string locals and conditional calls.</summary>
    [Params(1, 32, 128)]
    public int Locals { get; set; }

    /// <summary>Gets or sets a value indicating whether each local owns a nested enum instead of a string.</summary>
    [Params(false, true)]
    public bool Enums { get; set; }

    /// <summary>Builds and warms the same plans used by the allocation regression.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder("func f(c: bool)\n");
        for (var i = 0; i < this.Locals; i++)
        {
            source.Append("    var s").Append(i);
            if (this.Enums)
            {
                source.Append(" = Option<Option<string>>.Some(Option<string>.Some(\"a\"))\n    if c\n        let moved").Append(i).Append(" = s").Append(i).Append('\n');
            }
            else
            {
                source.Append(" = \"a\"\n    if c\n        writeLine(s").Append(i).Append(")\n");
            }
        }

        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark target must be prepared.");
        }

        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, source.ToString());
        if (!this.compilation.Bind().IsComplete || !this.compilation.Ownership.Analyze().IsVerified)
        {
            throw new InvalidOperationException("Benchmark source must pass ownership verification.");
        }
    }

    /// <summary>Rebuilds both control-flow analyses and ownership plans with retained storage.</summary>
    /// <returns>The ownership verification summary.</returns>
    [Benchmark]
    public OwnershipResult Analyze() => this.compilation.Ownership.Analyze();

    /// <summary>Measures Binding and both analyses together, without reparsing.</summary>
    /// <returns>The ownership verification summary.</returns>
    [Benchmark]
    public OwnershipResult BindAndAnalyze()
    {
        this.compilation.Bind();
        return this.compilation.Ownership.Analyze();
    }
}
