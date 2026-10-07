// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;

namespace Verification;

#pragma warning disable SA1201, SA1202, SA1204, SA1402, SA1600, SA1649 // Shared regression/measurement vocabulary.

internal static class HoverWorkloads
{
    internal const string Text = "struct Sample\n";
    internal const string ValueProgram = "/// A counter.\nstruct Counter\n/// - counter: The supplied counter.\nfunc inspect(counter: ref/Counter)\n    let count = 1\n    _ = count@copy\n    _ = counter\n";
    internal const string Program = "/// A value.\nstruct Sample\n    /// The count.\n    public var count: i32 = 0\n/// Reads the count.\nfunc read(value: ref/Sample) -> i32 => value.count\n/// Applies a callback.\nfunc apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\npublic func main() => ()\n";
    internal static readonly SourceIdentity Source = SourceIdentity.FromPath("temp/hover-workload/main.kimi");

    internal static HoverParticipant[] Create(string name, int configurations = 4)
    {
        var raw = name switch
        {
            "maximum-comment" => "/// " + new string('a', HoverLimits.Input - 4),
            "long-comment" => string.Concat(Enumerable.Repeat("/// A **documented** Type with [a guide](https://example.test/guide).\n///\n", 600)).TrimEnd('\n'),
            _ => "/// A **documented** Type with [a guide](https://example.test/guide).",
        };
        var result = new HoverParticipant[configurations];
        for (var i = 0; i < configurations; i++)
        {
            var declaration = new HoverDeclaration(
                "Type",
                "Project",
                new string("struct Sample".AsSpan()),
                [new("Project", Source.Value, new(7, 6), "main.kimi")],
                [new(new("doc.kimi", new string(raw.AsSpan())), new(0, raw.Length), 0, "Project", "doc.kimi", null, 0, default, [])],
                Identity: new("Sample", []));
            var identity = new HoverKey("leaf", []);
            if (name == "identity-dag")
            {
                for (var level = 0; level < 40; level++)
                {
                    identity = new("pair", [identity, identity]);
                }
            }
            else if (name == "wide-identity")
            {
                identity = Tree(14);
            }

            var effects = name == "long-effects" ? new string('e', 60_000) : null;
            var info = new HoverInfo([declaration], CopyType: "Sample", Copy: ConstraintProof.Refuted, Effects: effects, TypeIdentity: identity);
            var owner = SourceIdentity.FromPath("temp/hover-workload/App.kimiproj");
            result[i] = new(new(owner, UnitKind.Product, "target" + i), i + 1, new(new(Source.Value, Text), [new(new(7, 6), info)]));
        }

        return result;
    }

    internal static HoverParticipant[] CreateValues(int configurations = 4)
    {
        var result = new HoverParticipant[configurations];
        for (var i = 0; i < configurations; i++)
        {
            var compilation = Compilation.CreateForTest();
            if (!compilation.Prepare("x86_64-pc-windows-msvc"))
            {
                throw new InvalidOperationException("The Hover workload target must be prepared");
            }

            compilation.CollectHover = true;
            compilation.CollectDocumentation = true;
            compilation.Kotonoha.AddSource(new(Source.Value, new string(ValueProgram.AsSpan())));
            if (!compilation.Bind().IsComplete)
            {
                throw new InvalidOperationException("The variable and operation workload must bind");
            }

            var document = compilation.Binding.CreateHoverSnapshot().Documents[Source];
            result[i] = new(new(SourceIdentity.FromPath("temp/hover-workload/App.kimiproj"), UnitKind.Product, "target" + i), i + 1, document);
        }

        return result;
    }

    internal static HoverSessionWorkload Session(HoverParticipant[] participants, string source = Text, SourcePosition? position = null)
        => new(participants, source, position);

    private static HoverKey Tree(int depth)
        => depth == 0 ? new("leaf", []) : new("node", [Tree(depth - 1), Tree(depth - 1)]);
}

internal sealed class HoverSessionWorkload : IDisposable
{
    internal LspSender Sender { get; } = new(Stream.Null);

    internal LspSession Session { get; }

    internal LspMessage Hover { get; }

    internal LspMessage Edit { get; } = new() { Method = LspMethods.DidChange, Params = new DidChangeTextDocumentParams { TextDocument = new() { Uri = HoverWorkloads.Source.ToUri(), Version = 2 }, ContentChanges = [new() { Range = new(new(0, 0), new(0, 0)), Text = string.Empty }] } };

    internal HoverSessionWorkload(HoverParticipant[] participants, string source, SourcePosition? position)
    {
        this.Hover = new() { Method = LspMethods.Hover, Id = new(2, null), Params = new HoverParams { TextDocument = new() { Uri = HoverWorkloads.Source.ToUri() }, Position = position ?? new(0, 8) } };
        this.Session = new(this.Sender, static _ => { }, static _ => { });
        using var settings = JsonDocument.Parse("{\"checkQuietPeriodMs\":0}");
        this.Session.Process(new LspMessage { Method = LspMethods.Initialize, Id = new(1, null), Params = new InitializeParams { InitializationOptions = settings.RootElement.Clone(), Capabilities = new() { TextDocument = new() { Hover = new() { ContentFormat = ["markdown"] } } } } }, 0);
        this.Session.Process(new LspMessage { Method = LspMethods.DidOpen, Params = new DidOpenTextDocumentParams { TextDocument = new() { Uri = HoverWorkloads.Source.ToUri(), Text = source, Version = 1 } } }, 0);
        this.Session.Tick(0);
        var file = InputKey.File(HoverWorkloads.Source);
        this.Session.Process(new CommitRequest([(file, new() { Content = SourceContent.FromText(source, false), Overlay = true })], new()), 0);
        var owner = participants[0].Key.Owner;
        var project = new LoadedProject { Path = owner, Members = [HoverWorkloads.Source], ProductMembers = [HoverWorkloads.Source] };
        this.Session.Process(new DerivationDone { NewProjects = [project], Reached = [owner], Record = new(), Required = new(participants.Select(static p => p.Key)), KeepOwners = [], TestSourceOwners = [] }, 0);
        foreach (var participant in participants)
        {
            var result = new UnitResult
            {
                Key = participant.Key,
                Output = new(CheckOutcome.Completed, true, TestPresence.No, DiagnosticResult.Empty) { Hover = new([]) { Documents = new Dictionary<SourceIdentity, HoverDocument> { [HoverWorkloads.Source] = participant.Document } } },
                Reports = [],
                Inputs = [new(file, this.Session.Store.Find(file)!.Revision, null)],
            };
            this.Session.Process(new UnitDone(result), 0);
        }

        this.Session.Process(new ProductsDone(owner, true, false, new(owner, UnitKind.Test, "target0")), 0);
        this.Session.Process(new CheckDone(null), 0);
        this.Sender.FlushAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        this.Session.Dispose();
        this.Sender.DrainAsync().GetAwaiter().GetResult();
    }
}
