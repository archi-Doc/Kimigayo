// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.3.6: independent expectations for aggregation, prerequisite graphs and finalization boundaries.
public sealed class DiagnosticContractTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryErrorOfAPrerequisiteMustBeExplained(bool reverse)
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main").For(new("main.kimi", "abc"));
        var cause = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Startup);
        var use = new DiagnosticKey(null, 0, 2, 1, DiagnosticRequirement.Startup);
        void Direct() => target.Report(DiagnosticPartition.Startup, cause, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null);
        void Unresolved() => target.Report(DiagnosticPartition.Startup, cause, new(0, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, [DiagnosticKey.Unresolved], null);
        if (reverse)
        {
            Unresolved();
            Direct();
        }
        else
        {
            Direct();
            Unresolved();
        }

        target.Report(DiagnosticPartition.Startup, use, new(2, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, [cause], null);
        var result = owner.Finalize(rejected: true);
        Assert.Equal(["MissingStartupBody_Kd", "PrerequisiteUnavailable_Kd", "PrerequisiteUnavailable_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(2, 1), result.Diagnostics[^1].Span);
    }

    // SPEC 23.3.6.5: a mismatch keeps its differing parts; a Type lying wholly inside the other, as T in ref/T, is bounded on
    // its own rather than shown as a bare elision mark.
    [Fact]
    public void AMismatchedTypeInsideTheOtherStaysReadable()
    {
        var inner = "(Long" + new string('A', 120) + ", i32)";
        var (first, second) = DiagnosticText.BoundPair(inner, "ref/" + inner);
        Assert.StartsWith("(LongAAA", first.Text, StringComparison.Ordinal);
        Assert.EndsWith(", i32)", first.Text, StringComparison.Ordinal);
        Assert.StartsWith("ref/(LongAAA", second.Text, StringComparison.Ordinal);
        Assert.True(first.Elided && second.Elided);
        Assert.True(first.Text.Length <= DiagnosticLimits.ValueLength && second.Text.Length <= DiagnosticLimits.ValueLength);
    }

    // SPEC 23.3.6.5: bounding and pair elision never split a surrogate pair, so displayed values stay valid UTF-16.
    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(150)]
    public void BoundingKeepsSurrogatePairsWhole(int offset)
    {
        var text = new string('a', offset) + "\U0001F600" + new string('b', 200 - offset);
        Assert.True(IsValidUtf16(DiagnosticText.Bound(text).Text));
        var other = new string('a', offset) + "\U0001F601" + new string('b', 200 - offset);
        var (first, second) = DiagnosticText.BoundPair(text, other);
        Assert.True(IsValidUtf16(first.Text) && IsValidUtf16(second.Text));
        Assert.Contains("\U0001F600", first.Text, StringComparison.Ordinal);
        Assert.Contains("\U0001F601", second.Text, StringComparison.Ordinal);

        static bool IsValidUtf16(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(value[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [Fact]
    public void ADirectErrorDoesNotBreakACyclicPrerequisiteAtItsKey()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main").For(new("main.kimi", "abc"));
        var key = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Startup);
        target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null);
        target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, [key], null);
        Assert.Equal(2, owner.Finalize(rejected: true).Diagnostics.Length);
    }

    [Fact]
    public void RelatedEvidenceMergesAndFollowsConsumedSourcesBeforeLimits()
    {
        Assert.Equal(Build(false), Build(true));

        static DiagnosticResult Build(bool reverse)
        {
            var owner = new DiagnosticOwner();
            var target = owner.GetOrAddCollection("main").For(new("main.kimi", "x"));
            var documents = Enumerable.Range(0, 10).Select(i => new SourceDocument($"candidate{i}.kimi", "f")).ToArray();
            foreach (var document in documents)
            {
                target.Register(document);
            }

            var key = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Binding(BindingFailure.NoApplicableCandidate));
            foreach (var i in reverse ? Enumerable.Range(0, 10).Reverse() : Enumerable.Range(0, 10))
            {
                var location = target.Relate("candidate", new(0, 1), documents[i], $"candidate {i}");
                target.Report(DiagnosticPartition.Binding, key, new(0, 1), DiagnosticCode.NoApplicableOverload_Kd, null, null, null, null, null, null, [10], [location, location]);
            }

            var result = owner.Finalize(rejected: true);
            Assert.Equal(new[] { "main.kimi" }.Concat(documents.Take(8).Select(static x => x.Path)), result.Sources.Select(static x => x.Path));
            var record = Assert.Single(result.Diagnostics);
            Assert.Equal(0, record.Source);
            Assert.Equal(Enumerable.Range(1, 8), record.Related!.Select(static x => x.Source));
            Assert.Equal(new DiagnosticOmission("related locations", 2), Assert.Single(record.Omissions!));
            return result;
        }
    }

    [Fact]
    public void RecordingCapturesPrerequisitesAndRelatedScratchArrays()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", "abc");
        var target = owner.GetOrAddCollection("main").For(document);
        var cause = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Startup);
        var use = new DiagnosticKey(null, 0, 2, 1, DiagnosticRequirement.Startup);
        DiagnosticKey[] prerequisites = [DiagnosticKey.Unresolved];
        DiagnosticRelatedFact[] related = [target.Relate("declaration", new(1, 1), document, "original")];
        target.Report(DiagnosticPartition.Startup, cause, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null, related: related);
        target.Report(DiagnosticPartition.Startup, use, new(2, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, prerequisites, null);
        prerequisites[0] = cause;
        related[0] = target.Relate("declaration", new(2, 1), document, "changed");
        var result = owner.Finalize(rejected: true);
        Assert.Equal(2, result.Diagnostics.Length);
        Assert.Equal("original", Assert.Single(result.Diagnostics[0].Related!).Label);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 2)]
    [InlineData(0, -1)]
    public void RelatedLocationsMustLieWithinTheirSnapshot(int start, int length)
    {
        var target = new DiagnosticOwner().GetOrAddCollection("main");
        var exception = Assert.Throws<DiagnosticContractException>(() => target.Relate("declaration", new(start, length), new("main.kimi", "abc"), null));
        Assert.Equal(DiagnosticFault.InvalidLocation, exception.Fault);
    }

    [Fact]
    public void NegativeCatalogCodeIsAnAnomalyRatherThanAnIndexFailure()
    {
        var (_, anomalies) = DiagnosticEntries.Load(Encoding.UTF8.GetBytes("  + Name=\"-1\"\n    Category=\"Language\"\n    Message=\"bad\"\n"));
        Assert.Contains("-1: no DiagnosticCode has this name.", anomalies);
    }

    [Fact]
    public void UndefinedSeverityIsACatalogAnomaly()
    {
        var (_, anomalies) = DiagnosticEntries.Load(Encoding.UTF8.GetBytes("  + Name=\"IdentifierExpected_Kd\"\n    Category=\"Language\"\n    Severity=\"0\"\n    Message=\"bad\"\n"));
        Assert.Contains("IdentifierExpected_Kd: the entry has no valid severity.", anomalies);
    }

    [Theory]
    [InlineData("spaces:0")]
    [InlineData(" :Number")]
    public void SchemasRequireNamedKindsAndNonemptyFactNames(string schema)
    {
        var entry = new DiagnosticEntry("test", DiagnosticSeverity.Error, "{0}") { Arguments = schema };
        Assert.NotNull(entry.Prepare());
    }

    [Fact]
    public void FactNamesCannotRepeatAcrossArgumentsAndEvidence()
    {
        var entry = new DiagnosticEntry("test", DiagnosticSeverity.Error, "{0}") { Arguments = "value:Text", Evidence = "value:Number" };
        Assert.NotNull(entry.Prepare());
    }

    [Fact]
    public void NumericFactsCannotBeProse()
    {
        var target = new DiagnosticOwner().GetOrAddCollection("main");
        var exception = Assert.Throws<DiagnosticContractException>(() => target.Add(default, DiagnosticCode.InvalidIndentation_Kd, "four spaces"));
        Assert.Equal(DiagnosticFault.InvalidArgument, exception.Fault);
    }

    [Fact]
    public void AnEmptyPrerequisiteSetKeepsTheDirectCodesFacts()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main").For(new("main.kimi", "x"));
        var key = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Syntax);
        target.Report(DiagnosticPartition.Syntax, key, new(0, 1), DiagnosticCode.InvalidIndentation_Kd, 4, null, null, null, [], null);
        var record = Assert.Single(owner.Finalize(rejected: true).Diagnostics);
        Assert.Equal(new DiagnosticValue("spaces", DiagnosticValueKind.Number, "4"), Assert.Single(record.Reason!));
        Assert.Null(record.Related);
    }

    [Fact]
    public void WarningOnlyAndInvalidatedPrerequisitesDoNotSuppressErrors()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main").For(new("main.kimi", "abc"));
        var cause = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Syntax);
        var use = new DiagnosticKey(null, 0, 2, 1, DiagnosticRequirement.Startup);
        target.Add(new(0, 1), DiagnosticCode.DeclarationOrderWarning_Kd);
        target.Report(DiagnosticPartition.Startup, use, new(2, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, [cause], null);
        Assert.Equal(2, owner.Finalize(rejected: true).Diagnostics.Length);
        target.Add(new(0, 1), DiagnosticCode.IdentifierExpected_Kd);
        Assert.DoesNotContain(owner.Finalize(rejected: true).Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        owner.Invalidate(DiagnosticPartition.Syntax);
        Assert.Equal(nameof(DiagnosticCode.PrerequisiteUnavailable_Kd), Assert.Single(owner.Finalize(rejected: true).Diagnostics).Code);
        Assert.True(owner.HasErrors);
    }

    [Fact]
    public void SourceTableKeepsDistinctSnapshotsOfOnePathInConsumptionOrder()
    {
        var owner = new DiagnosticOwner();
        var original = new SourceDocument("main.kimi", "old");
        var revised = new SourceDocument("main.kimi", "new");
        var target = owner.GetOrAddCollection("main").For(original);
        var other = target.For(revised);
        target.Add(new(0, 3), DiagnosticCode.IdentifierExpected_Kd);
        other.Add(new(0, 3), DiagnosticCode.IdentifierExpected_Kd);
        var result = owner.Finalize();
        Assert.Equal(2, result.Sources.Length);
        Assert.Equal([0, 1], result.Diagnostics.Select(static x => x.Source));
        Assert.Equal(["old", "new"], result.Diagnostics.Select(static x => Assert.Single(x.Display!.Excerpt).Text));
    }

    [Fact]
    public void AnExceptionRecordKeepsResultOrderAndUsesTheSharedNoteLimit()
    {
        var owner = new DiagnosticOwner();
        owner.GetOrAddCollection("main").For(new("main.kimi", "x")).Add(new(0, 1), DiagnosticCode.IdentifierExpected_Kd);
        var result = DiagnosticFaults.Create(DiagnosticFault.Exception, new string('x', 1000), "main.kimi", owner.Finalize());
        Assert.Equal(["CheckFaulted_Kd", "IdentifierExpected_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.True(result.Diagnostics[0].Note!.Length <= DiagnosticLimits.NoteLength);
    }

    [Fact]
    public void OutputAdaptersPreserveMergedEvidenceAndOmissions()
    {
        var c = Compilation.CreateForTest();
        var path = Path.GetFullPath("DiagnosticContract.kimi");
        var document = new SourceDocument(path, "use\nfirst\nsecond\n");
        var target = c.Kotonoha.DiagnosticCollection.For(document);
        c.Diagnostics.AddInput(document, c.Kotonoha);
        var key = target.KeyOf(document, new(0, 3), document, DiagnosticRequirement.Binding(BindingFailure.NoApplicableCandidate));
        target.Report(DiagnosticPartition.Binding, key, new(0, 3), DiagnosticCode.NoApplicableOverload_Kd, null, null, null, null, null, document, [2], [target.Relate("candidate", new(10, 6), document, "second candidate")]);
        target.Report(DiagnosticPartition.Binding, key, new(0, 3), DiagnosticCode.NoApplicableOverload_Kd, null, null, null, null, null, document, [2], [target.Relate("candidate", new(4, 5), document, "first candidate")]);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("No unique applicable overload can be established from the supplied arguments", record.Message);
        Assert.Equal("2", Assert.Single(record.Reason!).Value);
        Assert.Equal([4, 10], record.Related!.Select(static x => x.Span!.Value.Start));
        var console = new DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("first candidate", console.Text, StringComparison.Ordinal);
        Assert.True(console.Text.IndexOf("first candidate", StringComparison.Ordinal) < console.Text.IndexOf("second candidate", StringComparison.Ordinal));
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var check = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, result);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, capability)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            if (capability)
            {
                Assert.Equal([1, 2], sent.RelatedInformation!.Select(static x => x.Location.Range.Start.Line));
            }
            else
            {
                Assert.Contains("first candidate", sent.Message, StringComparison.Ordinal);
                Assert.Contains("second candidate", sent.Message, StringComparison.Ordinal);
            }

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    internal sealed class DiagnosticConsole : IConsoleService
    {
        private readonly StringBuilder text = new();

        public string Text => this.text.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message).Append('\n');

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message).Append('\n');

        public Task<InputResult> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept) => throw new NotSupportedException();
    }
}
