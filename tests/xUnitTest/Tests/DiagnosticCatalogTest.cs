// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 23.3.6.1 and 23.3.6.7: every code has one catalog entry with a severity and a category, and a
// malformed catalog or report is a contract violation, never silently repaired.
public sealed class DiagnosticCatalogTest
{
    [Fact]
    public void TheCatalogDefinesEveryCodeOnce()
    {
        Assert.Empty(DiagnosticEntries.Anomalies);
        for (var code = (DiagnosticCode)0; code < DiagnosticCode.Count; code++)
        {
            Assert.True(DiagnosticEntries.TryGet(code, out var entry), code.ToString());
            Assert.Equal(code.ToString(), entry.Name);
            Assert.True(Enum.IsDefined(entry.Category), code.ToString());
            Assert.True(entry.Arity <= 2, code.ToString());
        }
    }

    [Fact]
    public void LoadingReportsEveryAnomaly()
    {
        var text = """
              + Name="TypeMismatch_Kd"
                Category="Language"
                Message="First"

              + Name="TypeMismatch_Kd"
                Category="Language"
                Message="Second"

              + Name="NoSuch_Kd"
                Category="Language"
                Message="Unknown"

              + Name="Count"
                Category="Language"
                Message="Sentinel"

              + Name="IndentationLevelMismatch_Kd"
                Message="No category"

              + Name="InvalidCharacter_Kd"
                Category="Language"
                Message="Broken {0"

              + Name="InvalidIdentifier_Kd"
                Category="Language"
                Message="Three {0} {1} {2}"

              + Name="TopLevelKeywordAfterCode_Kd"
                Category="Language"
                Message="Literal {{brace}}"

              + Name="CodeAfterMultilineComment_Kd"
                Category="Language"
                Message=""

              + Name="SemicolonNotAllowed_Kd"
                Category="Language"
                Message="Token {0}"

              + Name="UnsupportedEscape_Kd"
                Category="Language"
                Message="Token {0}"
                Arguments="token"

              + Name="InvalidAssignment_Kd"
                Category="Language"
                Message="No arguments"
                Evidence="actual:Type"
                Label="found {1}"
            """;
        var (table, anomalies) = DiagnosticEntries.Load(Encoding.UTF8.GetBytes(text));

        Assert.Contains(anomalies, static x => x == "TypeMismatch_Kd: the entry is duplicated.");
        Assert.Contains(anomalies, static x => x == "NoSuch_Kd: no DiagnosticCode has this name.");
        Assert.Contains(anomalies, static x => x == "Count: no DiagnosticCode has this name.");
        Assert.Contains(anomalies, static x => x == "IndentationLevelMismatch_Kd: the entry has no category.");
        Assert.Contains(anomalies, static x => x.StartsWith("InvalidCharacter_Kd: the message or label is not a valid template", StringComparison.Ordinal));
        Assert.Contains(anomalies, static x => x == "SemicolonNotAllowed_Kd: Arguments must name each message argument (1), not 0.");
        Assert.Contains(anomalies, static x => x == "UnsupportedEscape_Kd: a fact is not written as name:Kind.");
        Assert.Contains(anomalies, static x => x == "InvalidAssignment_Kd: the label references a fact that the code does not name.");
        Assert.Contains(anomalies, static x => x == "InvalidIdentifier_Kd: the message takes more than two arguments.");
        Assert.Contains(anomalies, static x => x == "TopLevelKeywordAfterCode_Kd: a message without arguments contains a brace.");
        Assert.Contains(anomalies, static x => x == "CodeAfterMultilineComment_Kd: the entry has no message.");
        Assert.Contains(anomalies, static x => x == "UnresolvedBinding_Kd: the code has no catalog entry.");
        Assert.Equal("First", table[(int)DiagnosticCode.TypeMismatch_Kd]!.Message);
        Assert.Null(table[(int)DiagnosticCode.IndentationLevelMismatch_Kd]);
    }

    // SPEC 23.3.6.9: every repair kind has one catalog entry whose title, facts and conditions are consistent.
    [Fact]
    public void TheRepairCatalogDefinesEveryKindOnce()
    {
        Assert.Empty(RepairKinds.Anomalies);
        for (var kind = (RepairKind)0; kind < RepairKind.Count; kind++)
        {
            Assert.True(RepairKinds.TryGet(kind, out var entry), kind.ToString());
            Assert.Equal(RepairKinds.NameOf(kind), entry.Name);
        }

        Assert.True(RepairKinds.TryGet(RepairKind.Transfer, out var transfer));
        Assert.Equal(RepairConditionSet.Take | RepairConditionSet.UsageLegality, transfer.Relevant);
        Assert.Equal("Append @move to transfer x to f(a: T)", transfer.FormatTitle(["x", "f(a: T)"]));
        Assert.Equal("x offers Take and is a Movable Place", RepairConditions.Phrase(RepairCondition.Take, ["x"]));
    }

