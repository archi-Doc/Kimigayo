// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text.Json;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class LspHoverAdoptionTest
{
    [Fact]
    public async Task AllRequiredSettingsMustFinishEvenWithoutAnyDiagnosticContributor()
    {
        await using var h = new Harness("markdown");
        h.Derive(2);
        h.Complete(0);
        Assert.Equal(JsonValueKind.Null, (await h.Hover()).ValueKind);
        h.Complete(1);
        h.Decide();
        h.Finish();
        var result = await h.Hover();
        Assert.Equal("markdown", result.GetProperty("contents").GetProperty("kind").GetString());
        Assert.Contains("```kimi\nstruct Sample\n```", Body(result));
        Assert.Equal(7, result.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(13, result.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await h.Hover(0, 13)).ValueKind);
    }

    [Fact]
    public async Task ChangedInformationReplacesHistoryEvenWhenDiagnosticsStayEmpty()
    {
        await using var h = new Harness();
        h.Adopt();
        var first = Body(await h.Hover());
        h.Edit(0, 0, 0, 0, "\n");
        var previous = await h.Hover(1, 8);
        Assert.Equal(HoverRenderer.PreviousNotice + "\n\n" + first, Body(previous));
        h.Begin("\nstruct Sample\n");
        h.Complete(0, span: new(8, 6), copy: ConstraintProof.Refuted);
        h.Decide();
        h.Finish();
        var current = Body(await h.Hover(1, 8));
        Assert.DoesNotContain(HoverRenderer.PreviousNotice, current);
        Assert.Contains("Copy: No", current);
        Assert.Equal(JsonValueKind.Null, (await h.Hover(0, 8)).ValueKind);
    }

    [Fact]
    public async Task SameInputRevalidationRestoresCurrentResultsWithoutAnotherUnitResult()
    {
        await using var h = new Harness();
        h.Adopt();
        h.Edit(0, 8, 0, 9, "a");
        Assert.Equal(JsonValueKind.Null, (await h.Hover()).ValueKind); // Identical text still touched this historical token.
        h.Begin("struct Sample\n");
        Assert.DoesNotContain(HoverRenderer.PreviousNotice, Body(await h.Hover()));
        h.Finish();
        Assert.Contains("Copy: Yes", Body(await h.Hover()));
    }

    [Theory]
    [InlineData(CheckOutcome.Blocked)]
    [InlineData(CheckOutcome.Faulted)]
    [InlineData(CheckOutcome.Completed)]
    public async Task DefinitiveMissingInformationDiscardsHistory(CheckOutcome outcome)
    {
        await using var h = new Harness();
        h.Adopt();
        h.Edit(0, 0, 0, 0, "\n");
        h.Begin("\nstruct Sample\n");
        h.Complete(0, outcome: outcome, missing: true);
        h.Decide();
        h.Finish();
        Assert.Equal(JsonValueKind.Null, (await h.Hover(1, 8)).ValueKind);
        h.Edit(0, 0, 0, 0, "\n");
        Assert.Equal(JsonValueKind.Null, (await h.Hover(2, 8)).ValueKind);
    }

    [Fact]
    public async Task InputArrivingDuringACheckKeepsHistoryUntilTheLatestCheckFinishes()
    {
        await using var h = new Harness();
        h.Adopt();
        h.Edit(0, 0, 0, 0, "\n");
        h.Begin("\nstruct Sample\n");
        h.Edit(0, 0, 0, 0, "\n");
        h.Finish();
        Assert.StartsWith(HoverRenderer.PreviousNotice, Body(await h.Hover(2, 8)));
        h.Begin("\n\nstruct Sample\n");
        h.Complete(0, span: new(9, 6));
        h.Decide();
        h.Finish();
        Assert.DoesNotContain(HoverRenderer.PreviousNotice, Body(await h.Hover(2, 8)));
    }

    [Fact]
    public async Task CurrentDisagreementNeverFallsBackToAnAgreeingPreviousSet()
    {
        await using var h = new Harness();
        h.Adopt(2);
        h.Edit(0, 0, 0, 0, "\n");
        h.Begin("\nstruct Sample\n");
        h.Complete(0, span: new(8, 6));
        h.Complete(1, span: new(8, 6), copy: ConstraintProof.Unknown);
        h.Decide();
        h.Finish();
        Assert.Equal(JsonValueKind.Null, (await h.Hover(1, 8)).ValueKind);
    }

    [Fact]
    public async Task TestOnlySourcesIgnoreProductUnitsThatDoNotSelectThem()
    {
        await using var h = new Harness();
        h.Derive(1, testOnly: true);
        h.Complete(1);
        Assert.Contains("Copy: Yes", Body(await h.Hover()));
    }

    [Fact]
    public async Task FullReplacementAndSynchronizationLossDiscardPreviousInformation()
    {
        await using var h = new Harness();
        h.Adopt();
        h.Replace("struct Sample\n");
        Assert.Equal(JsonValueKind.Null, (await h.Hover()).ValueKind);
        h.Begin("struct Sample\n");
        h.Finish();
        Assert.NotEqual(JsonValueKind.Null, (await h.Hover()).ValueKind);
        h.Edit(99, 0, 99, 0, "x");
        Assert.Equal(JsonValueKind.Null, (await h.Hover()).ValueKind);
    }

    private static string Body(JsonElement result) => result.GetProperty("contents").GetProperty("value").GetString()!;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly MemoryStream output = new();
        private readonly LspSender sender;
        private readonly LspSession session;
        private readonly SourceIdentity source = SourceIdentity.FromPath("temp/hover-session/main.kimi");
        private readonly SourceIdentity owner = SourceIdentity.FromPath("temp/hover-session/App.kimiproj");
        private readonly List<UnitKey> keys = [];
        private string text = "struct Sample\n";
        private int version = 1;
        private int id = 10;

        public async ValueTask DisposeAsync()
        {
            this.session.Dispose();
            await this.sender.DrainAsync();
            this.output.Dispose();
        }

        internal Harness(string format = "plaintext")
        {
            this.sender = new(this.output);
            this.session = new(this.sender, static _ => { }, static _ => { });
            using var options = JsonDocument.Parse("{\"checkQuietPeriodMs\":0}");
            this.Message(LspMethods.Initialize, new InitializeParams
            {
                InitializationOptions = options.RootElement.Clone(),
                Capabilities = new() { TextDocument = new() { Hover = new() { ContentFormat = [format] } } },
            });
            this.Message(LspMethods.DidOpen, new DidOpenTextDocumentParams { TextDocument = new() { Uri = this.source.ToUri(), Version = 1, Text = this.text } });
            this.Begin(this.text);
        }

        internal void Begin(string text)
        {
            this.text = text;
            this.session.Tick(0);
            this.session.Process(new CommitRequest([(InputKey.File(this.source), new() { Content = SourceContent.FromText(text, false), Overlay = true })], new()), 0);
        }

        internal void Derive(int count, bool testOnly = false)
        {
            this.keys.Clear();
            for (var i = 0; i < count; i++)
            {
                this.keys.Add(new(this.owner, UnitKind.Product, "target" + i));
            }

            if (testOnly)
            {
                this.keys.Add(new(this.owner, UnitKind.Test, "target0"));
            }

            var project = new LoadedProject { Path = this.owner, Members = [this.source], ProductMembers = testOnly ? [] : [this.source], HasTestSources = testOnly };
            this.session.Process(new DerivationDone { NewProjects = [project], Reached = [this.owner], Record = new(), Required = new(this.keys), KeepOwners = [], TestSourceOwners = testOnly ? [this.owner] : [] }, 0);
        }

        internal void Complete(int index, SourceSpan? span = null, ConstraintProof copy = ConstraintProof.Proven, CheckOutcome outcome = CheckOutcome.Completed, bool missing = false)
        {
            var data = new HoverDocument(new(this.source.Value, this.text), [new(span ?? new(7, 6), new([new("Type", "App", "struct Sample", [], [])], Copy: copy))]);
            var result = new UnitResult
            {
                Key = this.keys[index],
                Output = new(outcome, true, TestPresence.No, DiagnosticResult.Empty) { Hover = missing ? null : new([]) { Documents = new Dictionary<SourceIdentity, HoverDocument> { [this.source] = data } } },
                Inputs = [new(InputKey.File(this.source), this.session.Store.Find(InputKey.File(this.source))!.Revision, null)],
                Reports = [],
            };
            this.session.Process(new UnitDone(result), 0);
        }

        internal void Decide() => this.session.Process(new ProductsDone(this.owner, true, false, new(this.owner, UnitKind.Test, "target0")), 0);

        internal void Finish() => this.session.Process(new CheckDone(null), 0);

        internal void Adopt(int count = 1)
        {
            this.Derive(count);
            for (var i = 0; i < count; i++)
            {
                this.Complete(i);
            }

            this.Decide();
            this.Finish();
        }

        internal void Edit(int startLine, int startCharacter, int endLine, int endCharacter, string replacement)
            => this.Message(LspMethods.DidChange, new DidChangeTextDocumentParams { TextDocument = new() { Uri = this.source.ToUri(), Version = ++this.version }, ContentChanges = [new() { Range = new(new(startLine, startCharacter), new(endLine, endCharacter)), Text = replacement }] });

        internal void Replace(string replacement)
            => this.Message(LspMethods.DidChange, new DidChangeTextDocumentParams { TextDocument = new() { Uri = this.source.ToUri(), Version = ++this.version }, ContentChanges = [new() { Text = replacement }] });

        internal async Task<JsonElement> Hover(int line = 0, int character = 8)
        {
            var id = ++this.id;
            this.session.Process(new LspMessage { Method = LspMethods.Hover, Id = new(id, null), Params = new HoverParams { TextDocument = new() { Uri = this.source.ToUri() }, Position = new(line, character) } }, 0);
            await this.sender.FlushAsync();
            using var stream = new MemoryStream(this.output.ToArray());
            var reader = new LspFrameReader(stream);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken) is { } frame)
            {
                using var json = JsonDocument.Parse(frame.Buffer.AsMemory(0, frame.Length));
                ArrayPool<byte>.Shared.Return(frame.Buffer);
                if (json.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id)
                {
                    return json.RootElement.GetProperty("result").Clone();
                }
            }

            throw new InvalidOperationException("Missing Hover response");
        }

        private void Message(string method, object parameters)
            => this.session.Process(new LspMessage { Method = method, Params = parameters, Id = method == LspMethods.Initialize ? new(1, null) : null }, 0);
    }
}
