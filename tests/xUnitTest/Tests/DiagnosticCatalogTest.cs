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

              + Name="IdentifierExpected_Kd"
                Message="No category"

              + Name="InvalidCharacter_Kd"
                Category="Language"
                Message="Broken {0"

              + Name="InvalidIdentifier_Kd"
                Category="Language"
                Message="Three {0} {1} {2}"

              + Name="IncompleteSyntax_Kd"
                Category="Language"
                Message="Literal {{brace}}"

              + Name="MissingComma_Kd"
                Category="Language"
                Message=""
            """;
        var (table, anomalies) = DiagnosticEntries.Load(Encoding.UTF8.GetBytes(text));

        Assert.Contains(anomalies, static x => x == "TypeMismatch_Kd: the entry is duplicated.");
        Assert.Contains(anomalies, static x => x == "NoSuch_Kd: no DiagnosticCode has this name.");
        Assert.Contains(anomalies, static x => x == "Count: no DiagnosticCode has this name.");
        Assert.Contains(anomalies, static x => x == "IdentifierExpected_Kd: the entry has no category.");
        Assert.Contains(anomalies, static x => x.StartsWith("InvalidCharacter_Kd: the message is not a valid template", StringComparison.Ordinal));
        Assert.Contains(anomalies, static x => x == "InvalidIdentifier_Kd: the message takes more than two arguments.");
        Assert.Contains(anomalies, static x => x == "IncompleteSyntax_Kd: a message without arguments contains a brace.");
        Assert.Contains(anomalies, static x => x == "MissingComma_Kd: the entry has no message.");
        Assert.Contains(anomalies, static x => x == "UnresolvedBinding_Kd: the code has no catalog entry.");
        Assert.Equal("First", table[(int)DiagnosticCode.TypeMismatch_Kd]!.Message);
        Assert.Null(table[(int)DiagnosticCode.IdentifierExpected_Kd]);
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
        Assert.True(DiagnosticEntries.TryGet(DiagnosticCode.IdentifierExpected_Kd, out entry));
        Assert.Equal(0, entry.Arity);
        Assert.Equal(entry.Message, entry.FormatMessage(null, null));
    }

    [Theory]
    [InlineData(DiagnosticCode.IdentifierExpected_Kd, "x", null, DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.InvalidCharacter_Kd, null, null, DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.UnsupportedLanguageVersion_Kd, "1", null, DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.UnsupportedLanguageVersion_Kd, null, "1", DiagnosticFault.InvalidArgument)]
    [InlineData(DiagnosticCode.Count, null, null, DiagnosticFault.UnknownCode)]
    [InlineData(DiagnosticCode.Template_Kd, null, null, DiagnosticFault.UnknownCode)]
    public void AReportMustMatchItsDefinition(DiagnosticCode code, string? first, string? second, DiagnosticFault fault)
    {
        var compilation = Compilation.CreateForTest();
        var collection = compilation.Kimigayo.GetOrAddDiagnosticCollection("main.kimi");
        var exception = Assert.Throws<DiagnosticContractException>(() => collection.Add(default, code, first, second));
        Assert.Equal(fault, exception.Fault);
        Assert.Empty(collection.GetArray());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void ALocationMustLieInItsSource(int start, int length)
    {
        var compilation = Compilation.CreateForTest();
        var collection = compilation.Kimigayo.GetOrAddDiagnosticCollection("main.kimi");
        var document = new SourceDocument("main.kimi", "abc");
        var exception = Assert.Throws<DiagnosticContractException>(() => collection.Add(new(start, length), DiagnosticCode.IdentifierExpected_Kd, sourceDocument: document));
        Assert.Equal(DiagnosticFault.InvalidLocation, exception.Fault);

        exception = Assert.Throws<DiagnosticContractException>(() => collection.Add(new(0, 1), DiagnosticCode.ProjectLoadFailed_Kd, "failure", location: "main.kimiproj"));
        Assert.Equal(DiagnosticFault.InvalidLocation, exception.Fault);
    }

    [Fact]
    public void AnInsertionPointAtTheEndIsValid()
    {
        var compilation = Compilation.CreateForTest();
        var collection = compilation.Kimigayo.GetOrAddDiagnosticCollection("main.kimi");
        collection.Add(new(3, 0), DiagnosticCode.IdentifierExpected_Kd, sourceDocument: new SourceDocument("main.kimi", "abc"));
        Assert.Single(collection.GetArray());
    }
}
