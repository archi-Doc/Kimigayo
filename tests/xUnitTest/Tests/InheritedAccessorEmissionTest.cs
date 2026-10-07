// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class InheritedAccessorEmissionTest
{
    private const string Source = """
        open struct Base<T>
            public var item: i32 = 42
            public computed number: i32
                get(self: ref/Self) -> i32 => self.item
            public computed view: ref/i32
                get(self: ref/Self) -> ref/i32 => self.item@ref
            public let stored: i32 = 2
                get(self: ref/Self) -> i32 => storage + self.item
            public computed fresh: string
                get(self: ref/Self) -> string => "fresh"
        struct Leaf: Base<i64>
            public init() => ()

        """;

    [Theory]
    [InlineData("Owner", "let x = Leaf.init()\nrequire x.number == 42 else => $abort(\"getter\")")]
    [InlineData("Reference", "func read(x: ref/Leaf) -> i32 => x.number\nlet x = Leaf.init()\nrequire read(x@ref) == 42 else => $abort(\"getter\")")]
    [InlineData("Result", "let x = Leaf.init()\nlet r = x.view\nrequire r == 42 else => $abort(\"getter\")")]
    [InlineData("Stored", "let x = Leaf.init()\nrequire x.stored == 44 else => $abort(\"getter\")")]
    [InlineData("OwnedResult", "let x = Leaf.init()\nConsole.writeLine(x.fresh)", "fresh\n")]
    [InlineData("Temporary", "require Leaf.init().number == 42 else => $abort(\"getter\")")]
    [InlineData("Nested", "let x = (Leaf.init(), 0)\nrequire x.0.number == 42 else => $abort(\"getter\")")]
    [InlineData("Generic", "struct Other<U>: Base<U>\n    public init() => ()\nfunc read<V>(x: ref/Other<V>) -> i32 => x.number\nlet x = Other<bool>.init()\nrequire read(x@ref) == 42 else => $abort(\"getter\")")]
    public void SharedInheritedGettersUseTheOrdinaryCallPipeline(string name, string body, string stdout = "")
        => ScalarEmissionTest.EmitFixture("InheritedAccessor" + name, Source + body, stdout);

    [Theory]
    [InlineData("var x = Leaf.init()\nlet r = x.view\nx.item = 0\n_ = r")]
    [InlineData("let r = Leaf.init().view\n_ = r")]
    public void GetterResultsRetainTheirSourceLoan(string body)
        => Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(DiagnosticCorpus.Check(Source + body).Diagnostics).Code);

    [Fact]
    public void ASharedGetterDoesNotProveTheSeparateExclusiveSetter()
    {
        const string Code = "open struct Base\n    public computed number: i32\n        get(self: ref/Self) -> i32 => 42\n        set(self: uniq/Self, value: i32) -> () => ()\nstruct Leaf: Base\n    public init() => ()\nvar x = Leaf.init()\nx.number = 3";
        var result = DiagnosticCorpus.Check(Code);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("UnsupportedBinding_Kd", record.Code);
        Assert.Equal("x.number", Code.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new(result.Diagnostics, result.Sources), string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(result.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(result, [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    [Fact]
    public void ObjectGetterExecutionKeepsItsLocatedUnsupportedBoundary()
        => Assert.Equal("UnsupportedBinding_Kd", Assert.Single(DiagnosticCorpus.Check(Source + "let x = Kimi.Intrinsics.makeObj(Leaf.init())\n_ = x.number").Diagnostics).Code);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SharedGetterPlansAreReused()
    {
        var c = MinimalEmissionTest.Analyze(Source + "let x = Leaf.init()\n_ = x.number\nlet r = x.view\n_ = r");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}
