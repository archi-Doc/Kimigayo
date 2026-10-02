// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kimi.Checking;
using Kimi.Lsp;

namespace Benchmark;

/// <summary>Measures hot document edits, diagnostic merging and retained directory listings without disk I/O.</summary>
internal static class LspMeasurements
{
    public static void Run()
    {
        var results = new List<object>();
        using var document = new TextDocument(string.Concat(Enumerable.Repeat("let value = 123456789\n", 5000)));
        var alternate = false;
        results.Add(new
        {
            name = "equalLengthEdit105KB",
            cost = Measure(() =>
            {
                alternate = !alternate;
                document.TryApply(new(0, 12), new(0, 13), alternate ? "2" : "1");
            }),
        });
        results.Add(new { name = "noOpEdit105KB", cost = Measure(() => document.TryApply(new(0, 0), new(0, 3), "let")) });
        results.Add(new
        {
            name = "insertOrDeleteNearStart105KB",
            cost = Measure(() =>
            {
                alternate = !alternate;
                document.TryApply(new(7, 4), new(7, alternate ? 4 : 5), alternate ? "x" : string.Empty);
            }),
        });

        var diagnostics = Enumerable.Range(0, 200).Select(static i => new LspDiagnostic(default, 1, "Code", "kimigayo", (i / 2).ToString("D3"))).ToArray();
        var list = new List<LspDiagnostic>(diagnostics);
        var contributions = new List<LspDiagnostic[]> { diagnostics[..100], diagnostics[50..150] };
        LspDiagnostic[]? payload = null;
        results.Add(new { name = "order200Diagnostics", cost = Measure(() => payload = WorkspaceCheck.Order(list)) });
        results.Add(new { name = "merge2x100Diagnostics", cost = Measure(() => payload = WorkspaceCheck.Merge(contributions)) });

        var directory = Path.Combine(Path.GetTempPath(), "kimi-lsp-measurements");
        var files = Enumerable.Range(0, 1000).Select(i => Path.Combine(directory, i.ToString("D4") + ".kimi")).ToArray();
        var disk = new InputState { Names = files, DiskNames = files };
        var key = InputKey.Listing(directory, InputKey.SourcePattern);
        foreach (var newFile in new[] { false, true })
        {
            var open = InputKey.File(newFile ? Path.Combine(directory, "new.kimi") : files[0]);
            var entries = new Dictionary<InputKey, (long, InputState)> { [open] = (1, new InputState { Overlay = true, Content = SourceContent.FromText("text", false) }) };
            var inputs = new CheckInputs(new(0, entries, []), static () => []);
            InputState? state = null;
            results.Add(new { name = newFile ? "listing1000WithNewOverlay" : "listing1000WithExistingOverlay", cost = Measure(() => state = inputs.ReadListing(key, disk), 5000) });
            GC.KeepAlive(state);
        }

        GC.KeepAlive(payload);
        MeasureMessages(results, directory);
        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // The receive-to-state-owner path of one keystroke and of a large reopen, and the sender's cost per frame.
    private static void MeasureMessages(List<object> results, string directory)
    {
        var sender = new LspSender(Stream.Null);
        using var session = new LspSession(sender, static _ => { }, static _ => { });
        Process(session, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"capabilities\":{}}}");
        var uri = new Uri(Path.Combine(directory, "Edit.kimi")).AbsoluteUri;
        var text = string.Concat(Enumerable.Repeat("let value = 123456789\n", 5000));
        var open = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{{\"textDocument\":{{\"uri\":\"{uri}\",\"languageId\":\"kimi\",\"version\":1,\"text\":{JsonSerializer.Serialize(text)}}}}}}}");
        Process(session, open);

        // Each keystroke inserts and the next deletes one character, with increasing versions.
        var changes = new byte[2000 + (5 * 20000)][];
        for (var i = 0; i < changes.Length; i++)
        {
            var range = i % 2 == 0 ? "\"start\":{\"line\":7,\"character\":4},\"end\":{\"line\":7,\"character\":4}},\"text\":\"x\"" : "\"start\":{\"line\":7,\"character\":4},\"end\":{\"line\":7,\"character\":5}},\"text\":\"\"";
            changes[i] = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{{\"textDocument\":{{\"uri\":\"{uri}\",\"version\":{i + 2}}},\"contentChanges\":[{{\"range\":{{{range}}}]}}}}");
        }

        var next = 0;
        results.Add(new { name = "didChangeKeystroke", cost = Measure(() => Process(session, changes[next++])) });
        results.Add(new { name = "didOpenRepeated105KB", cost = Measure(() => Process(session, open), 200) });

        var diagnostics = Enumerable.Range(0, 3).Select(static i => new LspDiagnostic(new(new(i, 0), new(i, 5)), 1, "Fake_Kd", "kimigayo", "An error message.")).ToArray();
        results.Add(new { name = "publishFrame3Diagnostics", cost = MeasureSender(sender, () => Publish(sender, uri, diagnostics)) });
        sender.DrainAsync().GetAwaiter().GetResult();
    }

    private static void Process(LspSession session, string json)
        => Process(session, Encoding.UTF8.GetBytes(json));

    private static void Process(LspSession session, byte[] body)
        => session.Process(LspMessageReader.Parse(body), 0);

    private static void Publish(LspSender sender, string uri, LspDiagnostic[] diagnostics)
        => sender.Notify(LspMethods.PublishDiagnostics, new PublishDiagnosticsParams { Uri = uri, Version = 1, Diagnostics = diagnostics }, LspJsonContext.Default.PublishDiagnosticsParams);

    // Counts the allocations of every thread, so the sender's pump is included; each sample waits for the output.
    private static object MeasureSender(LspSender sender, Action action, int iterations = 20000)
    {
        for (var i = 0; i < 2000; i++)
        {
            action();
        }

        sender.FlushAsync().GetAwaiter().GetResult();
        var nanoseconds = new double[5];
        var allocations = new double[5];
        for (var sample = 0; sample < nanoseconds.Length; sample++)
        {
            var bytes = GC.GetTotalAllocatedBytes(true);
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            sender.FlushAsync().GetAwaiter().GetResult();
            nanoseconds[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[sample] = (double)(GC.GetTotalAllocatedBytes(true) - bytes) / iterations;
        }

        Array.Sort(nanoseconds);
        Array.Sort(allocations);
        return new { nanoseconds = nanoseconds[2], bytes = allocations[2] };
    }

    private static object Measure(Action action, int iterations = 20000)
    {
        for (var i = 0; i < 2000; i++)
        {
            action();
        }

        var nanoseconds = new double[5];
        var allocations = new double[5];
        for (var sample = 0; sample < nanoseconds.Length; sample++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            nanoseconds[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
        }

        Array.Sort(nanoseconds);
        Array.Sort(allocations);
        return new { nanoseconds = nanoseconds[2], bytes = allocations[2] };
    }
}
