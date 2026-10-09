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
        void Direct() => target.Report(DiagnosticPartition.Startup, cause, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null);
        void Unresolved() => target.Report(DiagnosticPartition.Startup, cause, new(0, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, [DiagnosticKey.Unresolved], null);
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

        target.Report(DiagnosticPartition.Startup, use, new(2, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, [cause], null);
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
        target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null);
        target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, [key], null);
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
                target.Report(DiagnosticPartition.Binding, key, new(0, 1), DiagnosticCode.NoApplicableOverload_Kd, null, null, null, null, null, [10], [location, location]);
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
        target.Report(DiagnosticPartition.Startup, cause, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, related: related);
        target.Report(DiagnosticPartition.Startup, use, new(2, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, prerequisites, null);
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
        var (_, anomalies) = DiagnosticEntries.Load(Encoding.UTF8.GetBytes("  + Name=\"IndentationLevelMismatch_Kd\"\n    Category=\"Language\"\n    Severity=\"0\"\n    Message=\"bad\"\n"));
        Assert.Contains("IndentationLevelMismatch_Kd: the entry has no valid severity.", anomalies);
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
        target.Report(DiagnosticPartition.Syntax, key, new(0, 1), DiagnosticCode.InvalidIndentation_Kd, 4, null, null, [], null);
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
        target.Report(DiagnosticPartition.Startup, use, new(2, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, [cause], null);
        Assert.Equal(2, owner.Finalize(rejected: true).Diagnostics.Length);
        target.Add(new(0, 1), DiagnosticCode.IndentationLevelMismatch_Kd);
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
        target.Add(new(0, 3), DiagnosticCode.IndentationLevelMismatch_Kd);
        other.Add(new(0, 3), DiagnosticCode.IndentationLevelMismatch_Kd);
        var result = owner.Finalize();
        Assert.Equal(2, result.Sources.Length);
        Assert.Equal([0, 1], result.Diagnostics.Select(static x => x.Source));
        Assert.Equal(["old", "new"], result.Diagnostics.Select(static x => Assert.Single(x.Display!.Excerpt).Text));
    }

    [Fact]
    public void AnExceptionRecordKeepsResultOrderAndUsesTheSharedNoteLimit()
    {
        var owner = new DiagnosticOwner();
        owner.GetOrAddCollection("main").For(new("main.kimi", "x")).Add(new(0, 1), DiagnosticCode.IndentationLevelMismatch_Kd);
        var result = DiagnosticFaults.Create(DiagnosticFault.Exception, new string('x', 1000), "main.kimi", owner.Finalize());
        Assert.Equal(["CheckFaulted_Kd", "IndentationLevelMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
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
        target.Report(DiagnosticPartition.Binding, key, new(0, 3), DiagnosticCode.NoApplicableOverload_Kd, null, null, null, null, document, [2], [target.Relate("candidate", new(10, 6), document, "second candidate")]);
        target.Report(DiagnosticPartition.Binding, key, new(0, 3), DiagnosticCode.NoApplicableOverload_Kd, null, null, null, null, document, [2], [target.Relate("candidate", new(4, 5), document, "first candidate")]);
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

    // SPEC 23.3.6.9: a candidate is finalized with its title, bounded facts, located edits and judged conditions; candidates are
    // ordered by kind and then by their first edit, and two insertions at one point are one edit.
    [Fact]
    public void RepairCandidatesAreFinalizedInCatalogOrder()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", "let a = b\nconsume(resource)\n");
        var target = owner.GetOrAddCollection("main").For(document);
        var key = new DiagnosticKey(null, 0, 18, 8, DiagnosticRequirement.Ownership(OwnershipFailure.TransferRequired));
        DiagnosticRepairFact[] repairs =
        [
            new(RepairKind.ReplaceToken, ["b", "c"], [target.Edit(new(8, 1), "c")], RepairConditionSet.None),
            new(RepairKind.Transfer, ["resource", "consume(value: Resource)"], [target.Edit(new(26, 0), "ve"), target.Edit(new(26, 0), "@mo")], RepairConditionSet.Take, RepairConditionSet.UsageLegality),
        ];
        target.Report(DiagnosticPartition.Ownership, key, new(18, 8), DiagnosticCode.TransferRequired_Kd, null, null, null, null, document, repairs: repairs);

        var record = Assert.Single(owner.Finalize().Diagnostics);
        var candidates = record.Repairs!;
        Assert.Equal(["Repair.Transfer", "Repair.ReplaceToken"], candidates.Select(static x => x.Kind));
        var transfer = candidates[0];
        Assert.Equal("Append @move to transfer resource to consume(value: Resource)", transfer.Title);
        Assert.Equal([new("place", DiagnosticValueKind.Text, "resource"), new("target", DiagnosticValueKind.Text, "consume(value: Resource)")], transfer.Facts!);
        Assert.Equal(new RepairEdit(0, new(26, 0), new SourceRange(new(1, 16), new(1, 16)), "ve@mo"), Assert.Single(transfer.Edits));
        Assert.Equal([RepairCondition.Take], transfer.Verified);
        Assert.Equal([new RequiredCondition(RepairCondition.UsageLegality, "the edited operation and every later use of resource satisfy the initialization, Loan and lifetime conditions")], transfer.Required);
        var replace = candidates[1];
        Assert.Equal("Replace 'b' with 'c'", replace.Title);
        Assert.Equal(new RepairEdit(0, new(8, 1), new SourceRange(new(0, 8), new(0, 9)), "c", "b"), Assert.Single(replace.Edits));
        Assert.Empty(replace.Verified);
        Assert.Empty(replace.Required);
        Assert.Null(record.Omissions);
    }

    // SPEC 23.3.6.5, 23.3.6.9: a candidate beyond the count limit, or with too many edits or too much text, is omitted whole and counted.
    [Fact]
    public void RepairCandidatesBeyondTheLimitsAreOmittedWhole()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", new string('x', 100));
        var target = owner.GetOrAddCollection("main").For(document);
        var key = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Syntax);
        var repairs = new List<DiagnosticRepairFact>
        {
            new(RepairKind.InsertToken, ["a"], Enumerable.Range(0, DiagnosticLimits.RepairEdits + 1).Select(i => target.Edit(new(i, 0), "a")).ToArray(), RepairConditionSet.None),
            new(RepairKind.InsertToken, ["b"], [target.Edit(new(0, 0), new string('b', DiagnosticLimits.RepairText + 1))], RepairConditionSet.None),
        };
        for (var i = 0; i <= DiagnosticLimits.Repairs; i++)
        {
            repairs.Add(new(RepairKind.InsertToken, ["c"], [target.Edit(new(i + 1, 0), "c")], RepairConditionSet.None));
        }

        target.Report(DiagnosticPartition.Syntax, key, new(0, 1), DiagnosticCode.IndentationLevelMismatch_Kd, null, null, null, null, document, repairs: repairs.ToArray());
        var record = Assert.Single(owner.Finalize().Diagnostics);
        Assert.Equal(DiagnosticLimits.Repairs, record.Repairs!.Length);
        Assert.All(record.Repairs, static x => Assert.Equal("c", Assert.Single(x.Edits).Text));
        Assert.Equal([new DiagnosticOmission("repair candidates", 3)], record.Omissions!);
    }

    // SPEC 23.3.6.7: a malformed candidate is a compiler defect, never a property of the checked source.
    [Theory]
    [InlineData("no edits", DiagnosticFault.InvalidArgument)]
    [InlineData("overlap", DiagnosticFault.InvalidLocation)]
    [InlineData("outside", DiagnosticFault.InvalidLocation)]
    [InlineData("built-in", DiagnosticFault.InvalidLocation)]
    [InlineData("derived", DiagnosticFault.InvalidArgument)]
    [InlineData("conditions", DiagnosticFault.InvalidArgument)]
    [InlineData("facts", DiagnosticFault.InvalidArgument)]
    [InlineData("conflict", DiagnosticFault.ConflictingProblem)]
    public void AnInvalidRepairCandidateIsAContractViolation(string shape, DiagnosticFault fault)
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", "consume(resource)");
        var builtIn = new SourceDocument("compiler://Kimi/Core.kimi", "struct Core");
        var target = owner.GetOrAddCollection("main").For(document);
        var key = new DiagnosticKey(null, 0, 8, 8, DiagnosticRequirement.Ownership(OwnershipFailure.TransferRequired));
        var exception = Assert.Throws<DiagnosticContractException>(() =>
        {
            DiagnosticRepairFact[] repairs = shape switch
            {
                "no edits" => [new(RepairKind.Transfer, ["resource", "consume"], [], RepairConditionSet.Take, RepairConditionSet.UsageLegality)],
                "overlap" => [new(RepairKind.Transfer, ["resource", "consume"], [target.Edit(new(8, 8), "x"), target.Edit(new(10, 2), "y")], RepairConditionSet.Take, RepairConditionSet.UsageLegality)],
                "outside" => [new(RepairKind.Transfer, ["resource", "consume"], [target.Edit(new(17, 1), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality)],
                "built-in" => [new(RepairKind.Transfer, ["resource", "consume"], [target.Edit(new(0, 0), "@move", builtIn)], RepairConditionSet.Take, RepairConditionSet.UsageLegality)],
                "conditions" => [new(RepairKind.Transfer, ["resource", "consume"], [target.Edit(new(16, 0), "@move")], RepairConditionSet.Take | RepairConditionSet.UsageLegality, RepairConditionSet.UsageLegality)],
                "facts" => [new(RepairKind.Transfer, ["resource"], [target.Edit(new(16, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality)],
                _ => [new(RepairKind.Transfer, ["resource", "consume"], [target.Edit(new(16, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality)],
            };
            if (shape == "derived")
            {
                target.Report(DiagnosticPartition.Ownership, key, new(8, 8), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, [DiagnosticKey.Unresolved], document, repairs: repairs);
                return;
            }

            target.Report(DiagnosticPartition.Ownership, key, new(8, 8), DiagnosticCode.TransferRequired_Kd, null, null, null, null, document, repairs: repairs);
            if (shape == "conflict")
            {
                repairs[0] = repairs[0] with { Verified = RepairConditionSet.None, Required = RepairConditionSet.Take | RepairConditionSet.UsageLegality };
                target.Report(DiagnosticPartition.Ownership, key, new(8, 8), DiagnosticCode.TransferRequired_Kd, null, null, null, null, document, repairs: repairs);
            }
        });
        Assert.Equal(fault, exception.Fault);
    }

    // SPEC 23.3.6.8: a command renders each candidate after the Note with its title, one line per edit and its conditions.
    [Fact]
    public void TheCommandRendersRepairCandidates()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", "if a&&b => fire()\nconsume(resource)\n");
        var target = owner.GetOrAddCollection("main").For(document);
        var key = new DiagnosticKey(null, 0, 26, 8, DiagnosticRequirement.Ownership(OwnershipFailure.TransferRequired));
        target.Report(DiagnosticPartition.Ownership, key, new(26, 8), DiagnosticCode.TransferRequired_Kd, null, null, "resource is a let binding of a Non-Copy Type", null, document, repairs:
            [new(RepairKind.Transfer, ["resource", "consume(value: Resource)"], [target.Edit(new(34, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality)]);
        target.Report(DiagnosticPartition.Syntax, new(null, 0, 4, 2, DiagnosticRequirement.Syntax), new(4, 2), DiagnosticCode.IndentationLevelMismatch_Kd, null, null, null, null, document, repairs:
            [new(RepairKind.ReplaceToken, ["&&", "and"], [target.Edit(new(4, 2), " and ")], RepairConditionSet.None)]);
        var console = new DiagnosticConsole();
        new Kimigayo(console).Render(owner.Finalize(), string.Empty);
        var text = console.Text;
        output.WriteLine(text);
        Assert.Contains("Repair: Replace '&&' with 'and'\n = main.kimi:1:5: replace '&&' with ' and '\n", text, StringComparison.Ordinal);
        Assert.Contains("\nNote: resource is a let binding of a Non-Copy Type\nRepair: Append @move to transfer resource to consume(value: Resource)\n = main.kimi:2:17: insert '@move'\n = verified: Take; requires: the edited operation and every later use of resource satisfy the initialization, Loan and lifetime conditions\n", text, StringComparison.Ordinal);
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
