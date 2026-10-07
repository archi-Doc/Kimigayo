// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class InheritedProjectionEmissionTest
{
    private const string Layers = """
        open struct Base<T>
            public var item: T
            protected init(item: T) => self.item = item@move
        open struct Middle<U>: Base<U>
            public var small: i8 = 2
            protected init(item: U): base(item@move) => ()
        struct Leaf: Middle<i64>
            public var extra: i32 = 7
            public init(): base(3) => ()

        """;

    [Theory]
    [InlineData("Owned", "var x = Leaf.init()\nx.item += 2\nx.small++\nrequire x.item == 5 and x.small == 3 and x.extra == 7 else => $abort(\"owned\")")]
    [InlineData("Borrowed", "func change(x: uniq/Leaf)\n    x.item += 2\n    x.extra = 9\nfunc read(x: ref/Leaf) -> i64 => x.item\nvar x = Leaf.init()\nchange(x@uniq)\nrequire read(x@ref) == 5 and x.extra == 9 else => $abort(\"borrowed\")")]
    [InlineData("Nested", "var x = (Leaf.init(), 12)\nx.0.item = 8\nrequire x.0.item == 8 and x.0.extra == 7 and x.1 == 12 else => $abort(\"nested\")")]
    [InlineData("Disjoint", "var x = Leaf.init()\nlet r = x.item@ref\nx.extra = 8\nrequire r == 3 and x.extra == 8 else => $abort(\"disjoint\")")]
    [InlineData("Object", "var x = Kimi.Intrinsics.makeObj(Leaf.init())\nx.item = 9\nrequire x.item == 9 and x.extra == 7 else => $abort(\"object\")")]
    [InlineData("Raw", "var x = Leaf.init()\nunsafe\n    let p = x@raw\n    (*p).item = 11\n    require (*p).item == 11 and (*p).extra == 7 else => $abort(\"raw\")")]
    public void StandardStorageUsesOneSubstitutedFieldIdentity(string name, string body)
        => ScalarEmissionTest.EmitFixture("InheritedProjection" + name, Layers + body, string.Empty);

    [Theory]
    [InlineData("Base", "let taken = x.item@move", "base\nown\n")]
    [InlineData("Own", "let taken = x.extra@move", "own\nbase\n")]
    [InlineData("Both", "let a = x.item@move\nlet b = x.extra@move", "own\nbase\n")]
    [InlineData("Replace", "let a = x.item@move\nx.item = Resource.init(\"new\")", "base\nown\nnew\n")]
    public void PartialMovesDestroyOnlyTheRemainingFields(string name, string body, string stdout)
    {
        const string Source = """
            struct Resource
                public let text: string
                public init(text: string) => self.text = text@move
                drop => Console.writeLine(self.text)
            open struct Base
                public var item: Resource
                protected init() => self.item = Resource.init("base")
            struct Leaf: Base
                public var extra: Resource = Resource.init("own")
                public init(): base() => ()
            var x = Leaf.init()

            """;
        ScalarEmissionTest.EmitFixture("InheritedProjectionMove" + name, Source + body, stdout);
    }

    [Fact]
    public void ConditionalMoveKeepsTheOtherBaseFieldsAndLayeredCleanup()
    {
        const string Source = """
            struct Resource
                let id: i32
                public init(id: i32) => self.id = id
                drop => Console.writeLine("\(self.id)")
            open struct Base
                public let first: Resource = Resource.init(1)
                let second: Resource = Resource.init(2)
                protected init() => ()
            open struct Middle: Base
                let middle: Resource = Resource.init(3)
                protected init(): base() => ()
            struct Leaf: Middle
                let own: Resource = Resource.init(4)
                public init(): base() => ()
            func run(take: bool)
                let x = Leaf.init()
                if take
                    let value = x.first@move
                Console.writeLine("end")
            run(true)
            run(false)
            """;
        ScalarEmissionTest.EmitFixture("InheritedProjectionConditionalMove", Source, "1\nend\n4\n3\n2\nend\n4\n3\n2\n1\n");
    }

    [Fact]
    public void GenericConstructorsExecuteEveryBaseLayerExactlyOnce()
    {
        const string Source = """
            open struct Base<T>
                public let item: T
                protected init(item: T)
                    self.item = item@move
                    Console.writeLine("base init")
                drop => Console.writeLine("base drop")
            open struct Middle<U>: Base<U>
                protected init(item: U): base(item@move) => Console.writeLine("middle init")
                drop => Console.writeLine("middle drop")
            struct Leaf: Middle<i64>
                public init(): base(42) => Console.writeLine("leaf init")
                drop => Console.writeLine("leaf drop")
            let x = Leaf.init()
            require x.item == 42 else => $abort("base value")
            """;
        ScalarEmissionTest.EmitFixture("InheritedProjectionGenericConstruction", Source, "base init\nmiddle init\nleaf init\nleaf drop\nmiddle drop\nbase drop\n");
    }

    [Fact]
    public void MovingInheritedStorageAcrossADropLayerRemainsRejected()
    {
        const string Source = """
            open struct Base
                public let text: string = "base"
                protected init() => ()
                drop => ()
            struct Leaf: Base
                public init(): base() => ()
            let x = Leaf.init()
            let taken = x.text@move
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("let r = x.item@ref\nx.item = 9\n_ = r", "ComparisonLoanConflict_Kd")]
    [InlineData("let moved = x@move\n_ = x.item", "MovedPlace_Kd")]
    public void InheritedStoragePreservesLoanAndMoveChecks(string body, string code)
    {
        var records = DiagnosticCorpus.Check(Layers + "var x = Leaf.init()\n" + body).Diagnostics;
        Assert.Equal(code, Assert.Single(records).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedStoragePlansAreReused()
    {
        var c = MinimalEmissionTest.Analyze(Layers + "var x = Leaf.init()\nx.item += 2\n_ = x.extra\n_ = x.item");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}
