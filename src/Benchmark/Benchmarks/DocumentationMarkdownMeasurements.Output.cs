// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kimi.Compiler.Documentation;
using Markdig;

namespace Benchmark;

internal static partial class DocumentationMarkdownMeasurements
{
    private static void MeasureOutput(string output)
    {
        var pipeline = CreatePipeline();
        var options = new DocumentationHtmlOptions { DeclarationHeadingLevel = 0 };
        var inputs = Inputs().Where(x => x.Common).Select(x => x with { Text = x.Text.Replace("guide.md", "/guide.md").Replace("a(b)c", "/a(b)c") }).ToList();
        foreach (var count in new[] { 256, 1024, 4096, 16384 })
        {
            inputs.Add(new("Render-scale-" + count, string.Concat(Enumerable.Repeat("- **text** and [guide](/guide.md).\n", count)), false));
        }

        var rows = new List<Measurement>();
        foreach (var input in inputs)
        {
            var independent = DocumentationMarkdownDocument.Parse(input.Text);
            var markdig = Markdown.Parse(input.Text, pipeline);
            var expected = Markdown.ToHtml(markdig, pipeline).Replace("\r\n", "\n").Replace("<li>\n<p>", "<li><p>");
            var actual = independent.ToHtml(options).Replace("<li>\n<p>", "<li><p>");
            if (actual != expected)
            {
                throw new InvalidOperationException("Output mismatch: " + input.Name);
            }

            Pair("Render", () => sink = independent.ToHtml(options), () => sink = Markdown.ToHtml(markdig, pipeline));
            if (input.Common)
            {
                Pair("Parse+Render", () => sink = DocumentationMarkdownDocument.Parse(input.Text).ToHtml(options), () => sink = Markdown.ToHtml(input.Text, pipeline));
            }

            void Pair(string phase, Action left, Action right)
            {
                var a = Prepare(left);
                var b = Prepare(right);
                var ta = new double[7];
                var tb = new double[7];
                var ba = new double[7];
                var bb = new double[7];
                for (var i = 0; i < 7; i++)
                {
                    if ((i & 1) == 0)
                    {
                        Sample(left, a, out ta[i], out ba[i]);
                        Sample(right, b, out tb[i], out bb[i]);
                    }
                    else
                    {
                        Sample(right, b, out tb[i], out bb[i]);
                        Sample(left, a, out ta[i], out ba[i]);
                    }
                }

                rows.Add(new(phase, input.Name, "Independent", input.Text.Length, a, ta, ba));
                rows.Add(new(phase, input.Name, "Markdig", input.Text.Length, b, tb, bb));
                Console.WriteLine($"{phase} {input.Name}: {Median(ta):F1}/{Median(tb):F1} ns; {Median(ba):F0}/{Median(bb):F0} B");
            }
        }

        var report = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            ProductSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DocumentationMarkdown).Assembly.Location))),
            Measurements = rows,
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
