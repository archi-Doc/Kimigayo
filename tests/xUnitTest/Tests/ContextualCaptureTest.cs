// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.6.2: contextual self and a setter's value are never captured implicitly, and explicit captures of them obey the
// receiver, accessor, construction and destruction restrictions.
public class ContextualCaptureTest
{
    private const string Counter = "struct Counter\n    public var count: i32\n    public init(count: i32) => self.count = count\n\n";

    [Theory]
    [InlineData(Counter + "    public func peek(self: ref/Self) -> i32\n        let read = func () => self.count\n        return read()\n", "self")]
    [InlineData(Counter + "    public func bump(self: uniq/Self) -> ()\n        var step = func () => self.count += 1\n        step()\n", "self")]
    [InlineData(Counter + "    public func take(self) -> i32\n        let read = func () => self.count\n        return read()\n", "self")]
    [InlineData(Counter + "    public func reader(self: ref/Self) -> () -> i32\n        return func () => self.count\n", "self")]
    [InlineData(Counter + "    public func nested(self: ref/Self) -> i32\n        let outer = func () => (func () => self.count)()\n        return outer()\n", "self")]
    [InlineData("struct Box\n    public var item: i32\n        set(self: uniq/Self, value: i32) -> ()\n            let read = func () => value + 1\n            storage = read()\n    public init(item: i32) => self.item = item\n", "value")]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32) => self.id = id\n    drop\n        let show = func () => self.id\n        Console.writeLine(\"\\(show())\")\n", "self")]
    public void AnOmittedCaptureListNeverCapturesAContextualBinding(string declarations, string name)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(declarations, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.Equal(name, error.Text); // Located at the use.
        Assert.Contains("never captured implicitly", error.Note, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("never captured implicitly", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32)\n        self.id = id\n        let peek = func [self] () => self.id\n        _ = peek()\n")]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32)\n        self.id = id\n        let peek = func [self@move] () => self.id\n        _ = peek@move()\n")]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32) => self.id = id\n    drop\n        let show = func [self@ref] () => self.id\n        _ = show()\n")]
    public void AnExplicitCaptureOfSelfObeysConstructionAndDestruction(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.StartsWith("self", error.Text, StringComparison.Ordinal); // Located at the capture entry.
        Assert.Contains("reached only through its Fields", error.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitCapturesOfSelfAndAnOrdinaryValueRun()
    {
        const string Source = Counter + "    public func bump(self: uniq/Self) -> i32\n        var step = func [self] () => self.count += 1\n        step()\n        step()\n        return self.count\n\n" +
            "    public func peek(self: ref/Self) -> i32\n        let read = func [self] () => self.count\n        return read()\n\n" +
            "    public func outer(self: ref/Self) -> i32\n        let read = func [self] () => (func () => self.count)()\n        return read()\n\n" +
            "var c = Counter.init(1)\nConsole.writeLine(\"\\(c.bump())\")\nConsole.writeLine(\"\\(c.peek())\")\nConsole.writeLine(\"\\(c.outer())\")\nlet value = 4\nlet add = func () => value + 1\nConsole.writeLine(\"\\(add())\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ContextualCaptureExplicit", Source, "3\n3\n3\n5\n");
    }
}
