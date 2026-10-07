// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class InheritedSharedCallTest
{
    private const string Layers = """
        open struct Base<T>
            public var item: i32 = 42
            public func read(self: ref/Self) -> i32 => self.item
            public func view(self: ref/Self) -> ref/i32 => self.item@ref
        open struct Middle<U>: Base<U>
            protected init() => ()
        struct Leaf: Middle<i64>
            public let extra: i32 = 7
            public init() => ()

        """;

    [Theory]
    [InlineData("Owner", "let x = Leaf.init()\nrequire x.read() == 42 else => $abort(\"base call\")")]
    [InlineData("Reference", "func read(x: ref/Leaf) -> i32 => x.read()\nlet x = Leaf.init()\nrequire read(x@ref) == 42 else => $abort(\"base call\")")]
    [InlineData("Exclusive", "func read(x: uniq/Leaf) -> i32 => x.read()\nvar x = Leaf.init()\nrequire read(x@uniq) == 42 else => $abort(\"base call\")")]
    [InlineData("Nested", "let x = (Leaf.init(), 0)\nrequire x.0.read() == 42 else => $abort(\"base call\")")]
    [InlineData("Temporary", "require Leaf.init().read() == 42 else => $abort(\"base call\")")]
    [InlineData("Result", "let x = Leaf.init()\nlet r = x.view()\nrequire r == 42 else => $abort(\"base call\")")]
    [InlineData("BorrowedNested", "func read(x: ref/(Leaf, i32)) -> i32 => x.0.read()\nlet x = (Leaf.init(), 0)\nrequire read(x@ref) == 42 else => $abort(\"base call\")")]
    [InlineData("Repeated", "let x = Leaf.init()\nrequire x.read() + x.read() == 84 else => $abort(\"base call\")")]
    [InlineData("Generic", "struct Generic<V>: Middle<V>\n    public init() => ()\nfunc read<T>(x: ref/Generic<T>) -> i32 => x.read()\nlet x = Generic<bool>.init()\nrequire read(x@ref) == 42 else => $abort(\"base call\")")]
    public void SharedBaseMethodPreservesTheOriginalReceiver(string name, string body)
        => ScalarEmissionTest.EmitFixture("InheritedShared" + name, Layers + body, string.Empty);

    [Theory]
    [InlineData("var x = Leaf.init()\nlet r = x.view()\nx.item = 0\n_ = r")]
    [InlineData("let x = Leaf.init()\nlet r = x.view()\nlet moved = x@move\n_ = r")]
    [InlineData("let r = Leaf.init().view()\n_ = r")]
    public void ReturnedLoansKeepTheOriginalStorageAlive(string body)
    {
        var records = DiagnosticCorpus.Check(Layers + body).Diagnostics;
        Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(records).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ProjectedCallsReuseBorrowAndGenerationPlans()
    {
        var c = MinimalEmissionTest.Analyze(Layers + "let x = Leaf.init()\n_ = x.read()\nlet r = x.view()\n_ = r");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}
