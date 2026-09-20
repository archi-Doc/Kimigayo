// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler.Documentation;
using Markdig;

namespace Benchmark;

[MemoryDiagnoser]
public class DocumentationMarkdownBenchmark
{
    private const string TypicalText = """
        Computes the **total** of the supplied values using `Sum`.

        ## Parameters

        - `values`: The input *values*.
        - `initial`: The starting total.

        > Empty input returns the initial value.

        See [the guide](https://example.com/guide) for details.

        ```kimi
        let total = Sum(values, 0)
        ```
        """;

    private MarkdownPipeline pipeline = null!;
    private string text = string.Empty;

    [Params("Short", "Typical", "Long")]
    public string Input { get; set; } = "Typical";

    [GlobalSetup]
    public void Setup()
    {
        // Match the documentation profile's HTML policy and source locations.
        this.pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UsePreciseSourceLocation()
            .Build();

        // Normalize outside the timed methods, including on CRLF checkouts.
        var typical = TypicalText.ReplaceLineEndings("\n");
        this.text = this.Input switch
        {
            "Short" => "Returns the number of available items.",
            "Typical" => typical,
            "Long" => string.Concat(Enumerable.Repeat(typical + "\n\n", 64)),
            _ => throw new InvalidOperationException("Unknown benchmark input."),
        };
    }

    [Benchmark(Baseline = true)]
    public object MarkdigParse() => Markdown.Parse(this.text, this.pipeline);

    [Benchmark]
    public object IndependentParse() => DocumentationMarkdownDocument.Parse(this.text);
}
