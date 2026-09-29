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
        c.Kotonoha.DiagnosticCollection.Add(DiagnosticPartition.Binding, new(0, 5), DiagnosticCode.UnresolvedBinding_Kd, sourceDocument: document);
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
}
