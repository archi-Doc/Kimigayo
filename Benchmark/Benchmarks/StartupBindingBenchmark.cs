// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures reusable startup selection together with ordinary Core call Binding.</summary>
[Config(typeof(BenchmarkConfig))]
public class StartupBindingBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of calls in the selected body.</summary>
    [Params(1, 32, 512)]
    public int Calls { get; set; }

    /// <summary>Gets or sets a value indicating whether startup uses an explicit main.</summary>
    [Params(false, true)]
    public bool ExplicitMain { get; set; }

    /// <summary>Creates source and warms Binding and selection storage.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder(this.ExplicitMain ? "public func main()\n" : string.Empty);
        for (var i = 0; i < this.Calls; i++)
        {
            source.Append(this.ExplicitMain ? "    " : string.Empty).Append("::Core.writeLine(\"Hello world\")\n");
        }

        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark environment must be prepared.");
        }

        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, source.ToString());
        if (!this.compilation.Bind().IsComplete || !this.compilation.Binding.CheckStartup(OutputKind.Application).IsComplete)
        {
            throw new InvalidOperationException("Benchmark source must bind and select startup successfully.");
        }
    }

    /// <summary>Rebinds and selects startup without reparsing or cloning nodes.</summary>
    /// <returns>The selection summary.</returns>
    [Benchmark]
    public StartupResult BindAndSelect()
    {
        this.compilation.Bind();
        return this.compilation.Binding.CheckStartup(OutputKind.Application);
    }

    /// <summary>Measures only output-specific selection over final Binding.</summary>
    /// <returns>The selection summary.</returns>
    [Benchmark]
    public StartupResult Select() => this.compilation.Binding.CheckStartup(OutputKind.Application);
}
