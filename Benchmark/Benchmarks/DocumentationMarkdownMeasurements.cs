// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SourceSpan = Kimi.Diagnostics.SourceSpan;

namespace Benchmark;

#pragma warning disable SA1009, SA1117, SA1201, SA1202, SA1204, SA1401, SA1413, SA1600, SA1649 // Measurement lambdas, fixture initializers and result records.

// Separate from the BDN entry point: alternating paired samples, scaling, cold
// child processes and retained-result probes need different measurement lifetimes.
internal static class DocumentationMarkdownMeasurements
{
    private static object? sink;

    private static long checksum;

    internal static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: --documentation-markdown <output.json> | paired <baseline-Kimi.dll> <output.json> | diagnostics <output.json>", nameof(args));
        }

        nint originalAffinity = 0;
        if (OperatingSystem.IsWindows())
        {
            using var current = Process.GetCurrentProcess();
            originalAffinity = current.ProcessorAffinity;
            var allowed = (long)current.ProcessorAffinity;
            current.ProcessorAffinity = (nint)(allowed & -allowed);
        }

        var inputs = Inputs().ToArray();
        if (args[0] == "diagnostics")
        {
            if (OperatingSystem.IsWindows())
            {
                using var current = Process.GetCurrentProcess();
                current.ProcessorAffinity = originalAffinity;
            }

            Diagnostics(Path.GetFullPath(args[1]));
            return;
        }

        if (args[0] == "paired")
        {
            CompareBaseline(inputs.Where(x => x.Common).ToArray(), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            return;
        }

        if (args[0] == "cold")
        {
            var input = inputs.Single(x => x.Name == args[2]);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            if (args[1] == "Independent")
            {
                sink = DocumentationMarkdownDocument.Parse(input.Text, maximumDepth: 8192);
            }
            else
            {
                sink = Markdown.Parse(input.Text, CreatePipeline());
            }

            Console.WriteLine(JsonSerializer.Serialize(new { Nanoseconds = Stopwatch.GetElapsedTime(start).TotalNanoseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - before }));
            GC.KeepAlive(sink);
            return;
        }

        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var pipeline = CreatePipeline();
        var rows = new List<Measurement>();
        foreach (var input in inputs)
        {
            Pair("Parse", input, () => sink = DocumentationMarkdownDocument.Parse(input.Text, maximumDepth: 8192), () => sink = Markdown.Parse(input.Text, pipeline));
            if (!input.Common)
            {
                continue;
            }

            Retained(input, "Independent", () => DocumentationMarkdownDocument.Parse(input.Text));
            Retained(input, "Markdig", () => Markdown.Parse(input.Text, pipeline));
        }

        foreach (var size in new[]
        {
            8,
            128,
            2048
        }

        )
        {
            var input = new Input("Items-" + size, Parameters(size), false);
            var parameters = Enumerable.Range(0, size).Select(i => new DocumentationMarkdownParameter("p" + i)).ToArray();
            Pair("Parse+Extract", input, () =>
            {
                var doc = DocumentationMarkdownDocument.Parse(input.Text);
                checksum = doc.GetItemCandidates().Length;
            }, () =>
            {
                var doc = Markdown.Parse(input.Text, pipeline);
                checksum = Extract(doc).Length;
            });
            var independent = DocumentationMarkdownDocument.Parse(input.Text);
            var markdig = Markdown.Parse(input.Text, pipeline);
            var independentItems = independent.GetItemCandidates().ToArray();
            var markdigItems = Extract(markdig);
            if (!independentItems.Select(x => (x.Name, x.DescriptionSpan.Start, x.DescriptionSpan.End)).SequenceEqual(markdigItems.Select(x => (x.Name, x.Start, x.End))))
            {
                throw new InvalidOperationException("Candidate mismatch: " + input.Name);
            }

            if (!independent.ClassifyItems(parameters).ToArray().Select(x => x.ParameterMatchCount).SequenceEqual(Classify(markdigItems, parameters).Select(x => x.Matches)))
            {
                throw new InvalidOperationException("Classification mismatch: " + input.Name);
            }

            Pair("Classify-cached-syntax", input, () => checksum = independent.ClassifyItems(parameters).Length, () => checksum = Classify(markdigItems, parameters).Length);
            Pair("Summary-cached-syntax", input, () => checksum = independent.Summary?.Span.Start ?? -1, () => checksum = markdig[0] is ParagraphBlock p ? p.Span.Start : -1);
            Pair("Candidates-cached", input, () => checksum = independent.GetItemCandidates().Length, () => checksum = markdigItems.Length);
            foreach (var requests in new[]
            {
                1,
                4,
                16
            }

            )
            {
                var fresh = DocumentationMarkdownDocument.Parse(input.Text);
                var before = GC.GetTotalAllocatedBytes(true);
                var start = Stopwatch.GetTimestamp();
                Parallel.For(0, requests, _ =>
                {
                    _ = fresh.GetItemCandidates().Length;
                });
                rows.Add(new("Concurrent-first-extract-" + requests, input.Name, "Independent", input.Text.Length, 1, [Stopwatch.GetElapsedTime(start).TotalNanoseconds], [GC.GetTotalAllocatedBytes(true) - before]));
            }
        }

        foreach (var size in new[]
        {
            64,
            1024,
            16384
        }

        )
        {
            var input = new Input("Source-lines-" + size, string.Concat(Enumerable.Repeat("Words 😀 and **emphasis**.\n", size)).TrimEnd('\n'), false);
            var comment = SourceComment(input.Text);
            var source = comment.GetText(); // Identical precomputed source mapping for both engines.
            var doc = DocumentationMarkdownDocument.Parse(comment);
            var queries = Enumerable.Range(0, 1024).Select(i => new SourceSpan((int)((long)i * (source.Text.Length - 2) / 1024), 2)).ToArray();
            if (queries.Any(query => doc.GetSourceSpan(query) != Map(source, query)))
            {
                throw new InvalidOperationException("Source mapping mismatch: " + input.Name);
            }

            Pair("Map-1024-ranges", input, () =>
            {
                long sum = 0;
                foreach (var query in queries)
                {
                    sum += doc.GetSourceSpan(query).End;
                }

                checksum = sum;
            }, () =>
            {
                long sum = 0;
                foreach (var query in queries)
                {
                    sum += Map(source, query).End;
                }

                checksum = sum;
            });
        }

        var cancel = new CancellationToken(true);
        var cancelInput = new Input("Cancelled", Parameters(128), false);
        rows.Add(Measure("Precancelled-parse", cancelInput, "Independent", () =>
        {
            try
            {
                sink = DocumentationMarkdownDocument.Parse(cancelInput.Text, cancel);
            }
            catch (OperationCanceledException)
            {
                checksum++;
            }
        }));
        var depthInput = new Input("Depth-limit", new string('>', 1024) + " text", false);
        rows.Add(Measure("Depth-interruption", depthInput, "Independent", () =>
        {
            try
            {
                sink = DocumentationMarkdownDocument.Parse(depthInput.Text);
            }
            catch (DocumentationMarkdownLimitException)
            {
                checksum++;
            }
        }));
        var cold = new List<object>();
        foreach (var input in inputs.Where(x => x.Common))
        {
            foreach (var engine in new[]
            {
                "Independent",
                "Markdig"
            }

            )
            {
                for (var launch = 0; launch < 3; launch++)
                {
                    var processInfo = new ProcessStartInfo("dotnet")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    foreach (var argument in new[]
                    {
                        typeof(DocumentationMarkdownMeasurements).Assembly.Location,
                        "--documentation-markdown",
                        "cold",
                        engine,
                        input.Name
                    }

                    )
                    {
                        processInfo.ArgumentList.Add(argument);
                    }

                    using var process = Process.Start(processInfo)!;
                    var result = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(error);
                    }

                    cold.Add(new { input.Name, Engine = engine, Launch = launch, Result = JsonSerializer.Deserialize<JsonElement>(result) });
                }
            }
        }

        var report = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            ProductSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DocumentationMarkdownDocument).Assembly.Location))),
            MarkdigVersion = typeof(Markdown).Assembly.GetName().Version?.ToString(),
            ProcessPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            Inputs = inputs.Select(x => new { x.Name, Length = x.Text.Length, x.Common, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.Text))) }),
            Measurements = rows,
            Cold = cold,
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        GC.KeepAlive(sink);
        GC.KeepAlive(checksum);
        void Pair(string phase, Input input, Action independent, Action markdig)
        {
            try
            {
                markdig();
            }
            catch (ArgumentException exception) when (!input.Common)
            {
                rows.Add(Measure(phase, input, "Independent", independent));
                rows.Add(new(phase, input.Name, "Markdig", input.Text.Length, 0, [], [], exception.Message));
                Console.WriteLine($"{phase} {input.Name}: Markdig rejected: {exception.Message}");
                return;
            }

            var a = Prepare(independent);
            var b = Prepare(markdig);
            var timesA = new double[7];
            var timesB = new double[7];
            var bytesA = new double[7];
            var bytesB = new double[7];
            for (var sample = 0; sample < 7; sample++)
            {
                if ((sample & 1) == 0)
                {
                    Sample(independent, a, out timesA[sample], out bytesA[sample]);
                    Sample(markdig, b, out timesB[sample], out bytesB[sample]);
                }
                else
                {
                    Sample(markdig, b, out timesB[sample], out bytesB[sample]);
                    Sample(independent, a, out timesA[sample], out bytesA[sample]);
                }
            }

            rows.Add(new(phase, input.Name, "Independent", input.Text.Length, a, timesA, bytesA));
            rows.Add(new(phase, input.Name, "Markdig", input.Text.Length, b, timesB, bytesB));
            Console.WriteLine($"{phase} {input.Name}: {Median(timesA):F1}/{Median(timesB):F1} ns, {Median(bytesA):F0}/{Median(bytesB):F0} B");
        }

        void Retained(Input input, string engine, Func<object> parse)
        {
            var count = Math.Clamp(200_000 / input.Text.Length, 8, 256);
            var roots = new object[count];
            var before = GC.GetTotalMemory(true);
            for (var i = 0; i < roots.Length; i++)
            {
                roots[i] = parse();
            }

            var retained = GC.GetTotalMemory(true) - before;
            rows.Add(new("Retained-after-GC", input.Name, engine, input.Text.Length, count, [], [(double)retained / count]));
            GC.KeepAlive(roots);
        }
    }

    private static void Diagnostics(string output)
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), io);
        var rows = new List<Measurement>();
        foreach (var count in new[] { 8, 128, 2048 })
        {
            var input = new Input("Items-" + count, Parameters(count), false);
            foreach (var requests in new[] { 1, 4, 16 })
            {
                foreach (var cached in new[] { false, true })
                {
                    var times = new double[7];
                    var bytes = new double[7];
                    // Two untimed batches warm scheduling, publication and allocation paths.
                    for (var sample = -2; sample < times.Length; sample++)
                    {
                        var doc = DocumentationMarkdownDocument.Parse(input.Text);
                        if (cached)
                        {
                            _ = doc.GetItemCandidates();
                        }

                        using var ready = new CountdownEvent(requests);
                        using var gate = new ManualResetEventSlim();
                        var tasks = Enumerable.Range(0, requests).Select(_ => Task.Run(() =>
                        {
                            ready.Signal();
                            gate.Wait();
                            if (doc.GetItemCandidates().Length != count)
                            {
                                throw new InvalidOperationException("Incomplete candidate publication");
                            }
                        })).ToArray();
                        if (!ready.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("Workers did not become ready");
                        }

                        var before = GC.GetTotalAllocatedBytes(true);
                        var start = Stopwatch.GetTimestamp();
                        gate.Set();
                        Task.WaitAll(tasks);
                        var ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                        var allocated = GC.GetTotalAllocatedBytes(true) - before;
                        if (sample >= 0)
                        {
                            times[sample] = ns;
                            bytes[sample] = allocated;
                        }
                    }

                    rows.Add(new((cached ? "Concurrent-cached-" : "Concurrent-first-") + requests, input.Name, "Independent", input.Text.Length, 1, times, bytes));
                }
            }
        }

        var text = string.Concat(Enumerable.Repeat("Words **strong** and `code`.\n", 100000));
        var cancelledTimes = new double[7];
        var cancelledBytes = new double[7];
        for (var i = 0; i < cancelledTimes.Length; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var before = GC.GetTotalAllocatedBytes(true);
            var start = Stopwatch.GetTimestamp();
            cancellation.CancelAfter(1);
            try
            {
                sink = DocumentationMarkdownDocument.Parse(text, cancellation.Token);
                throw new InvalidOperationException("Large parse completed before requested cancellation");
            }
            catch (OperationCanceledException)
            {
                cancelledTimes[i] = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                cancelledBytes[i] = GC.GetTotalAllocatedBytes(true) - before;
            }
        }

        var retry = new Input("Retry-large-input", text, false);
        rows.Add(new("Active-cancellation-1ms-request", retry.Name, "Independent", text.Length, 1, cancelledTimes, cancelledBytes));
        rows.Add(Measure("Retry-after-cancellation", retry, "Independent", () => sink = DocumentationMarkdownDocument.Parse(text)));
        var report = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessorCount = Environment.ProcessorCount,
            ProductSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DocumentationMarkdownDocument).Assembly.Location))),
            Measurements = rows,
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }

    private static void CompareBaseline(Input[] inputs, string baselinePath, string output)
    {
        var context = new BaselineLoadContext(Path.GetDirectoryName(baselinePath)!);
        try
        {
            var assembly = context.LoadFromAssemblyPath(baselinePath);
            var types = new[] { typeof(string), typeof(CancellationToken), typeof(int) };
            var beforeParse = assembly.GetType(typeof(DocumentationMarkdownDocument).FullName!)!.GetMethod("Parse", types)!.CreateDelegate<Func<string, CancellationToken, int, object>>();
            var afterParse = typeof(DocumentationMarkdownDocument).GetMethod("Parse", types)!.CreateDelegate<Func<string, CancellationToken, int, object>>();
            var rows = new List<Measurement>();
            foreach (var input in inputs)
            {
                Action before = () => sink = beforeParse(input.Text, CancellationToken.None, 8192);
                Action after = () => sink = afterParse(input.Text, CancellationToken.None, 8192);
                var count = Math.Max(Prepare(before), Prepare(after));
                var beforeTimes = new double[15];
                var afterTimes = new double[15];
                var beforeBytes = new double[15];
                var afterBytes = new double[15];
                for (var i = 0; i < beforeTimes.Length; i++)
                {
                    if ((i & 1) == 0)
                    {
                        Sample(before, count, out beforeTimes[i], out beforeBytes[i]);
                        Sample(after, count, out afterTimes[i], out afterBytes[i]);
                    }
                    else
                    {
                        Sample(after, count, out afterTimes[i], out afterBytes[i]);
                        Sample(before, count, out beforeTimes[i], out beforeBytes[i]);
                    }
                }

                rows.Add(new("Paired-parse", input.Name, "DM3", input.Text.Length, count, beforeTimes, beforeBytes));
                rows.Add(new("Paired-parse", input.Name, "Optimized", input.Text.Length, count, afterTimes, afterBytes));
                Console.WriteLine($"{input.Name}: {Median(beforeTimes):F1} -> {Median(afterTimes):F1} ns; {Median(beforeBytes):F0} -> {Median(afterBytes):F0} B");
            }

            var report = new
            {
                Timestamp = DateTimeOffset.UtcNow,
                BaselineSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baselinePath))),
                OptimizedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DocumentationMarkdownDocument).Assembly.Location))),
                Measurements = rows,
            };
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            sink = null;
            context.Unload();
        }
    }

    private static MarkdownPipeline CreatePipeline() => new MarkdownPipelineBuilder { MaximumNestingDepth = 1_000_000 }.DisableHtml().UsePreciseSourceLocation().Build();

    private static Measurement Measure(string phase, Input input, string engine, Action operation)
    {
        var count = Prepare(operation);
        var times = new double[7];
        var bytes = new double[7];
        for (var i = 0; i < times.Length; i++)
        {
            Sample(operation, count, out times[i], out bytes[i]);
        }

        return new(phase, input.Name, engine, input.Text.Length, count, times, bytes);
    }

    private static int Prepare(Action operation)
    {
        var start = Stopwatch.GetTimestamp();
        do
        {
            operation();
        }
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 50);
        var count = 1;
        while (true)
        {
            Sample(operation, count, out var ns, out _);
            if (ns * count >= 20_000_000 || count >= 1 << 24)
            {
                return count;
            }

            count *= 2;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Sample(Action operation, int count, out double nanoseconds, out double bytes)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++)
        {
            operation();
        }

        nanoseconds = Stopwatch.GetElapsedTime(start).TotalNanoseconds / count;
        bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / count;
    }

    private static double Median(double[] values) => values.Order().ElementAt(values.Length / 2);

    private static IEnumerable<Input> Inputs()
    {
        yield return new("Summary", "Returns the first matching value, or zero when no value matches.", true);
        yield return new("Rich-summary", "Returns **the first** matching `value`. See [the guide](guide.md \"Usage\").", true);
        yield return new("Parameters", Parameters(12), true);
        yield return new("Code-example", "Example:\n\n```kimi\n" + string.Concat(Enumerable.Repeat("let total = add(2, 3)\n", 256)) + "```", true);
        yield return new("Nested", "- value: input\n  - positive\n  - bounded\n\n  > Use **care**.\n  > Continue here.\n\n- return: result", true);
        yield return new("Links-entities", string.Concat(Enumerable.Repeat("[guide](a(b)c \"title\") and &amp; &#x1F600; `code`. ", 32)), true);
        foreach (var size in new[]
        {
            256,
            1024,
            4096,
            16384
        }

        )
        {
            yield return new("Prose-" + size, new string('x', size), false);
            yield return new("Blocks-" + size, string.Concat(Enumerable.Repeat("word\n\n", size)), false);
            yield return new("Failed-links-" + size, string.Concat(Enumerable.Repeat("[x](a(", size)), false);
            yield return new("Failed-titles-" + size, string.Concat(Enumerable.Repeat("[x](u \" ", size)), false);
            yield return new("Delimiters-" + size, string.Concat(Enumerable.Repeat("a **b** ", size)), false);
            yield return new("Unmatched-brackets-" + size, new string('[', size), false);
        }

        foreach (var size in new[]
        {
            32,
            128,
            512,
            2048
        }

        )
        {
            yield return new("Quote-depth-" + size, new string('>', size) + " text", false);
        }

        foreach (var size in new[]
        {
            32,
            64,
            128,
            256
        }

        )
        {
            yield return new("Backtick-runs-" + size, string.Concat(Enumerable.Range(1, size).Select(i => new string('`', i) + "x ")), false);
        }
    }

    private static string Parameters(int count) => "Returns a value.\n\n" + string.Concat(Enumerable.Range(0, count).Select(i => "- `p" + i + "`: Parameter description.\n"));

    // Benchmark-only adapter for this generator's root plain/code names. It does
    // not use the legacy product extractor, whose profile/classification differs.
    private static Item[] Extract(MarkdownDocument doc)
    {
        var result = new List<Item>();
        foreach (var block in doc)
        {
            if (block is not ListBlock list)
            {
                continue;
            }

            foreach (ListItemBlock item in list)
            {
                var paragraph = (ParagraphBlock)item[0];
                var name = (CodeInline)paragraph.Inline!.FirstChild!;
                var following = (LiteralInline)name.NextSibling!;
                if (!following.Content.ToString().StartsWith(": ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Benchmark grammar");
                }

                result.Add(new(name.Content, item, following.Span.Start + 1, item.Span.End + 1, 0));
            }
        }

        return result.ToArray();
    }

    private static Item[] Classify(Item[] items, DocumentationMarkdownParameter[] parameters)
    {
        var counts = new Dictionary<string, int>(parameters.Length, StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (!parameter.IsReceiver)
            {
                counts[parameter.Name] = counts.GetValueOrDefault(parameter.Name) + 1;
            }
        }

        var result = (Item[])items.Clone();
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = result[i] with
            {
                Matches = counts.GetValueOrDefault(result[i].Name)
            };
        }

        return result;
    }

    private static DocumentationComment SourceComment(string input)
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = true;
        compilation.Kotonoha.AddSource(new SourceDocument("benchmark.kimi", "/// " + input.Replace("\n", "\r\n/// ", StringComparison.Ordinal) + "\r\nfunc f() => ()"));
        return compilation.Kotonoha.DocumentationSources.Single().Comments.Single();
    }

    private static SourceSpan Map(DocumentationText source, SourceSpan span)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(span.Start);
        ArgumentOutOfRangeException.ThrowIfNegative(span.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, source.Text.Length);
        var start = source.GetSourceOffset(span.Start);
        if (span.Length == 0)
        {
            return new(start, 0);
        }

        var end = source.GetSourceOffset(span.End - 1);
        var original = source.Source.SourceText;
        end += source.Text[span.End - 1] == '\n' && original[end] == '\r' && end + 1 < original.Length && original[end + 1] == '\n' ? 2 : 1;
        return SourceSpan.FromBounds(start, end);
    }

    // Isolate dependency registries as well as Kimi itself. Sharing Tinyhand with
    // a second Kimi assembly would collide in its module-initializer type registry.
    private sealed class BaselineLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName name)
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? this.LoadFromAssemblyPath(path) : null;
        }
    }

    private sealed record Input(string Name, string Text, bool Common);

    private readonly record struct Item(string Name, MarkdownObject Node, int Start, int End, int Matches);

    private sealed record Measurement(string Phase, string Input, string Engine, int Characters, int OperationsPerSample, double[] Nanoseconds, double[] Bytes, string? Error = null);
}
