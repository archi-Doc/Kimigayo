// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ReceiverDefaultTest
{
    private const string Meter = "struct Meter\n    public var raw: i32 = 4\n    public func read(self) -> i32 => self.raw\n    public computed doubled: i32\n        get() -> i32 => self.raw * 2\n";
    private const string Nested = Meter + "func leaf(m: ref/Meter, value: i32 = m.read()) -> i32 => value\nfunc f(a: ref/Meter, b: ref/Meter, value: i32 = leaf(a) + leaf(b)) -> i32 => value\nvar a = Meter.init()\nvar b = Meter.init()\nb.raw = 7\nrequire f(a@ref, b@ref) == 11 and f(b@ref, a@ref) == 11 else => $abort(\"nested\")";

    [Theory]
    [InlineData("OwnedMethod", Meter + "func f(m: Meter, value: i32 = m.read()) -> i32 => value\nrequire f(Meter.init()) == 4 else => $abort(\"method\")")]
    [InlineData("SharedMethod", Meter + "func f(m: ref/Meter, value: i32 = m.read()) -> i32 => value\nlet m = Meter.init()\nrequire f(m@ref) == 4 else => $abort(\"shared method\")")]
    [InlineData("ReservedMethod", Meter + "func f(m: uniq/Meter, value: i32 = m.read()) -> i32\n    m.raw = value + 1\n    return value\nvar m = Meter.init()\nrequire f(m@uniq) == 4 and m.raw == 5 else => $abort(\"reserved method\")")]
    [InlineData("OwnedGetter", Meter + "func f(m: Meter, value: i32 = m.doubled) -> i32 => value\nrequire f(Meter.init()) == 8 else => $abort(\"getter\")")]
    [InlineData("ReservedGetter", Meter + "func f(m: uniq/Meter, value: i32 = m.doubled) -> i32\n    m.raw = value\n    return value\nvar m = Meter.init()\nrequire f(m@uniq) == 8 and m.raw == 8 else => $abort(\"reserved getter\")")]
    [InlineData("SharedGetter", Meter + "func f(m: ref/Meter, value: i32 = m.doubled) -> i32 => value\nlet m = Meter.init()\nrequire f(m@ref) == 8 else => $abort(\"shared getter\")")]
    [InlineData("NestedOrigins", Nested)]
    [InlineData("FunctionValue", "func five() -> i32 => 5\nfunc f(action: () -> i32, value: i32 = action()) -> i32 => value\nrequire f(five) == 5 else => $abort(\"function\")")]
    [InlineData("FunctionLocal", "func five() -> i32 => 5\nfunc f(value: i32 = label work: do\n    let action: () -> i32 = five\n    exit to work action()\n) -> i32 => value\nrequire f() == 5 else => $abort(\"local function\")")]
    [InlineData("FunctionNestedDefault", "func five() -> i32 => 5\nfunc f(action: () -> i32 = five, value: i32 = action()) -> i32 => value\nrequire f() == 5 else => $abort(\"function default\")")]
    [InlineData("IndependentFunctionArgument", "func take(x: string) -> i32 => 5\nfunc f(action: (string) -> i32, value: i32 = action(\"independent\")) -> i32 => value\nrequire f(take) == 5 else => $abort(\"argument\")")]
    public void SharedCallsInspectPreparedSlots(string name, string source)
        => ScalarEmissionTest.EmitFixture("ReceiverDefault" + name, source, string.Empty);

    [Theory]
    [InlineData("")]
    [InlineData("\nf(take, \"prepared\", 5)")]
    [InlineData("\nf(take, \"prepared\")")]
    public void ValueCallCannotConsumeAPreparedArgument(string use)
    {
        const string Declaration = "func take(x: string) -> i32 => 5\nfunc f(action: (string) -> i32, x: string, value: i32 = action(x@move)) -> i32 => value";
        var source = Declaration + (use.Length == 0 ? "\npublic func main() => ()" : use);
        var issue = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.DefaultArgumentMove_Kd), issue.Code);
        Assert.Equal("x", source.Substring(issue.Span!.Value.Start, issue.Span.Value.Length));
        Assert.Contains("prepared argument", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreparedMoveDiagnosticReachesTheCliAndServer()
    {
        const string Source = "func f(action: (string) -> i32, x: string, value: i32 = action(x@move)) -> i32 => value\npublic func main() => ()";
        var path = Path.GetFullPath("receiver-default.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.DefaultArgumentMove_Kd), record.Code);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
            Assert.Contains(record.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InspectionsEndBeforeTheCalleeAndCleanup()
    {
        const string Source = "struct Box\n    public let text: string\n    public init(text: string) => self.text = text@move\n    public func inspect(self) -> i32\n        Console.writeLine(self.text)\n        return 4\nfunc f(box: Box, value: i32 = box.inspect()) => Console.writeLine(\"callee\")\nf(Box.init(\"prepared\"))";
        var ir = ScalarEmissionTest.EmitFixture("ReceiverDefaultCleanup", Source, "prepared\ncallee\n");
        StringEmissionTest.WriteAuditedFixture("ReceiverDefaultCleanup", Source, ir, "prepared\ncallee\n", "prepared=1;callee=1", order: [1, 0]);
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void WarmNestedReceiverDefaultsAllocateNothing()
        {
            var c = MinimalEmissionTest.Analyze(Nested);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
