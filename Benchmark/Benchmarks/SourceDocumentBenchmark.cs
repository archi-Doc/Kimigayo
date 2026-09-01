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
        this.sourceText = string.Concat(Enumerable.Repeat("let value = 12345\r\nlet st = \"public DiagnosticCollection DiagnosticCollection => this.diagnosticCollection ?? this.Kotonoha.DiagnosticCollection;\"\n", this.LineCount));
    }

    [Benchmark]
    public SourceDocument Create()
        => new("benchmark.kimi", this.sourceText);

    [Benchmark]
    public int CreateAndMapLines()
    {
        // SourceDocument builds its line table on first use, so touch it explicitly.
        var sourceDocument = new SourceDocument("benchmark.kimi", this.sourceText);
        return sourceDocument.LineCount;
    }
}
