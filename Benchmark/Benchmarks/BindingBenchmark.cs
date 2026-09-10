// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures Binding independently of parsing and the reserved Mod boundary.</summary>
[Config(typeof(BenchmarkConfig))]
public class BindingBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of calls whose existing syntax is rebound.</summary>
    [Params(32, 512)]
    public int Calls { get; set; }

    /// <summary>Creates and validates syntax and warms reusable semantic storage.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder("func identity<T>(value: T) -> T => value\n");
        for (var i = 0; i < this.Calls; i++)
        {
            source.Append("let result").Append(i).Append(" = identity(").Append(i).Append(")\n");
        }

        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark environment must be prepared.");
        }

        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, source.ToString());
        if (this.compilation.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !this.compilation.Bind().IsComplete)
        {
            throw new InvalidOperationException("Benchmark source must bind successfully.");
        }
    }

    /// <summary>Measures a complete semantic reset, declaration pass, and final Bind/check.</summary>
    /// <returns>The pass summary.</returns>
    [Benchmark]
    public BindingResult FinalBind() => this.compilation.Binding.Bind(BindingMode.Final);

    /// <summary>Measures the provisional-to-final pipeline on the same tree.</summary>
    /// <returns>The final summary.</returns>
    [Benchmark]
    public BindingResult Pipeline() => this.compilation.Bind();
}
