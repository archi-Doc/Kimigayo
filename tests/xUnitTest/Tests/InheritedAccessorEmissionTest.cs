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

    private const string Leveled = "open struct Leveled<T>\n    public var level: i32 = 1\n        get(self: ref/Self) -> i32 => storage\nstruct Leaf: Leveled<i64>\n    public init() => ()\n";

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

    // SPEC 13.7.2, 9.5.1: a compound update of an inherited Property with a custom `get` and a standard `set` reads through the getter
    // with the whole located receiver lent at its base prefix, as a getter call does (it failed generation with "Borrow source has no
    // matching aggregate storage" or "Reborrow has no matching reference source").
    [Theory]
    [InlineData("Compound", "var x = Leaf.init()\nx.level += 1\nConsole.writeLine(\"\\(x.level)\")", "2\n")]
    [InlineData("Increment", "var x = Leaf.init()\nx.level++\nConsole.writeLine(\"\\(x.level)\")", "2\n")]
    [InlineData("Exclusive", "func up(x: uniq/Leaf) -> ()\n    x.level += 1\nvar x = Leaf.init()\nup(x@uniq)\nConsole.writeLine(\"\\(x.level)\")", "2\n")]
    [InlineData("Mixed", "var x = Leaf.init()\nx.level = 5\nx.level += 1\nConsole.writeLine(\"\\(x.level)\")", "6\n")]
    [InlineData("Generic", "struct Other<U>: Leveled<U>\n    public init() => ()\nfunc up<V>(x: uniq/Other<V>) -> ()\n    x.level += 2\nvar x = Other<bool>.init()\nup(x@uniq)\nConsole.writeLine(\"\\(x.level)\")", "3\n")]
    public void CompoundUpdatesReadThroughAProjectedGetter(string name, string body, string stdout)
        => ScalarEmissionTest.EmitFixture("InheritedAccessorUpdate" + name, Leveled + body, stdout);

    [Fact]
    public void ACompoundUpdateKeepsTheReceiverExclusive()
        => Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(DiagnosticCorpus.Check(Leveled + "var x = Leaf.init()\nlet r = x@ref\nx.level += 1\n_ = r").Diagnostics).Code);

    [Theory]
    [InlineData("var x = Leaf.init()\nlet r = x.view\nx.item = 0\n_ = r")]
    [InlineData("let r = Leaf.init().view\n_ = r")]
    public void GetterResultsRetainTheirSourceLoan(string body)
        => Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(DiagnosticCorpus.Check(Source + body).Diagnostics).Code);

    [Theory]
    [InlineData("Leaf.init()")]
    [InlineData("Kimi.Intrinsics.makeObj(Leaf.init())")]
    public void ASharedGetterDoesNotProveTheSeparateExclusiveSetter(string value)
    {
        var code = "open struct Base\n    public computed number: i32\n        get(self: ref/Self) -> i32 => 42\n        set(self: uniq/Self, value: i32) -> () => ()\nstruct Leaf: Base\n    public init() => ()\nvar x = " + value + "\nx.number = 3";
        var result = DiagnosticCorpus.Check(code);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("Unsupported_Kd", record.Code);
        Assert.Equal("x.number", code.Substring(record.Span!.Value.Start, record.Span.Value.Length));
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

    [Theory]
    [InlineData("Owner", "let x = Kimi.Intrinsics.makeObj(Leaf.init())\nrequire x.number == 42 else => $abort(\"object getter\")")]
    [InlineData("Rc", "let x = Kimi.Intrinsics.makeRc(Leaf.init())\nrequire x.number == 42 else => $abort(\"shared getter\")")]
    [InlineData("Arc", "let x = Kimi.Intrinsics.makeArc(Leaf.init())\nrequire x.number == 42 else => $abort(\"shared getter\")")]
    [InlineData("Borrow", "let x = Kimi.Intrinsics.makeObj(Leaf.init())\nlet r = x@objref\nrequire r.number == 42 else => $abort(\"object getter\")")]
    [InlineData("Result", "let x = Kimi.Intrinsics.makeObj(Leaf.init())\nlet r = x.view\nrequire r == 42 else => $abort(\"object result\")")]
    [InlineData("OpenView", "let x = Kimi.Intrinsics.makeObj(Leaf.init())\nlet r = x@objref@objref/Base<i64>\nrequire r.number == 42 else => $abort(\"open view\")")]
    [InlineData("OwnSealed", "struct Plain\n    public computed value: i32\n        get(self: ref/Self) -> i32 => 42\nlet x = Kimi.Intrinsics.makeObj(Plain.init())\nrequire x.value == 42 else => $abort(\"own getter\")")]
    [InlineData("Generic", "struct Generic<T>: Base<T>\n    public init() => ()\nfunc read<T>(x: objref/Generic<T>) -> i32 => x.number\nlet x = Kimi.Intrinsics.makeObj(Generic<bool>.init())\nrequire read(x@objref) == 42 else => $abort(\"generic getter\")")]
    public void ObjectGettersUseTheProvenPayloadProjection(string name, string body)
        => ScalarEmissionTest.EmitFixture("InheritedObjectGetter" + name, Source + body, string.Empty);

    [Theory]
    [InlineData("var x = Kimi.Intrinsics.makeObj(Leaf.init())\nlet r = x.view\nx.item = 0\n_ = r")]
    [InlineData("let x = Kimi.Intrinsics.makeObj(Leaf.init())\nlet r = x.view\nlet moved = x@move\n_ = r")]
    [InlineData("var x = Kimi.Intrinsics.makeObj(Leaf.init())\nlet r = x.view\nx = Kimi.Intrinsics.makeObj(Leaf.init())\n_ = r")]
    public void ObjectGetterResultsRetainTheirOriginalObjectLoan(string body)
        => Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(DiagnosticCorpus.Check(Source + body).Diagnostics).Code);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Leaf.init()")]
    [InlineData("Kimi.Intrinsics.makeObj(Leaf.init())")]
    public void SharedGetterPlansAreReused(string value)
    {
        var c = MinimalEmissionTest.Analyze(Source + "let x = " + value + "\n_ = x.number\nlet r = x.view\n_ = r");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}
