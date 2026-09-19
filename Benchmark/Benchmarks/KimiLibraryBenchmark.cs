// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures embedded library initialization and warm binding without source IO or parsing in the warm path.</summary>
[Config(typeof(BenchmarkConfig))]
public class KimiLibraryBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Warms the library source cache and compilation-local binding storage.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.compilation = Compilation.CreateForTest();
        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, "Console.writeLine(\"x\")");
        for (var i = 0; i < 8; i++)
        {
            if (!this.compilation.Bind().IsComplete)
            {
                throw new InvalidOperationException("Library benchmark must bind successfully.");
            }
        }
    }

    /// <summary>Builds a fresh mutable syntax and identity graph from cached embedded source text.</summary>
    /// <returns>The compilation-local library.</returns>
    [Benchmark]
    public KimiLibrary CreateLibrary() => Compilation.CreateForTest().Library;

    /// <summary>Creates, parses and binds a fresh small program, including library initialization.</summary>
    /// <returns>The initial binding result.</returns>
    [Benchmark]
    public BindingResult CreateAndBind()
    {
        var fresh = Compilation.CreateForTest();
        fresh.Kotonoha.CreateCodeContext().Parse(fresh.Kotonoha.RootKoto, "Console.writeLine(\"x\")");
        return fresh.Bind();
    }

    /// <summary>Rebinds the same program and library, reusing all semantic storage.</summary>
    /// <returns>The final binding result.</returns>
    [Benchmark]
    public BindingResult WarmBind() => this.compilation.Bind();
}
