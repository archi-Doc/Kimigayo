// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.4.5 to 23.4.7: the quiet period, the base rule, reuse, pending units and publication, driven step by step
// with a fake clock and a controllable runner. Each line's "error" words become diagnostics.
public sealed class CheckSchedulerTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-schedule-" + Guid.NewGuid().ToString("N"));

    public CheckSchedulerTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public async Task EditsWithinTheQuietPeriodStartOneCheck()
    {
        await using var harness = new SchedulerHarness(quietPeriod: null);
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "error");
        harness.At(90).Change(path, 2, "error error");
        harness.At(180).Change(path, 3, "error");
        harness.At(1179);
        Assert.Equal(0, harness.Starts);
        harness.At(1180);
        Assert.Equal(1, harness.Starts);

        await harness.RunCheckAsync();
        var publish = Assert.Single(await harness.PublishesAsync(path));
        Assert.Equal(3, publish.GetProperty("version").GetInt32());
        Assert.Equal(1, publish.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task AnEditAfterTheBaseDiscardsTheResultAndSchedulesTheNextCheck()
    {
        await using var harness = new SchedulerHarness(quietPeriod: null);
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "error");
        harness.At(1000);
        await harness.RunCheckAsync(block: true, whileBlocked: () => harness.At(1210).Change(path, 2, "error error"));
        Assert.Empty(await harness.PublishesAsync(path));
        Assert.Equal(2210, harness.Session.Deadline);

        harness.At(2210);
        await harness.RunCheckAsync();
        var publish = Assert.Single(await harness.PublishesAsync(path));
        Assert.Equal(2, publish.GetProperty("version").GetInt32());
        Assert.Equal(2, publish.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task AnUnchangedIdentityReusesTheResult()
    {
        await using var harness = new SchedulerHarness();
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "error");
        harness.At(250);
        await harness.RunCheckAsync();
        Assert.Equal(1, harness.Runs);

        harness.At(300).Change(path, 2, "error!");
        harness.At(310).Change(path, 3, "error");
        harness.At(560);
        await harness.RunCheckAsync();
        Assert.Equal(1, harness.Runs);
        var publishes = await harness.PublishesAsync(path);
        Assert.Equal(2, publishes.Count);
        Assert.Equal(3, publishes[1].GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task PendingUnitsAreSkippedAndKeepTheirLastDiagnostics()
    {
        await using var harness = new SchedulerHarness();
        var first = this.PathOf("A.kimi");
        var second = this.PathOf("B.kimi");
        harness.At(0).Open(first, "error");
        harness.At(1).Open(second, "error");
        harness.At(251);
        await harness.RunCheckAsync();
        Assert.Equal(2, harness.Runs);

        harness.At(300).Change(first, 2, "error error");
        harness.At(301).Change(second, 2, "error error");
        harness.At(551);
        await harness.RunCheckAsync(block: true, whileBlocked: () => harness.At(600).Change(first, 3, "error error error"));
        Assert.Equal(3, harness.Runs); // The most recently edited project runs first; the other is pending and skipped.
        Assert.Single(await harness.PublishesAsync(first));
        Assert.Equal(2, (await harness.PublishesAsync(second))[1].GetProperty("diagnostics").GetArrayLength());

        harness.At(850);
        await harness.RunCheckAsync();
        Assert.Equal(4, harness.Runs);
        Assert.Equal(3, (await harness.PublishesAsync(first))[1].GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task ClosingAnImplicitSourceRetiresItsUnitAndClearsItsDiagnostics()
    {
        await using var harness = new SchedulerHarness();
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "error");
        harness.At(250);
        await harness.RunCheckAsync();
        harness.At(300).Close(path);
        harness.At(550);
        await harness.RunCheckAsync();
        var publishes = await harness.PublishesAsync(path);
        Assert.Equal(2, publishes.Count);
        Assert.Equal(0, publishes[1].GetProperty("diagnostics").GetArrayLength());
        Assert.False(publishes[1].TryGetProperty("version", out _));
        Assert.Empty(harness.Session.Units);
    }

    [Fact]
    public async Task EmptyResultsAreNotSentBeforeAnyDiagnostics()
    {
        await using var harness = new SchedulerHarness();
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "fine");
        harness.At(250);
        await harness.RunCheckAsync();
        Assert.Empty(await harness.PublishesAsync(path));
        Assert.Single(harness.Session.Units, static x => x.Key.Kind == UnitKind.Product);
    }

    [Fact]
    public async Task ShutdownRetiresThePendingCheck()
    {
        await using var harness = new SchedulerHarness();
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "error");
        harness.Message("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"shutdown\"}");
        Assert.Null(harness.Session.Deadline);
        harness.At(1000);
        Assert.Equal(0, harness.Starts);
        Assert.Contains(await harness.FramesAsync(), static x => x.TryGetProperty("id", out var id) && id.GetInt32() == 9 && x.GetProperty("result").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task ADiscardedProductCannotRetireItsTestUnit()
    {
        await using var harness = new SchedulerHarness();
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "test error");
        harness.At(250);
        await harness.RunCheckAsync();
        var test = Assert.Single(harness.Session.Units, static x => x.Key.Kind == UnitKind.Test).Value.Result;

        harness.At(300).Change(path, 2, "error");
        harness.At(550);
        await harness.RunCheckAsync(block: true, whileBlocked: () => harness.At(600).Change(path, 3, "test error"));
        Assert.Same(test, Assert.Single(harness.Session.Units, static x => x.Key.Kind == UnitKind.Test).Value.Result);
        Assert.Single(await harness.PublishesAsync(path));

        harness.At(850);
        await harness.RunCheckAsync();
        Assert.Single(harness.Session.Units, static x => x.Key.Kind == UnitKind.Test);
    }

    [Fact]
    public async Task SharedDiagnosticsRemainDistinctWhenContributorsChangeAndRetire()
    {
        await using var harness = new SchedulerHarness();
        var shared = this.PathOf("Shared.kimi");
        var identity = SourceIdentity.FromPath(shared);
        harness.Session.Runner = (_, _, plan, source, _) =>
        {
            var text = source.ReadSource(plan.Key.Owner.Value).Text!;
            return new(
                CheckOutcome.Completed,
                false,
                TestPresence.No,
                [Fake("common", 0, default), Fake(text, 0, default)],
                [new(identity.Value, true)]);
        };
        var first = this.PathOf("A.kimi");
        var second = this.PathOf("B.kimi");
        harness.At(0).Open(first, "A");
        harness.At(1).Open(second, "B");
        harness.At(251);
        await harness.RunCheckAsync();
        var published = await harness.PublishesAsync(shared);
        // SPEC 23.4.7: range, then contributor (A before B), then result order; a value both send appears once.
        Assert.Equal(["common", "A", "B"], published[^1].GetProperty("diagnostics").EnumerateArray().Select(static x => x.GetProperty("message").GetString()));

        harness.At(300).Change(first, 2, "C");
        harness.At(550);
        await harness.RunCheckAsync();
        published = await harness.PublishesAsync(shared);
        Assert.Equal(["common", "C", "B"], published[^1].GetProperty("diagnostics").EnumerateArray().Select(static x => x.GetProperty("message").GetString()));

        harness.At(600).Close(second);
        harness.At(850);
        await harness.RunCheckAsync();
        published = await harness.PublishesAsync(shared);
        Assert.Equal(["common", "C"], published[^1].GetProperty("diagnostics").EnumerateArray().Select(static x => x.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task DesynchronizationSurvivesUnrelatedChecksAndDiskChanges()
    {
        await using var harness = new SchedulerHarness();
        harness.Session.Runner = static (_, _, plan, source, token) => WorkspaceCheck.RunCheck(Kimigayo.CreateSilent(), plan, false, source, token);
        var path = this.PathOf("A.kimi");
        File.WriteAllText(path, "let x = 1\n");
        harness.At(0).Open(path, "let x = 1\n");
        harness.Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\",\"version\":2}},\"contentChanges\":[{LspTestClient.Range(10, 0, 10, 0, "x")}]}}}}");
        harness.At(250);
        await harness.RunCheckAsync();
        Assert.False(harness.Session.Store.Find(InputKey.File(path))!.State!.Established);

        File.WriteAllText(path, "let x = 1234\n");
        harness.At(300).Open(this.PathOf("B.kimi"), "let y = 2\n");
        harness.At(550);
        await harness.RunCheckAsync();
        var state = harness.Session.Store.Find(InputKey.File(path))!.State!;
        Assert.False(state.Established);
        Assert.True(state.Overlay);
        Assert.Single(await harness.PublishesAsync(path));

        harness.At(600).Change(path, 3, "let x = 1\n");
        harness.At(850);
        await harness.RunCheckAsync();
        Assert.True(harness.Session.Store.Find(InputKey.File(path))!.State!.Established);
        Assert.Equal(0, (await harness.PublishesAsync(path))[1].GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task AnUnreadableChangeDesynchronizesTheOpenDocument()
    {
        // The client applied every change, so dropping the notification would leave the server's text silently stale.
        await using var harness = new SchedulerHarness();
        harness.Session.Runner = static (_, _, plan, source, token) => WorkspaceCheck.RunCheck(Kimigayo.CreateSilent(), plan, false, source, token);
        var path = this.PathOf("A.kimi");
        harness.At(0).Open(path, "let x = 1\n");
        harness.Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\",\"version\":2}},\"contentChanges\":[{LspTestClient.Range(0, 0, 0, 0, "x")},{{\"text\":null}}]}}}}");
        harness.At(250);
        await harness.RunCheckAsync();
        Assert.False(harness.Session.Store.Find(InputKey.File(path))!.State!.Established);
        var frames = await harness.FramesAsync();
        Assert.Contains(frames, static x => LspTestClient.IsLog(x, "Invalid params"));
        Assert.Contains(frames, static x => LspTestClient.IsLog(x, "out of sync"));

        harness.At(300).Change(path, 3, "let x = 1\n");
        harness.At(550);
        await harness.RunCheckAsync();
        Assert.True(harness.Session.Store.Find(InputKey.File(path))!.State!.Established);
    }

    [Fact]
    public async Task AnotherSpellingOfTheUriFindsTheOpenDocument()
    {
        await using var harness = new SchedulerHarness();
        var path = this.PathOf("A.kimi");
        var spelling = LspTestClient.Uri(path).Replace("/A.kimi", "/%41.kimi", StringComparison.Ordinal);
        harness.At(0).Open(path, "fine");
        harness.Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{{\"textDocument\":{{\"uri\":\"{spelling}\",\"version\":2}},\"contentChanges\":[{LspTestClient.Full("error")}]}}}}");
        harness.At(250);
        await harness.RunCheckAsync();
        Assert.Equal(1, Assert.Single(await harness.PublishesAsync(path)).GetProperty("diagnostics").GetArrayLength());

        harness.At(300).Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didClose\",\"params\":{{\"textDocument\":{{\"uri\":\"{spelling}\"}}}}}}");
        harness.At(550);
        await harness.RunCheckAsync();
        Assert.Empty(harness.Session.Units);

        // The close released the first spelling too, so a reopened document receives its changes.
        harness.At(600).Open(path, "fine");
        harness.Change(path, 2, "error error");
        harness.At(850);
        await harness.RunCheckAsync();
        Assert.Equal(2, (await harness.PublishesAsync(path))[^1].GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task AnAlreadyLoadedCandidateStillExpandsItsProductDependencies()
    {
        var app = this.WriteProject("App", "Dependencies={Lib={PackageId=\"lib\" PackageVersion=\"1\" Project=\"../Lib/Lib.kimiproj\"}}");
        this.WriteProject("Lib", "Dependencies={Leaf={PackageId=\"leaf\" PackageVersion=\"1\" Project=\"../Leaf/Leaf.kimiproj\"}}");
        var leaf = this.WriteProject("Leaf", "TestSources={\"../Lib/Nested/Test.kimi\"}");
        var source = this.PathOf("Lib/Nested/Test.kimi");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await using var harness = new SchedulerHarness();
        harness.Session.Runner = static (_, _, _, _, _) => new(CheckOutcome.Completed, true, TestPresence.No, DiagnosticResult.Empty);
        harness.At(0).Open(app, File.ReadAllText(app));
        harness.At(1).Open(source, "test");
        harness.At(251);
        await harness.RunCheckAsync();
        Assert.Contains(harness.Session.Units.Keys, key => key.Owner == SourceIdentity.FromPath(leaf) && key.Kind == UnitKind.Product);
        Assert.DoesNotContain(harness.Session.Units.Keys, key => key.Owner == SourceIdentity.FromPath(source));
    }

    [Fact]
    public async Task APendingSourceListingDoesNotBecomeAProjectLoadFailure()
    {
        var project = this.WriteProject("App");
        await using var harness = new SchedulerHarness();
        harness.Session.Runner = static (_, _, _, _, _) => new(CheckOutcome.Completed, true, TestPresence.No, DiagnosticResult.Empty);
        harness.At(0).Open(project, File.ReadAllText(project));
        harness.At(250);
        await harness.RunCheckAsync(beforeCommit: () => harness.At(260).Open(this.PathOf("App/New.kimi"), "new"));
        Assert.Empty(harness.Session.Units);
        Assert.Empty(await harness.PublishesAsync(project));

        harness.At(510);
        await harness.RunCheckAsync();
        Assert.Single(harness.Session.Units, static x => x.Key.Kind == UnitKind.Product);
    }

    [Fact]
    public void UnreadableInputsAreNotReportedAsAbsent()
    {
        Assert.False(DiskReader.ReadFile(this.directory).Established);
        var path = this.PathOf("not-a-directory");
        File.WriteAllText(path, "file");
        Assert.False(DiskReader.ReadListing(path, InputKey.SourcePattern).Established);
        Assert.True(DiskReader.ReadFile(this.PathOf("missing.kimi")).Absent);
        Assert.Empty(DiskReader.ReadListing(this.PathOf("missing"), InputKey.SourcePattern).Names!);
    }

    private string WriteProject(string name, string settings = "")
    {
        var directory = this.PathOf(name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Library\" Targets={{\"{WindowsProfile.Target}\"}} {settings}");
        return path;
    }

    private string PathOf(string name) => Path.Combine(this.directory, name);

    /// <summary>Drives one session on the test thread; the worker runs on the thread pool and its products are pumped in order.</summary>
    private sealed class SchedulerHarness : IAsyncDisposable
    {
        private readonly Channel<object> posted = Channel.CreateUnbounded<object>();
        private readonly MemoryStream output = new();
        private readonly SemaphoreSlim gate = new(0);
        private WorkspaceCheck? started;
        private int blockNext;
        private int runs;

        // A null quiet period leaves the server default in effect.
        public SchedulerHarness(int? quietPeriod = 250)
        {
            this.Sender = new(this.output);
            this.Session = new(this.Sender, this.Start, item => this.posted.Writer.TryWrite(item)) { Runner = this.Run };
            var period = quietPeriod is { } value ? $"\"checkQuietPeriodMs\":{value}," : string.Empty;
            this.Message($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"capabilities\":{{}},\"initializationOptions\":{{{period}\"target\":\"{WindowsProfile.Target}\"}}}}}}");
            this.Message("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}");
        }

        public LspSender Sender { get; }

        public LspSession Session { get; }

        public long Now { get; private set; }

        public int Starts { get; private set; }

        public int Runs => Volatile.Read(ref this.runs);

        public SchedulerHarness At(long now)
        {
            this.Now = now;
            this.Session.Tick(now);
            return this;
        }

        public void Message(string json)
            => this.Session.Process(LspMessageReader.Parse(Encoding.UTF8.GetBytes(json)), this.Now);

        public void Open(string path, string text)
            => this.Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\",\"languageId\":\"kimi\",\"version\":1,\"text\":{JsonSerializer.Serialize(text)}}}}}}}");

        public void Change(string path, int version, string text)
            => this.Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\",\"version\":{version}}},\"contentChanges\":[{LspTestClient.Full(text)}]}}}}");

        public void Close(string path)
            => this.Message($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didClose\",\"params\":{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\"}}}}}}");

        /// <summary>Runs the started check to completion, processing its products as the state owner would.</summary>
        /// <param name="block">Whether the first unit waits until <paramref name="whileBlocked"/> ran.</param>
        /// <param name="whileBlocked">State-owner steps taken while the first unit runs.</param>
        /// <param name="beforeCommit">State-owner steps taken before re-validation is committed.</param>
        /// <returns>A task that completes after <see cref="CheckDone"/>.</returns>
        public async Task RunCheckAsync(bool block = false, Action? whileBlocked = null, Action? beforeCommit = null)
        {
            var check = this.started ?? throw new InvalidOperationException("No check was started.");
            this.started = null;
            this.blockNext = block ? 1 : 0;
            var worker = Task.Run(check.Run, TestContext.Current.CancellationToken);
            while (true)
            {
                var item = await this.posted.Reader.ReadAsync(TestContext.Current.CancellationToken);
                if (item is RunnerEntered)
                {
                    whileBlocked?.Invoke();
                    this.gate.Release();
                    continue;
                }

                if (item is CommitRequest)
                {
                    beforeCommit?.Invoke();
                }

                this.Session.Process(item, this.Now);
                if (item is CheckDone done)
                {
                    Assert.Null(done.Failure);
                    break;
                }
            }

            await worker;
        }

        public async Task<List<JsonElement>> FramesAsync()
        {
            await this.Sender.FlushAsync();
            using var stream = new MemoryStream(this.output.ToArray());
            var reader = new LspFrameReader(stream);
            var frames = new List<JsonElement>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken) is { } frame)
            {
                using var document = JsonDocument.Parse(frame.Buffer.AsMemory(0, frame.Length));
                frames.Add(document.RootElement.Clone());
                ArrayPool<byte>.Shared.Return(frame.Buffer);
            }

            return frames;
        }

        public async Task<List<JsonElement>> PublishesAsync(string path)
        {
            var uri = LspTestClient.Uri(path);
            return (await this.FramesAsync()).Where(x => LspTestClient.IsPublish(x, uri)).Select(static x => x.GetProperty("params")).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            this.Session.Dispose();
            await this.Sender.DrainAsync();
            this.gate.Dispose();
        }

        private void Start(WorkspaceCheck check)
        {
            this.started = check;
            this.Starts++;
        }

        private CheckOutput Run(StoreSnapshot snapshot, CheckInputs inputs, UnitPlan plan, CheckInputSource source, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.runs);
            var path = plan.Members[0].Value;
            var content = source.ReadSource(path);
            var text = content.Text ?? Encoding.UTF8.GetString(content.GetBytes());
            if (Interlocked.Exchange(ref this.blockNext, 0) == 1)
            {
                this.posted.Writer.TryWrite(RunnerEntered.Instance);
                this.gate.Wait(cancellationToken);
            }

            var diagnostics = new List<CheckDiagnostic>();
            var lines = text.Split('\n');
            for (var line = 0; line < lines.Length; line++)
            {
                for (var index = lines[line].IndexOf("error", StringComparison.Ordinal); index >= 0; index = lines[line].IndexOf("error", index + 5, StringComparison.Ordinal))
                {
                    diagnostics.Add(Fake("error", 0, new SourceRange(new(line, index), new(line, index + 5))));
                }
            }

            return new(CheckOutcome.Completed, diagnostics.Count == 0, text.Contains("test", StringComparison.Ordinal) ? TestPresence.Yes : TestPresence.No, diagnostics.ToArray(), [new(SourceIdentity.FromPath(path).Value, true)]);
        }
    }

    // A published record at one source of the result's table, with the given display range.
    private static CheckDiagnostic Fake(string message, int source, SourceRange range)
        => new("Fake_Kd", DiagnosticSeverity.Error, DiagnosticCategory.Language, message, source, new SourceSpan(0, 1)) { Display = new(range, []) };

    private sealed class RunnerEntered
    {
        public static readonly RunnerEntered Instance = new();
    }
}
