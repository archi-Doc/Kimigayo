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
        other.Add(new(0, 1), DiagnosticCode.IdentifierExpected_Kd);
        target.Add(new(4, 3), DiagnosticCode.IdentifierExpected_Kd);
        target.Add(new(0, 3), DiagnosticCode.IncompleteSyntax_Kd);
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, null, "no source");
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.SourceReadFailed_Kd, "third.kimi", "whole input");

        var result = owner.Finalize();
        Assert.Equal(["first.kimi", "second.kimi", "third.kimi"], result.Sources.Select(static x => x.Path));
        Assert.Equal(
            ["IncompleteSyntax_Kd", "IdentifierExpected_Kd", "IdentifierExpected_Kd", "SourceReadFailed_Kd", "ProjectPreparationFailed_Kd"],
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
        target.Report(DiagnosticPartition.Binding, key, new(0, 5), DiagnosticCode.UnresolvedBinding_Kd, null, null, null, null, null, document);
        c.Kotonoha.DiagnosticCollection.Add(new(0, 5), DiagnosticCode.IdentifierExpected_Kd, sourceDocument: document);
        c.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, "App.kimiproj", "kept");
        Assert.True(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding));

        c.Diagnostics.InvalidateSemantics();
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding));
        Assert.True(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
        Assert.Equal(["ProjectPreparationFailed_Kd", "IdentifierExpected_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code).Order(StringComparer.Ordinal).Reverse());
    }

    [Fact]
    public void ALongLineIsClippedAroundItsUnderline()
    {
        var owner = new DiagnosticOwner();
        var text = new string('a', 300) + "bad" + new string('z', 300);
        owner.GetOrAddCollection("long.kimi").For(new SourceDocument("long.kimi", text)).Add(new(300, 3), DiagnosticCode.IdentifierExpected_Kd);
        var line = Assert.Single(Assert.Single(owner.Finalize().Diagnostics).Display!.Excerpt);
        Assert.True(line.Text.Length <= 162, line.Text);
        Assert.StartsWith("…", line.Text, StringComparison.Ordinal);
        Assert.EndsWith("…", line.Text, StringComparison.Ordinal);
        Assert.Equal("bad", line.Text.Substring(line.Start, line.Length));
    }

    [Fact]
    public void TheFaultRecordMatchesTheCatalogAndReplacesOrKeepsRecords()
    {
        Assert.True(DiagnosticEntries.TryGet(DiagnosticCode.CheckFaulted_Kd, out var entry));
        Assert.Equal(DiagnosticSeverity.Error, entry.Severity);
        Assert.Equal(DiagnosticCategory.Internal, entry.Category);
        foreach (var fault in Enum.GetValues<DiagnosticFault>())
        {
            var record = Assert.Single(DiagnosticFaults.Create(fault, "detail", "App.kimiproj").Diagnostics);
            Assert.Equal(entry.FormatMessage(DiagnosticFaults.Describe(fault), null), record.Message);
            Assert.Equal(new DiagnosticValue("fault", DiagnosticValueKind.Enumeration, fault.ToString()), Assert.Single(record.Reason!));
            Assert.Equal("detail", record.Note);
        }

        var owner = new DiagnosticOwner();
        owner.Report(DiagnosticPartition.Input, DiagnosticCode.ProjectPreparationFailed_Kd, "App.kimiproj", "kept");
        var kept = DiagnosticFaults.Create(DiagnosticFault.Exception, new string('x', 1000), "App.kimiproj", owner.Finalize());
        Assert.Equal(["ProjectPreparationFailed_Kd", "CheckFaulted_Kd"], kept.Diagnostics.Select(static x => x.Code));
        Assert.Single(kept.Sources);
        Assert.All(kept.Diagnostics, static x => Assert.Equal(0, x.Source));
        Assert.True(kept.Diagnostics[^1].Note!.Length <= 401);
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
        target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null);
        var exception = Assert.Throws<DiagnosticContractException>(() => { target.Report(DiagnosticPartition.Startup, key, new(1, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null); });
        Assert.Equal(DiagnosticFault.ConflictingProblem, exception.Fault);
        exception = Assert.Throws<DiagnosticContractException>(() => { target.Report(DiagnosticPartition.Startup, key, new(0, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, null, null); });
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
        target.Report(DiagnosticPartition.Emission, second, new(8, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, [first], null);

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
            => target.Report(DiagnosticPartition.Startup, key, new(key.Start, 1), DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, prerequisites, null);
    }

    [Fact]
    public void DistinctProblemsWithoutADefinedOrderAreAContractViolation()
    {
        var owner = new DiagnosticOwner();
        var target = owner.GetOrAddCollection("main.kimi").For(new SourceDocument("main.kimi", "abc"));
        var first = new DiagnosticKey(new object(), 0, 0, 1, DiagnosticRequirement.Startup);
        var second = new DiagnosticKey(new object(), 0, 0, 1, DiagnosticRequirement.Startup);
        target.Report(DiagnosticPartition.Startup, first, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null);
        target.Report(DiagnosticPartition.Startup, second, new(0, 1), DiagnosticCode.MissingStartupBody_Kd, null, null, null, null, null, null);
        Assert.Equal(DiagnosticFault.UndefinedOrder, Assert.Throws<DiagnosticContractException>(() => owner.Finalize()).Fault);
    }
}
