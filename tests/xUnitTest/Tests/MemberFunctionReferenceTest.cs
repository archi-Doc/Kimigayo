// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.3: a Type-qualified instance function reference is unbound; its self is an ordinary parameter of the Item's
// signature, and value.method without invocation forms no bound-method value.
public class MemberFunctionReferenceTest
{
    private const string Counter = "struct Counter\n    var value: i32\n\n    public init(value: i32)\n        self.value = value\n\n" +
        "    public func peek(self) -> i32 => self.value + 1\n    public func add(self: uniq/Self, amount: i32) -> () => self.value += amount\n" +
        "    public func twice<T>(self, other: T) -> (i32, T) => (self.value, other@move)\n" +
        "var counter = Counter.init(value: 1)\n";

    private const string Apply = "func apply<F>(action: ref/F, counter: ref/Counter) -> i32\n    F is Callable<(ref/Counter) -> i32>\n    return action(counter)\n";

    [Theory]
    [InlineData("Item", Counter + "let peek = Counter.peek\nrequire peek(counter@ref) == 2 else => $abort(\"item\")")]
    [InlineData("Erased", Counter + "let erased: (ref/Counter) -> i32 = Counter.peek\nrequire erased(counter@ref) == 2 else => $abort(\"erased\")")]
    [InlineData("Exclusive", Counter + "let add: (uniq/Counter, i32) -> () = Counter.add\nadd(counter@uniq, 5)\nrequire counter.peek() == 7 else => $abort(\"exclusive\")")]
    [InlineData("Callable", Apply + Counter + "require apply(Counter.peek, counter@ref) == 2 else => $abort(\"callable\")")]
    [InlineData("GenericMember", Counter + "let pair = Counter.twice<bool>\nlet p = pair(counter@ref, true)\nrequire p.0 == 1 and p.1 else => $abort(\"generic\")")]
    [InlineData("QualifiedErasure", "group Tools\n    public func inc(value: i32) -> i32 => value + 1\nlet f: (i32) -> i32 = Tools.inc\nrequire f(1) == 2 else => $abort(\"qualified\")")]
    public void TypeQualifiedReferencesTakeTheReceiverAsAnArgument(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("MemberReference" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("let p = counter.peek")]
    [InlineData("let p: (ref/Counter) -> i32 = counter.peek")]
    public void AMethodNamedThroughAValueIsNoValue(string body)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(Counter + body, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.BoundMethodValue_Kd), error.Code);
        Assert.Equal("counter.peek", error.Text);
        Assert.Contains("Counter.peek", error.Advice, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("'peek' is named through a value", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Fact]
    public void MembersOfGenericContainersRemainUnsupported()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    var item: T\n\n    public func get(self) -> ref/T during self => self.item@ref\nlet g = Box<i32>.get");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
    }
}
