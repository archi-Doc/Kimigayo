// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 9.6.1, 10.5, 23.3.6.1: a container named without its Type arguments is an invalid Type formation with its counts, and a
// reference form that is not yet implemented is one located Unsupported record, also when a later use reads its binding.
public class ReferenceDiagnosticTest
{
    private const string Box = "struct Box<T>\n    public var value: T\n\n    public init(value: T) => self.value = value@move\n\n    public func size(self: ref/Self) -> isize => 1\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n    public func peek(self: ref/Self) -> i32 => self.item@follow\n";

    [Theory]
    [InlineData(Box + "let s = Box.size", "Box", "never inferred", "Box<...>.size")]
    [InlineData(Box + "let b = Box.init(4)", "Box", "never inferred", "Box<...>.init")]
    [InlineData(Box + "let b: Box = Box<i32>.init(4)", "Box", "declares 1 Type parameter, and 0 Type arguments are written", "one Type argument for each of T")]
    [InlineData(Box + "let b: Box<i32, bool> = Box<i32>.init(4)", "Box<i32, bool>", "declares 1 Type parameter, and 2 Type arguments are written", "one Type argument for each of T")]
    public void AContainerWithoutItsTypeArgumentsCannotFormAType(string source, string text, string note, string advice)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidTypeFormation_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Contains(note, error.Note, StringComparison.Ordinal);
        Assert.Contains(advice, error.Advice, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(note, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData("func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    let c: (ref/T, ref/T) -> i32 = T.compare\n    return c(a, b)", "T.compare")]
    [InlineData("func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    let c = T.compare\n    return c(a, b)", "T.compare")]
    [InlineData("func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    let c = T.compare\n    return 0", "T.compare")]
    [InlineData(Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet p = Holder.peek\nrequire p(h@ref) == 3 else => $abort(\"p\")", "Holder.peek")]
    [InlineData("let b = Raw.allocate<u8>\nlet p = b(4)", "Raw.allocate<u8>")]
    [InlineData("let a: (isize) -> raw/u8 = Raw.allocate<u8>", "Raw.allocate<u8>")]
    public void AnUnimplementedReferenceIsOneLocatedUnsupportedRecord(string source, string text)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.UnsupportedBinding_Kd), error.Code);
        Assert.Equal(text, error.Text);
    }

    [Fact]
    public void ACallThroughTheRequirementStaysSupported()
    {
        const string Source = "func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    return T.compare(a, b)\nlet x = 1\nlet y = 2\nrequire order(x@ref, y@ref) < 0 else => $abort(\"order\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }
}
