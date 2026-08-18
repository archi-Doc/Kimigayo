// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler.Target;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class TargetTripleBenchmark
{
    private string targetTriple = "x86_64-pc-windows-msvc";

    public TargetTripleBenchmark()
    {
    }

    [GlobalSetup]
    public void Setup()
    {
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    [Benchmark]
    public TargetTriple ParseTargetTriple()
    {
        return TargetTriple.Parse(this.targetTriple);
    }

    [Benchmark]
    public IrTarget ParseTargetTripleAndIrTarget()
    {
        var t = TargetTriple.Parse(this.targetTriple);
        return IrTarget.Create(t);
    }
}
