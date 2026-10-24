// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public sealed partial class DiagnosticOwnerTest
{
    [Fact]
    public void SyntaxTransferRemapsDistinctSnapshotsAndTheirEvidenceLocations()
    {
        var source = new DiagnosticOwner();
        var donor = source.GetOrAddCollection("donor");
        var oldDocument = new SourceDocument("same.kimi", "old snapshot");
        var newDocument = new SourceDocument("same.kimi", "new snapshot");
        var relatedDocument = new SourceDocument("related.kimi", "before\nreplace");
        var editedDocument = new SourceDocument("repair.kimi", "line\nreplace");
        var oldTarget = donor.For(oldDocument);
        var newTarget = donor.For(newDocument);
        donor.Register(relatedDocument);
        donor.Register(editedDocument);
        oldTarget.AddSyntax(
            new(0, 3),
            DiagnosticCode.InvalidCharacter_Kd,
            "old",
            related: [donor.Relate("declaration", new(7, 7), relatedDocument, "original"), donor.Relate("declaration", new(5, 7), editedDocument, "original")],
            repairs:
            [new(
                RepairKind.ReplaceToken,
                ["replace", "fixed"],
                [donor.Edit(new(7, 7), "fixed", relatedDocument), donor.Edit(new(5, 7), "fixed", editedDocument)],
                RepairConditionSet.None),
            new(RepairKind.ReplaceToken, ["old", "fixed"], [donor.Edit(new(0, 3), "fixed", oldDocument)], RepairConditionSet.None)]);
        var oldKey = oldTarget.LastError!.Value;
        newTarget.Add(new(0, 3), DiagnosticCode.InvalidCharacter_Kd, "new");
        var newKey = newTarget.LastError!.Value;
        donor.For(new SourceDocument("unrelated.kimi", "unused")).Add(new(0, 6), DiagnosticCode.InvalidCharacter_Kd, "unused");
        var original = source.Finalize();

        var destination = new DiagnosticOwner();
        var recipient = destination.GetOrAddCollection("recipient");
        recipient.Register(editedDocument);
        recipient.For(new SourceDocument("same.kimi", "local snapshot")).Add(new(0, 5), DiagnosticCode.InvalidCharacter_Kd, "local");
        var transfer = new DiagnosticOwner.SyntaxTransfer(source, recipient);
        transfer.Include(newKey);
        transfer.Include(oldKey);
        transfer.Copy();

        Assert.Equal(2, transfer.Key(oldKey).Source);
        Assert.Equal(3, transfer.Key(newKey).Source);
        var result = destination.Finalize();
        Assert.Equal(["repair.kimi", "same.kimi", "same.kimi", "same.kimi", "related.kimi"], result.Sources.Select(item => item.Path));
        Assert.Equal([1, 2, 3], result.Diagnostics.Select(item => item.Source));
        Assert.Equal(
            ["local snapshot", "old snapshot", "new snapshot"],
            result.Diagnostics.Select(item => Assert.Single(item.Display!.Excerpt).Text));
        var imported = result.Diagnostics[1];
        Assert.Equal([0, 4], imported.Related!.Select(item => item.Source));
        var related = imported.Related![1];
        Assert.Equal(new SourceSpan(7, 7), related.Span);
        Assert.Equal(new SourceRange(new(1, 0), new(1, 7)), related.Range);
        var repairs = Assert.IsType<RepairCandidate[]>(imported.Repairs);
        Assert.Equal([0, 2], repairs.Select(item => item.Edits[0].Source));
        var edits = repairs[0].Edits;
        Assert.Equal([0, 4], edits.Select(item => item.Source));
        var edit = edits[0];
        Assert.Equal(0, edit.Source);
        Assert.Equal(new SourceSpan(5, 7), edit.Span);
        Assert.Equal(new SourceRange(new(1, 0), new(1, 7)), edit.Range);
        Assert.Equal("fixed", edit.Text);
        Assert.Equal("replace", edit.Replaced);
        Assert.Equal("old", Assert.Single(repairs[1].Edits).Replaced);
        Assert.Equal(original, source.Finalize());
    }

    [Fact]
    public void SyntaxTransferCopiesTheCauseClosureAndPreservesUnresolvedPrerequisites()
    {
        var source = new DiagnosticOwner();
        var document = new SourceDocument("donor.kimi", "a b c d e f");
        var donor = source.GetOrAddCollection("donor").For(document);
        donor.Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "a");
        var cause = donor.LastError!.Value;
        donor.AddDependentSyntax(new(2, 1), "middle", cause, document);
        var middle = donor.LastError!.Value;
        donor.AddDependentSyntax(new(4, 1), "outer", middle, document);
        var outer = donor.LastError!.Value;
        donor.AddDependentSyntax(new(6, 1), "unknown", DiagnosticKey.Unresolved, document);
        var unresolved = donor.LastError!.Value;
        donor.Add(new(8, 1), DiagnosticCode.InvalidCharacter_Kd, "e");
        donor.ReportLocation(DiagnosticRequirement.Emission, new(10, 1), DiagnosticCode.Unsupported_Kd, document);
        var destination = new DiagnosticOwner();
        var recipient = destination.GetOrAddCollection("recipient");
        recipient.For(new SourceDocument("recipient.kimi", "r")).Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "r");
        var transfer = new DiagnosticOwner.SyntaxTransfer(source, recipient);
        transfer.Include(outer);
        transfer.Include(unresolved);
        transfer.Copy();

        var imported = recipient.For(document);
        Assert.True(imported.RecallError(new(2, 1)));
        Assert.Equal(transfer.Key(middle), imported.LastError);
        Assert.True(imported.RecallError(new(4, 1)));
        Assert.Equal(transfer.Key(outer), imported.LastError);
        Assert.False(imported.RecallError(new(8, 1)));
        Assert.Equal(DiagnosticKey.Unresolved, transfer.Key(DiagnosticKey.Unresolved));
        var result = destination.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission);
        Assert.Equal(["InvalidCharacter_Kd", "InvalidCharacter_Kd", "PrerequisiteUnavailable_Kd"], result.Diagnostics.Select(item => item.Code));
        Assert.Equal([0, 1, 1], result.Diagnostics.Select(item => item.Source));
        Assert.Equal(new SourceSpan(6, 1), result.Diagnostics[2].Span);
        Assert.Null(result.Diagnostics[2].Related);
        Assert.False(destination.HasErrorsIn(DiagnosticPartition.Emission));
    }

    [Fact]
    public void SyntaxTransferRejectsAKnownCauseWithoutRecordedSyntax()
    {
        var source = new DiagnosticOwner();
        var document = new SourceDocument("donor.kimi", "a");
        var donor = source.GetOrAddCollection("donor").For(document);
        var key = donor.KeyOf(document, new(0, 1), document, DiagnosticRequirement.Syntax);
        var destination = new DiagnosticOwner();
        var transfer = new DiagnosticOwner.SyntaxTransfer(source, destination.GetOrAddCollection("recipient"));
        transfer.Include(DiagnosticKey.Unresolved);
        transfer.Copy();
        Assert.Equal(DiagnosticKey.Unresolved, transfer.Key(DiagnosticKey.Unresolved));
        var error = Assert.Throws<DiagnosticContractException>(() => transfer.Include(key));
        Assert.Equal(DiagnosticFault.Collection, error.Fault);
        Assert.Empty(destination.Finalize().Diagnostics);
    }
}
