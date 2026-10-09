// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 23.3.3 and 23.3.6: one owner per check request, partitions with an error state independent of display,
// records ordered by source table and span, one fault record, and a JSON form that keeps every field.
public sealed class DiagnosticOwnerTest
{
    [Fact]
    public void RecordsFollowSourceTableThenSpanWithSourcelessLast()
    {
        var owner = new DiagnosticOwner();
        var first = new SourceDocument("first.kimi", "abc def");
        var second = new SourceDocument("second.kimi", "xyz");
        var target = owner.GetOrAddCollection("first.kimi").For(first);
        var other = owner.GetOrAddCollection("second.kimi").For(second);
        other.Add(new(0, 1), DiagnosticCode.IndentationLevelMismatch_Kd);
        target.Add(new(4, 3), DiagnosticCode.IndentationLevelMismatch_Kd);
        target.Add(new(0, 3), DiagnosticCode.SemicolonNotAllowed_Kd);
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, null, note: "no source");
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.SourceReadFailed_Kd, "third.kimi", note: "whole input");

        var result = owner.Finalize();
        Assert.Equal(["first.kimi", "second.kimi", "third.kimi"], result.Sources.Select(static x => x.Path));
        Assert.Equal(
            ["SemicolonNotAllowed_Kd", "IndentationLevelMismatch_Kd", "IndentationLevelMismatch_Kd", "SourceReadFailed_Kd", "ProjectPreparationFailed_Kd"],
            result.Diagnostics.Select(static x => x.Code));
        Assert.Null(result.Diagnostics[3].Span);
        Assert.Equal(-1, result.Diagnostics[^1].Source);
        Assert.Equal(new SourceRange(new(0, 4), new(0, 7)), result.Diagnostics[1].Display!.Range);
        Assert.Equal(new DiagnosticExcerptLine(1, "abc def", 4, 3), Assert.Single(result.Diagnostics[1].Display!.Excerpt));
    }

    [Fact]
    public void InvalidationDiscardsOnlyItsPartitions()
    {
        var c = Compilation.CreateForTest();
        var document = new SourceDocument("main.kimi", "value");
        var target = c.Kotonoha.DiagnosticCollection;
        var key = target.KeyOf(document, new(0, 5), document, DiagnosticRequirement.Binding(BindingFailure.MissingName));
        target.Report(DiagnosticPartition.Binding, key, new(0, 5), DiagnosticCode.UnresolvedBinding_Kd, null, null, null, null, document);
        c.Kotonoha.DiagnosticCollection.Add(new(0, 5), DiagnosticCode.IndentationLevelMismatch_Kd, sourceDocument: document);
        c.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, "App.kimiproj", note: "kept");
        Assert.True(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding));

        c.Diagnostics.InvalidateSemantics();
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding));
        Assert.True(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
        Assert.Equal(["ProjectPreparationFailed_Kd", "IndentationLevelMismatch_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code).Order(StringComparer.Ordinal).Reverse());
    }

    [Fact]
    public void ALongLineIsClippedAroundItsUnderline()
    {
        var owner = new DiagnosticOwner();
        var text = new string('a', 300) + "bad" + new string('z', 300);
        owner.GetOrAddCollection("long.kimi").For(new SourceDocument("long.kimi", text)).Add(new(300, 3), DiagnosticCode.IndentationLevelMismatch_Kd);
        var line = Assert.Single(Assert.Single(owner.Finalize().Diagnostics).Display!.Excerpt);
        Assert.True(line.Text.Length <= 162, line.Text);
        Assert.StartsWith("…", line.Text, StringComparison.Ordinal);
        Assert.EndsWith("…", line.Text, StringComparison.Ordinal);
        Assert.Equal("bad", line.Text.Substring(line.Start, line.Length));
    }

    [Fact]
    public void ALongTabbedExcerptPreservesItsAnchorAndOriginalRange()
    {
        var owner = new DiagnosticOwner();
        var prefix = string.Concat(Enumerable.Repeat("\tab", 1000));
        var document = new SourceDocument("tabs.kimi", prefix + "bad" + new string('z', 500));
        owner.GetOrAddCollection("tabs").For(document).Add(new(prefix.Length, 3), DiagnosticCode.IndentationLevelMismatch_Kd);
        var record = Assert.Single(owner.Finalize().Diagnostics);
        var line = Assert.Single(record.Display!.Excerpt);
        Assert.True(line.Text.Length <= DiagnosticLimits.ExcerptWidth + 2);
        Assert.Equal("bad", line.Text.Substring(line.Start, line.Length));
        Assert.Equal(new SourceRange(new(0, prefix.Length), new(0, prefix.Length + 3)), record.Display.Range);
        Assert.StartsWith("…", line.Text, StringComparison.Ordinal);
        Assert.EndsWith("…", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongLineEndInsertionKeepsOneCaretAfterItsText()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("end.kimi", new string('x', 10000));
        owner.GetOrAddCollection("end").For(document).Add(new(10000, 0), DiagnosticCode.IndentationLevelMismatch_Kd);
        var line = Assert.Single(Assert.Single(owner.Finalize().Diagnostics).Display!.Excerpt);
        Assert.Equal(line.Text.Length, line.Start);
        Assert.Equal(1, line.Length);
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public sealed class AllocationTests
    {
        [Fact]
        public void ExcerptAllocationDoesNotGrowWithUnquotedLineLength()
        {
            var small = Create(2000);
            var large = Create(1000000);
            var smallBytes = AllocationMeasurement.Measure(() => small.Finalize());
            var largeBytes = AllocationMeasurement.Measure(() => large.Finalize());
            Assert.True(largeBytes <= smallBytes, $"Short: {smallBytes}; long: {largeBytes}");

            static DiagnosticOwner Create(int prefix)
            {
                var owner = new DiagnosticOwner();
                var document = new SourceDocument("long.kimi", new string('x', prefix) + "bad" + new string('z', 300));
                owner.GetOrAddCollection("long").For(document).Add(new(prefix, 3), DiagnosticCode.IndentationLevelMismatch_Kd);
                owner.Finalize(); // Source line indexing is input preparation; measure only repeated record formation.
                return owner;
            }
        }
    }

    // SPEC 23.3.6.3: the project file is consumed before every source, so a fault at it that no kept record names takes the
    // first source table entry, and the kept records keep their own sources.
    [Fact]
    public void AFaultAtTheProjectFileKeepsConsumptionOrder()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", "let x = 1\n");
        owner.GetOrAddCollection("main.kimi").For(document).Add(new SourceSpan(4, 1), DiagnosticCode.InvalidCharacter_Kd, "x");
        var result = DiagnosticFaults.Create(DiagnosticFault.Exception, "detail", "App.kimiproj", owner.Finalize());
        Assert.Equal(["App.kimiproj", "main.kimi"], result.Sources.Select(static x => x.Path));
        Assert.Equal(["CheckFaulted_Kd", "InvalidCharacter_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal([0, 1], result.Diagnostics.Select(static x => x.Source));

        // A project file registered when the check starts keeps its place before later sources in ordinary records too.
        var ordered = new DiagnosticOwner();
        ordered.RegisterPath("App.kimiproj");
        ordered.GetOrAddCollection("main.kimi").For(document).Add(new SourceSpan(4, 1), DiagnosticCode.InvalidCharacter_Kd, "x");
        ordered.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, "App.kimiproj", note: "late");
        Assert.Equal(["App.kimiproj", "main.kimi"], ordered.Finalize().Sources.Select(static x => x.Path));
    }

    [Fact]
    public void TheFaultRecordMatchesTheCatalogAndReplacesOrKeepsRecords()
    {
        Assert.True(DiagnosticEntries.TryGet(DiagnosticCode.CheckFaulted_Kd, out var entry));
        Assert.Equal(DiagnosticSeverity.Error, entry.Severity);
        Assert.Equal(DiagnosticCategory.Internal, entry.Category);
        Assert.Equal(new DiagnosticParameter("fault", DiagnosticValueKind.Enumeration, false), Assert.Single(entry.ArgumentSchema));
        foreach (var fault in Enum.GetValues<DiagnosticFault>())
        {
            var record = Assert.Single(DiagnosticFaults.Create(fault, "detail", "App.kimiproj").Diagnostics);
            Assert.Equal(entry.FormatMessage(DiagnosticFaults.Describe(fault), null), record.Message);
            Assert.Equal(new DiagnosticValue("fault", DiagnosticValueKind.Enumeration, fault.ToString()), Assert.Single(record.Reason!));
            Assert.Equal("detail", record.Note);
        }

        var owner = new DiagnosticOwner();
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, "App.kimiproj", note: "kept");
        var kept = DiagnosticFaults.Create(DiagnosticFault.Exception, new string('x', 1000), "App.kimiproj", owner.Finalize());
        // Whole-input records at the same source follow code order, including an appended analysis fault.
        Assert.Equal(["CheckFaulted_Kd", "ProjectPreparationFailed_Kd"], kept.Diagnostics.Select(static x => x.Code));
        Assert.Single(kept.Sources);
        Assert.All(kept.Diagnostics, static x => Assert.Equal(0, x.Source));
        Assert.True(kept.Diagnostics[0].Note!.Length <= DiagnosticLimits.NoteLength);
    }

    [Fact]
    public void TheJsonFormKeepsEveryField()
    {
        var owner = new DiagnosticOwner();
        owner.GetOrAddCollection("main.kimi").For(new SourceDocument("main.kimi", "let ok = a < b < c\n")).Add(new(9, 9), DiagnosticCode.ChainedComparison_Kd);
        var result = DiagnosticFaults.Create(DiagnosticFault.Exception, "detail", "App.kimiproj", owner.Finalize());

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"category\":\"Language\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label\":\"This comparison requires explicit grouping\"", json, StringComparison.Ordinal);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // SPEC 23.3.6.8, 23.3.6.9: the JSON form keeps every field of a repair candidate, and equality compares candidates.
    [Fact]
    public void TheJsonFormKeepsRepairCandidates()
    {
        var owner = new DiagnosticOwner();
        var document = new SourceDocument("main.kimi", "consume(resource)\n");
        var target = owner.GetOrAddCollection("main.kimi").For(document);
        var key = new DiagnosticKey(null, 0, 8, 8, DiagnosticRequirement.Ownership(OwnershipFailure.TransferRequired));
        target.Report(DiagnosticPartition.Ownership, key, new(8, 8), DiagnosticCode.TransferRequired_Kd, null, null, null, null, document, repairs:
            [new(RepairKind.Transfer, ["resource", "consume(value: Resource)"], [target.Edit(new(16, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality)]);
        var result = owner.Finalize();

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"repairs\":[{\"kind\":\"Repair.Transfer\",\"title\":\"Append @move to transfer resource to consume(value: Resource)\"", json, StringComparison.Ordinal);
        Assert.Contains("\"edits\":[{\"source\":0,\"span\":{\"start\":16,\"length\":0},\"range\":{\"start\":{\"line\":0,\"character\":16},\"end\":{\"line\":0,\"character\":16}},\"text\":\"@move\"}]", json, StringComparison.Ordinal);
        Assert.Contains("\"verified\":[\"Take\"],\"required\":[{\"condition\":\"UsageLegality\",\"phrase\":", json, StringComparison.Ordinal);
        var read = JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult)!;
        Assert.Equal(result, read);
        Assert.NotEqual(result, read with { Diagnostics = [read.Diagnostics[0] with { Repairs = null }] });
    }

    [Fact]
    public void TheRequirementTableDescribesEveryRequirementOnce()
    {
        Assert.Empty(DiagnosticRequirements.Anomalies);
        Assert.All(DiagnosticRequirements.All, static x => Assert.True(DiagnosticRequirements.TryGetDescription(x, out _), x.Name));
        var text = """
              + Name="Syntax"
                Description="a"

              + Name="Syntax"
                Description="b"

              + Name="Nope"
                Description="c"
            """;
        var (_, anomalies) = DiagnosticRequirements.Load(System.Text.Encoding.UTF8.GetBytes(text));
        Assert.Contains("Syntax: the entry is duplicated.", anomalies);
        Assert.Contains("Nope: no requirement has this name.", anomalies);
        Assert.Contains("Input: the requirement has no entry.", anomalies);
    }

    [Fact]
    public void ARepeatedProblemMergesAndAConflictingOneIsAContractViolation()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main.kimi").For(new SourceDocument("main.kimi", "abc"));
        target.Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "a");
        target.Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "a", note: "a note");
        var record = Assert.Single(owner.Finalize().Diagnostics);
        Assert.Equal("a note", record.Note);

        var key = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Startup);
        target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null);
        var exception = Assert.Throws<DiagnosticContractException>(() => { target.Report(DiagnosticPartition.Startup, key, new(1, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null); });
        Assert.Equal(DiagnosticFault.ConflictingProblem, exception.Fault);
        exception = Assert.Throws<DiagnosticContractException>(() => { target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, null); });
        Assert.Equal(DiagnosticFault.InvalidArgument, exception.Fault);
    }

    [Fact]
    public void ADerivedProblemIsSuppressedOnlyWhenItsPrerequisitesLeadToPublishedDirectErrors()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main.kimi").For(new SourceDocument("main.kimi", "a b c d e"));
        var cause = new DiagnosticKey(null, 0, 0, 1, DiagnosticRequirement.Syntax, 0, "a");
        target.Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "a");
        var chained = Key(2);
        Derive(chained, [cause]);
        Derive(Key(4), [chained]);
        Derive(Key(6), [DiagnosticKey.Unresolved]);
        var first = Key(8);
        var second = new DiagnosticKey(null, 0, 8, 1, DiagnosticRequirement.Emission);
        Derive(first, [second]);
        target.Report(DiagnosticPartition.Emission, second, new(8, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, [first], null);

        // The chain resolves to the direct error; the unresolved mark and the cycle are published.
        var result = owner.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission);
        Assert.Equal(["InvalidCharacter_Kd", "PrerequisiteUnavailable_Kd", "PrerequisiteUnavailable_Kd", "PrerequisiteUnavailable_Kd"], result.Diagnostics.Select(static x => x.Code));
        var unresolved = result.Diagnostics[1];
        Assert.Equal(new SourceSpan(6, 1), unresolved.Span);
        Assert.Equal("Startup", unresolved.Reason![0].Value);
        Assert.Equal("the program has a valid startup", unresolved.Reason[1].Value);
        Assert.Null(unresolved.Related);
        var cyclic = result.Diagnostics[2];
        Assert.Equal(new DiagnosticRelated("prerequisite", 0, new(8, 1), new SourceRange(new(0, 8), new(0, 9)), "generation succeeds"), Assert.Single(cyclic.Related!));

        DiagnosticKey Key(int start) => new(null, 0, start, 1, DiagnosticRequirement.Startup);

        void Derive(DiagnosticKey key, DiagnosticKey[] prerequisites)
            => target.Report(DiagnosticPartition.Startup, key, new(key.Start, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, prerequisites, null);
    }

    [Fact]
    public void DistinctProblemsWithoutADefinedOrderAreAContractViolation()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main.kimi").For(new SourceDocument("main.kimi", "abc"));
        var first = new DiagnosticKey(new object(), 0, 0, 1, DiagnosticRequirement.Startup);
        var second = new DiagnosticKey(new object(), 0, 0, 1, DiagnosticRequirement.Startup);
        target.Report(DiagnosticPartition.Startup, first, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null);
        target.Report(DiagnosticPartition.Startup, second, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null);
        Assert.Equal(DiagnosticFault.UndefinedOrder, Assert.Throws<DiagnosticContractException>(() => owner.Finalize()).Fault);
    }

    // SPEC 23.3.6.2, 23.3.6.5: the Reason is typed by the catalog; two long Types keep their differences and elide the rest;
    // environment text is a bounded Note; limits count what they omit.
    [Fact]
    public void ExplanationsAreTypedBoundedAndCountTheirOmissions()
    {
        var owner = new DiagnosticOwner();
        var text = string.Concat(Enumerable.Range(0, 12).Select(static i => $"line{i}\n"));
        var document = new SourceDocument("main.kimi", text);
        var target = owner.GetOrAddCollection("main.kimi").For(document);
        var common = new string('A', 120);
        var mismatch = target.KeyOf(document, new(0, 5), document, DiagnosticRequirement.Binding(BindingFailure.TypeMismatch));
        target.Report(DiagnosticPartition.Binding, mismatch, new(0, 5), DiagnosticCode.TypeMismatch_Kd, null, null, null, null, document, [$"Pair<{common}, i32>", $"Pair<{common}, bool>"]);

        // A derived problem spanning many lines with more prerequisites than the limit.
        var prerequisites = Enumerable.Range(0, 10).Select(i => new DiagnosticKey(null, 0, i * 6, 5, DiagnosticRequirement.Syntax)).ToArray();
        var derived = target.KeyOf(document, new(6, 60), document, DiagnosticRequirement.Startup);
        target.Report(DiagnosticPartition.Startup, derived, new(6, 60), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, prerequisites, document);
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, "App.kimiproj", note: new string('x', 1000));

        var records = owner.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Startup).Diagnostics;
        var typed = Assert.Single(records, static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Assert.Equal(["actual", "expected"], typed.Reason!.Select(static x => x.Name));
        Assert.Equal(["…i32…", "…bool…"], typed.Reason!.Select(static x => x.Value));
        Assert.All(typed.Reason!, static x => Assert.True(x.Elided));
        Assert.Equal("expected …bool…, found …i32…", typed.Label);

        var undecided = Assert.Single(records, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        Assert.Equal(8, undecided.Related!.Length);
        Assert.Equal([new DiagnosticOmission("excerpt lines", 6), new DiagnosticOmission("related locations", 2)], undecided.Omissions!);

        var input = Assert.Single(records, static x => x.Code == nameof(DiagnosticCode.ProjectPreparationFailed_Kd));
        Assert.Equal(DiagnosticLimits.NoteLength, input.Note!.Length);
        Assert.Contains(DiagnosticText.Elision, input.Note, StringComparison.Ordinal);
    }
}
