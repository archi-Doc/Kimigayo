// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 9.3: protected, protected internal and private protected apply only to members of a struct, including Containers
// declared directly in it, and to their accessors; they are invalid on root and group declarations, group members and
// Contract requirements. Binding reports each misplaced form once at the declaration's signature, naming what declares it;
// a Contract requirement takes no modifier at all, which the parser reports alone.
public sealed class ProtectedPlacementTest(ITestOutputHelper output)
{
    [Fact]
    public void AModifierOnAContractRequirementIsReportedByTheParserAlone()
    {
        var record = Assert.Single(DiagnosticCorpus.Check("contract C\n    protected func f(self: ref/Self) -> i32\npublic func main() => ()\n").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), record.Code);
    }

    [Theory]
    [InlineData("protected func f() -> i32 => 1\n", "the source root", "f() -> i32")]
    [InlineData("protected internal func f() -> i32 => 1\n", "the source root", "f() -> i32")]
    [InlineData("protected struct S\n", "the source root", null)]
    [InlineData("group G\n    protected func f() => ()\n", "group G", "f()")]
    [InlineData("group G\n    private protected struct Hidden\n", "group G", null)]
    [InlineData("enum E\n    A\n    protected func f(self: ref/Self) -> i32 => 1\n", "enum E", "f(self: ref/Self) -> i32")]
    [InlineData("func g()\n    protected func h() => ()\n    h()\n", "a function body", "h()")]
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
