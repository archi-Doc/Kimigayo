// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Diagnostics;
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

        var diagnostics = Enumerable.Range(0, 200).Select(static i => new LspDiagnostic(default, 1, "Code", "kimigayo", (i / 2).ToString("D3"))).ToArray();
        var list = new List<LspDiagnostic>(diagnostics.Length);
        LspDiagnostic[]? payload = null;
        results.Add(new
        {
            name = "deduplicate200Diagnostics",
            cost = Measure(() =>
            {
                list.Clear();
                list.AddRange(diagnostics);
                payload = WorkspaceCheck.Deduplicate(list);
            }),
        });

        results.Add(new
        {
            name = "deduplicate200DiagnosticsBefore",
            cost = Measure(() =>
            {
                list.Clear();
                list.AddRange(diagnostics);
                payload = DeduplicateBefore(list);
            }),
        });

        var directory = Path.Combine(Path.GetTempPath(), "kimi-lsp-measurements");
        var files = Enumerable.Range(0, 1000).Select(i => Path.Combine(directory, i.ToString("D4") + ".kimi")).ToArray();
        var disk = new InputState { Names = files, DiskNames = files };
        var key = InputKey.Listing(directory, InputKey.SourcePattern);
        foreach (var newFile in new[] { false, true })
        {
            var open = InputKey.File(newFile ? Path.Combine(directory, "new.kimi") : files[0]);
            var entries = ImmutableDictionary<InputKey, (long, InputState)>.Empty.Add(open, (1, new InputState { Overlay = true, Content = SourceContent.FromText("text", false) }));
            var inputs = new CheckInputs(new(0, entries, []), static () => []);
            InputState? state = null;
            results.Add(new { name = newFile ? "listing1000WithNewOverlay" : "listing1000WithExistingOverlay", cost = Measure(() => state = inputs.ReadListing(key, disk), 5000) });
            GC.KeepAlive(state);
        }

        GC.KeepAlive(payload);
        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
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

    // The implementation at 91aa2a5b, retained as a paired timing/allocation reference.
    private static LspDiagnostic[] DeduplicateBefore(List<LspDiagnostic> sorted)
    {
        var count = 1;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (!sorted[i].Equals(sorted[count - 1]))
            {
                sorted[count++] = sorted[i];
            }
        }

        return sorted.GetRange(0, count).ToArray();
    }
}