    [Fact]
    public void LoadingTheRepairCatalogReportsEveryAnomaly()
    {
        var text = """
              + Name="Repair.Transfer"
                Title="First {0} {1}"
                Facts="place:Text,target:Text"
                Conditions="Take,UsageLegality"

              + Name="Repair.Transfer"
                Title="Second"

              + Name="Repair.NoSuch"
                Title="Unknown"

              + Name="Repair.Borrow"
                Title="Borrow {0}"
                Facts="place"

              + Name="Repair.BorrowExclusively"
                Title="Borrow {2}"
                Facts="place:Text,spelling:Text"

              + Name="Repair.RemoveUnsafe"
                Title="Remove"
                Conditions="Scope"

              + Name="Repair.ReplaceToken"
                Title="Replace"
                Conditions="Take"

              + Name="Repair.InsertToken"
                Title=""
            """;
        var (table, anomalies) = RepairKinds.Load(Encoding.UTF8.GetBytes(text));
        Assert.Contains(anomalies, static x => x == "Repair.Transfer: the entry is duplicated.");
        Assert.Contains(anomalies, static x => x == "Repair.NoSuch: no RepairKind has this name.");
        Assert.Contains(anomalies, static x => x == "Repair.Borrow: a fact is not written as name:Kind.");
        Assert.Contains(anomalies, static x => x == "Repair.BorrowExclusively: the title references a fact that the kind does not name.");
        Assert.Contains(anomalies, static x => x == "Repair.RemoveUnsafe: Scope is not a repair condition.");
        Assert.Contains(anomalies, static x => x == "Repair.ReplaceToken: the phrase of Take references a fact that the kind does not name.");
        Assert.Contains(anomalies, static x => x == "Repair.InsertToken: the entry has no title.");
        Assert.Contains(anomalies, static x => x == "Repair.PropagateFailure: the kind has no catalog entry.");
        Assert.Equal("First {0} {1}", table[(int)RepairKind.Transfer]!.Title);
        Assert.Null(table[(int)RepairKind.Borrow]);
        Assert.Equal(["The repair catalog resource is missing."], RepairKinds.Load(null).Anomalies);
    }

    [Fact]
    public void AMissingCatalogIsAnAnomaly()
    {
        var (table, anomalies) = DiagnosticEntries.Load(null);
        Assert.Equal(["The catalog resource is missing."], anomalies);
        Assert.All(table, static x => Assert.Null(x));
    }

    [Fact]
    public void MessagesFormatWithTheInvariantCulture()
    {
        Assert.True(DiagnosticEntries.TryGet(DiagnosticCode.UnsupportedLanguageVersion_Kd, out var entry));
        Assert.Equal(2, entry.Arity);
        Assert.Equal("Unsupported language version '1.5'; this compiler supports '0.0.2'", entry.FormatMessage(1.5, "0.0.2"));
        Assert.True(DiagnosticEntries.TryGet(DiagnosticCode.IndentationLevelMismatch_Kd, out entry));
        Assert.Equal(0, entry.Arity);
        Assert.Equal(entry.Message, entry.FormatMessage(null, null));
    }

    [Theory]
    [InlineData(DiagnosticCode.IndentationLevelMismatch_Kd, "x", null, DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.InvalidCharacter_Kd, null, null, DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.UnsupportedLanguageVersion_Kd, "1", null, DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.UnsupportedLanguageVersion_Kd, null, "1", DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.Count, null, null, DiagnosticFault.UnknownCode)]
    [InlineData(DiagnosticCode.Template_Kd, null, null, DiagnosticFault.UnknownCode)]
    public void AReportMustMatchItsDefinition(DiagnosticCode code, string? first, string? second, DiagnosticFault fault)
    {
        var compilation = Compilation.CreateForTest();
        var collection = compilation.Diagnostics.GetOrAddCollection("main.kimi");
        var exception = Assert.Throws<DiagnosticContractException>(() => { collection.Add(default, code, first, second); });
        Assert.Equal(fault, exception.Fault);
        Assert.Empty(TestDiagnostics.Of(compilation));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void ALocationMustLieInItsSource(int start, int length)
    {
        var compilation = Compilation.CreateForTest();
        var collection = compilation.Diagnostics.GetOrAddCollection("main.kimi");
        var document = new SourceDocument("main.kimi", "abc");
        var exception = Assert.Throws<DiagnosticContractException>(() => { collection.Add(new(start, length), DiagnosticCode.IndentationLevelMismatch_Kd, sourceDocument: document); });
        Assert.Equal(DiagnosticFault.InvalidLocation, exception.Fault);

        exception = Assert.Throws<DiagnosticContractException>(() => { collection.Add(new(0, 1), DiagnosticCode.ProjectLoadFailed_Kd); });
        Assert.Equal(DiagnosticFault.InvalidLocation, exception.Fault);
    }

    [Fact]
    public void AnInsertionPointAtTheEndIsValid()
    {
        var compilation = Compilation.CreateForTest();
        var collection = compilation.Diagnostics.GetOrAddCollection("main.kimi");
        collection.Add(new(3, 0), DiagnosticCode.IndentationLevelMismatch_Kd, sourceDocument: new SourceDocument("main.kimi", "abc"));
        Assert.Single(TestDiagnostics.Of(compilation));
    }
}
