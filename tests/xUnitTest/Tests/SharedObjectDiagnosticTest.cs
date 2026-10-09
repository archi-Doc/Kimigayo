// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class SharedObjectDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("rc", "@objuniq")]
    [InlineData("arc", "@objuniq")]
    [InlineData("rc", "@objuniq/i32")]
    [InlineData("arc", "@objuniq/i32")]
    public void ExclusiveViewsExplainTheSharedPayloadAuthority(string mode, string operation)
    {
        var text = $"func run(value: {mode}/i32)\n    let view = value{operation}";
        var result = Report(text);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("SharedPathAccess_Kd", error.Code);
        Assert.Equal("value", text.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains("shared payload access only", error.Note, StringComparison.Ordinal);
        Assert.Contains("strong count of one", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        this.CheckOutputs(result);
    }

    // SPEC 13.5.5.1, 13.5.8: a counted payload grants Read only, even from a writable handle slot at strong count one.
    [Theory]
    [InlineData("let u = owner@follow@uniq", "owner@follow@uniq")]
    [InlineData("let taken = owner@follow@move", "owner@follow@move")]
    [InlineData("owner@follow = Item.init(2)", "owner@follow")]
    [InlineData("owner.value = 9", "owner.value")]
    [InlineData("owner.value += 1", "owner.value")]
    [InlineData("owner@follow.value = 9", "owner@follow.value")]
    [InlineData("let old = Kimi.Intrinsics.replace(owner@follow@uniq, with: Item.init(2))", "owner@follow@uniq")]
    [InlineData("var other = Item.init(2)\nKimi.Intrinsics.exchange(owner@follow@uniq, other@uniq)", "owner@follow@uniq")]
    public void CountedPayloadsGrantSharedAccessOnly(string operation, string target)
    {
        foreach (var factory in new[] { "makeRc", "makeArc" })
        {
            var text = $"struct Item\n    public var value: i32\n    public init(value: i32) => self.value = value\nvar owner = Kimi.Intrinsics.{factory}(Item.init(1))\n{operation}";
            var error = Assert.Single(Report(text).Diagnostics);
            Assert.Equal("SharedPathAccess_Kd", error.Code);
            Assert.Equal(target, text.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        }
    }

    [Theory]
    [InlineData("rc")]
    [InlineData("arc")]
    public void ExclusiveReceiversRetainTheirRequiredType(string mode)
    {
        var text = $"struct Item\n    public func edit(self: uniq/Self) => ()\nfunc run(value: {mode}/Item) => value.edit()";
        var result = Report(text);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Equal("value.edit()", text.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains(mode + "/Item", error.Note, StringComparison.Ordinal);
        Assert.Contains("uniq/Item", error.Note, StringComparison.Ordinal);
        Assert.Contains("shared payload access only", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Advice);
        this.CheckOutputs(result);
    }

    [Fact]
    public void CloningObjExplainsTheCountedHandleContract()
    {
        var result = Report("func run(value: obj/i32) => Kimi.Intrinsics.clone(value@ref)");
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Contains("rc or arc", error.Advice, StringComparison.Ordinal);
        Assert.Contains("obj ownership cannot be duplicated", error.Advice, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        this.CheckOutputs(result);
    }

    [Fact]
    public void UserCloneNamesDoNotAcquireIntrinsicAdvice()
    {
        var error = Assert.Single(Report("func clone(value: ref/i32) => ()\nfunc run(value: obj/i32) => clone(value@ref)").Diagnostics);
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Null(error.Advice);
    }

    [Fact]
    public void AuthorityFactsSurviveLongTypeNames()
    {
        var name = "Payload" + new string('x', 180);
        var error = Assert.Single(Report($"struct {name}\n    public func edit(self: uniq/Self) => ()\nfunc run(value: arc/{name}) => value.edit()").Diagnostics);
        Assert.Contains("arc/", error.Note, StringComparison.Ordinal);
        Assert.Contains("uniq/", error.Note, StringComparison.Ordinal);
        Assert.Contains("shared payload access only", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(7)\nlet view = owner@objuniq")]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(7)\nlet slot = owner@uniq")]
    [InlineData("var owner = Kimi.Intrinsics.makeArc(7)\nlet view = owner@objref")]
    public void PayloadAuthorityDoesNotRestrictTheHandleSlot(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Fact]
    public void UnresolvedInputsAndIndependentErrorsKeepTheirOwnCauses()
    {
        var missing = Assert.Single(Report("Kimi.Intrinsics.clone(missing)").Diagnostics);
        Assert.Equal("UnresolvedBinding_Kd", missing.Code);
        var first = Assert.Single(Report("func run(value: arc/i32) => value@objuniq").Diagnostics);
        var extended = Report("\nfunc run(value: arc/i32) => value@objuniq\nlet wrong: i32 = true");
        Assert.Equal(2, extended.Diagnostics.Length);
        var same = Assert.Single(extended.Diagnostics, x => x.Code == first.Code);
        Assert.Equal(first.Note, same.Note);
        Assert.Equal(first.Span!.Value.Start + 1, same.Span!.Value.Start);
    }

    private static DiagnosticResult Report(string text)
    {
        var path = Path.GetFullPath("shared-object-diagnostic.kimi");
        var c = MinimalEmissionTest.Analyze(text, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize());
        return result;
    }

    private void CheckOutputs(DiagnosticResult result)
    {
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(error.Note ?? error.Advice!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(Path.GetFullPath("shared-object-diagnostic.kimi"));
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Note ?? error.Advice!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
