// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 9.4: protected, protected internal and private protected apply only to members of a struct, including Containers
// declared directly in it, and to their accessors; they are invalid on root and group declarations, group members and
// Contract requirements. Binding reports each misplaced form once at the declaration's signature, naming what declares it;
// a declaration that takes no access word at all (a Contract requirement; a root or local function, SPEC 6.1.1 and 9.3) is
// reported by the parser alone, at the word.
public sealed class ProtectedPlacementTest(ITestOutputHelper output)
{
    [Fact]
    public void AModifierOnAContractRequirementIsReportedByTheParserAlone()
    {
        var record = Assert.Single(DiagnosticCorpus.Check("contract C\n    protected func f(self: ref/Self) -> i32\npublic func main() => ()\n").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), record.Code);
    }

    [Theory]
    [InlineData("protected func f() -> i32 => 1\n")]
    [InlineData("protected internal func f() -> i32 => 1\n")]
    [InlineData("func g()\n    protected func h() => ()\n    h()\n")]
    public void AnAccessWordOnAFunctionThatTakesNoneIsReportedByTheParserAlone(string declarations)
    {
        var source = declarations + "public func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), record.Code);
        Assert.Equal(new SourceSpan(source.IndexOf("protected", StringComparison.Ordinal), "protected".Length), record.Span);
    }

    [Theory]
    [InlineData("protected struct S\n", "the source root", null)]
    [InlineData("group G\n    protected func f() => ()\n", "group G", "f()")]
    [InlineData("group G\n    private protected struct Hidden\n", "group G", null)]
    [InlineData("enum E\n    A\n    protected func f(self: ref/Self) -> i32 => 1\n", "enum E", "f(self: ref/Self) -> i32")]
    public void AProtectedFormOutsideAStructIsReportedOnce(string declarations, string container, string? at)
    {
        var source = declarations + "public func main() => ()\n";
        var result = DiagnosticCorpus.Check(source);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProtectedPlacement_Kd), record.Code);
        Assert.EndsWith("this declaration belongs to " + container, record.Message, StringComparison.Ordinal);
        if (at is not null)
        {
            var start = source.IndexOf(at, StringComparison.Ordinal);
            Assert.Equal(new SourceSpan(start, at.Length), record.Span);
        }
    }

    [Theory]
    [InlineData("open struct B\n    protected func f(self: ref/Self) -> i32 => 1\n    protected struct Inner\n    private protected func g() => ()\n    protected internal func h() => ()\n")]
    [InlineData("contract C\nstruct S<T>\n    var v: T\n    Self is C when T is Copy\n        protected func g(self: ref/Self) -> i32 => 1\n")]
    public void StructMembersMayBeProtected(string declarations)
        => Assert.Empty(DiagnosticCorpus.Check(declarations + "public func main() => ()\n").Diagnostics);

    // SPEC 9.4.1, 23.1.2 (R7-E2 E2-3a, M1): the placement is its own requirement, so each misplaced declaration keeps its record beside the
    // declaration's other failure (a duplicate, SPEC 6.1; a group function that is virtual, 6.4.1), which E2-2's head dropped. The twins fix
    // one cause: the other record stays alone.
    [Theory]
    [InlineData("group G\n    protected func f() => ()\n    protected func f() => ()\n", "DuplicateBinding_Kd ProtectedPlacement_Kd ProtectedPlacement_Kd")]
    [InlineData("group G\n    protected let x: i32 = 1\n    protected let x: i32 = 2\n", "DuplicateBinding_Kd ProtectedPlacement_Kd ProtectedPlacement_Kd")]
    [InlineData("group G\n    protected virtual func f() => ()\n", "InvalidVirtualDeclaration_Kd ProtectedPlacement_Kd")]
    [InlineData("group G\n    func f() => ()\n    func f() => ()\n", "DuplicateBinding_Kd")]
    [InlineData("group G\n    let x: i32 = 1\n    let x: i32 = 2\n", "DuplicateBinding_Kd")]
    [InlineData("group G\n    virtual func f() => ()\n", "InvalidVirtualDeclaration_Kd")]
    public void APlacementIsItsOwnRecordBesideTheDeclarationsOtherFailure(string declarations, string expected)
    {
        var result = DiagnosticCorpus.Check(declarations + "public func main() => ()\n");
        Assert.Equal(expected, string.Join(' ', result.Diagnostics.Select(static x => x.Code).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void CliAndLspCarryTheContainer()
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze("group Tools\n    protected func f() => ()", path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProtectedPlacement_Kd), error.Code);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("belongs to group Tools : ProtectedPlacement_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("protected outside a struct", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("InaccessibleBinding", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains("belongs to group Tools", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
