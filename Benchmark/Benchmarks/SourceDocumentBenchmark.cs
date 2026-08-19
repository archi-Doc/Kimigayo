// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class SourceDocumentBenchmark
{
    private string sourceText = string.Empty;

    [Params(100, 10_000)]
    public int LineCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.sourceText = string.Concat(Enumerable.Repeat("let value = 12345\r\nlet st = \"text\"\n", this.LineCount));
    }

    [Benchmark]
    public SourceDocument Create()
        => new("benchmark.kimi", this.sourceText);
}
