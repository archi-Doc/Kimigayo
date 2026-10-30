// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class InheritedStorageTest
{
    // A constructor of Tally whose body continues once its Fields are initialized; Buffer is Non-Copy.
    private const string Tally =
        "struct Buffer\n    public var n: i32 = 0\n    public init() => ()\ngroup Audit\n    public func change(value: uniq/Buffer) => value.n += 10\n" +
        "    public func read(value: ref/Buffer) -> i32 => value.n\n    public func pass(value: uniq/Buffer) -> uniq/Buffer => value\n" +
        "struct Tally\n    var total: i32\n    var buffer: Buffer\n    public init(start: i32)\n        self.total = 0\n        self.buffer = Buffer.init()\n";

    // A constructor of Holder, whose view Field stores a shared reference during a, once its Fields are initialized.
    private const string Holder =
        "struct Buffer\n    public var n: i32 = 0\n    public init() => ()\nstruct Holder {a}\n    var buffer: Buffer\n    public var view: ref/Buffer during a\n" +
        "    public init(other: ref/Buffer during a)\n        self.buffer = Buffer.init()\n        self.view = other\n";

    [Fact]
    public void InheritedStandardStorageKeepsItsDeclaringLayer()
    {
        const string Source = """
            open struct Base
                public var count: i32
                protected init() => self.count = 3
            struct Leaf: Base
                public let extra: i32 = 7
                public init(): base() => ()
            var item = Leaf.init()
            require item.count == 3 and item.extra == 7 else => $abort("read")
            item.count = 5
            require item.count + item.extra == 12 else => $abort("write")
            let moved = item@move
            require moved.count == 5 else => $abort("move")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStorageProjection", Source, string.Empty);
    }

    [Fact]
    public void BaseArgumentsCannotAccessTheConstructionReceiver()
    {
        const string Source = """
            open struct Base
                protected init(value: i32) => ()
            struct Leaf: Base
                let value: i32 = 42
                public init(): base(self.value) => ()
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    // SPEC 6.3.4, 11.7: the construction receiver also forbids inherited Field and computed access, custom accessor calls and
    // a Non-Copy Move, each one Language record at the access.
    [Theory]
    [InlineData("open struct Base\n    public var x: i32 = 1\n    public init() => ()\nstruct Leaf: Base\n    var y: i32\n    public init()\n        self.y = self.x\n", "self.x")]
    [InlineData("open struct Base\n    public var x: i32 = 1\n    public init() => ()\nstruct Leaf: Base\n    var y: i32 = 0\n    public init()\n        self.x = 3\n", "self.x")]
    [InlineData("struct S\n    var a: i32\n    public computed b: i32\n        get() -> i32 => 5\n    public init()\n        self.a = self.b\n", "self.b")]
    [InlineData("struct S\n    public computed b: i32\n        get() -> i32 => 5\n        set(value: i32) -> () => ()\n    public init()\n        self.b = 1\n", "self.b")]
    [InlineData("struct S\n    var a: i32\n    public var b: i32 = 0\n        get() -> i32 => storage\n    public init()\n        self.a = self.b\n", "self.b")]
    [InlineData("struct S\n    public var b: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value\n    public init()\n        self.b += 1\n", "self.b")]
    [InlineData("struct R\n    public var n: i32 = 0\n    drop => ()\nstruct S\n    var r: R\n    public init(r: R)\n        self.r = r@move\n        let moved = self.r@move\n", "self.r@move")]
    [InlineData("struct R\n    public var n: i32 = 0\n    drop => ()\nstruct H\n    public var r: R = R.init()\nstruct S\n    var h: H\n    public init(h: H)\n        self.h = h@move\n        let moved = self.h.r@move\n", "self.h.r@move")]
    public void TheConstructionReceiverForbidsInheritedComputedAndMovingAccess(string declarations, string at)
    {
        var source = declarations + "public func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), source.LastIndexOf(at, StringComparison.Ordinal), at.Length), (error.Code, error.Span!.Value.Start, error.Span.Value.Length));
    }

    // SPEC 6.3.4, 15.1.5: a constructor's writable Fields are lent exclusively, but no analysis models those Loans yet ("Field borrows end
    // before completion"; PLAN G166), so each exclusive borrow is one located Unsupported_Kd and the body reaches no analysis: no
    // conflict is accepted, no read of `start` is a conflict and no check faults (DECISIONS BU-15). The rows are a write, a second
    // borrow, a read, `exchange` and early exits while a borrow lives, and a stored reference Field lent to `exchange`.
    [Theory]
    [InlineData(Tally + "        Kimi.Intrinsics.replace(self.total@uniq, with: 5)\n", "self.total@uniq")]
    [InlineData(Tally + "        Kimi.Intrinsics.replace(self.total@uniq/i32, with: 5)\n", "self.total@uniq/i32")]
    [InlineData(Tally + "        let old = Kimi.Intrinsics.exchange(self.buffer@uniq, with: Buffer.init())\n", "self.buffer@uniq")]
    [InlineData(Tally + "        Audit.change(self.buffer@uniq)\n", "self.buffer@uniq")]
    [InlineData(Tally + "        let r = self.buffer@uniq\n        self.buffer = Buffer.init()\n        Audit.change(r)\n", "self.buffer@uniq")]
    [InlineData(Tally + "        let r = self.buffer@uniq\n        let s = self.buffer@uniq\n        Audit.change(r)\n        Audit.change(s)\n", "self.buffer@uniq")]
    [InlineData(Tally + "        let r = Audit.pass(self.buffer@uniq)\n        let n = Audit.read(self.buffer@ref)\n        Audit.change(r)\n", "self.buffer@uniq")]
    [InlineData(Tally + "        let r = self.buffer@uniq\n        let old = Kimi.Intrinsics.exchange(self.buffer@uniq, with: Buffer.init())\n        Audit.change(r)\n", "self.buffer@uniq")]
    [InlineData(Tally + "        let r = self.buffer@uniq\n        if start > 0\n            return\n        Audit.change(r)\n", "self.buffer@uniq")]
    [InlineData(Tally + "        let r = self.buffer@uniq\n        let k = start + 1\n        Audit.change(r)\n", "self.buffer@uniq")]
    [InlineData(Holder + "        let old = Kimi.Intrinsics.exchange(self.view@uniq, with: self.buffer@ref)\n", "self.view@uniq")]
    [InlineData(Holder + "        var local = Buffer.init()\n        let old = Kimi.Intrinsics.exchange(self.view@uniq, with: local@ref)\n", "self.view@uniq")]
    public void AConstructorsExclusiveFieldBorrowIsUnsupported(string declarations, string at)
    {
        var source = declarations + "public func main() => ()\n";
        var expected = new List<(string, int, int)>();
        for (var i = source.IndexOf(at, StringComparison.Ordinal); i >= 0; i = source.IndexOf(at, i + 1, StringComparison.Ordinal))
        {
            expected.Add((nameof(DiagnosticCode.Unsupported_Kd), i, at.Length));
        }

        Assert.Equal(expected, DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length)));
    }

    // SPEC 6.3.4, 16.3.1, 15.6.5: the construction and the destruction receiver are Places of their own, never a parameter: a borrow of
    // a receiver Field and a borrow of the first parameter have distinct Origins, and both receivers take one reserved receiver slot.
    [Fact]
    public void TheSpecialReceiversArePlacesOfTheirOwn()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    var n: i32\n    public init(x: i32)\n        self.n = x\n        let a = self.n@ref\n" +
            "        let b = x@ref\n        _ = a\n        _ = b\n    drop\n        let c = self.n@ref\n        _ = c\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var borrows = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>()
            .Select(static x => (Text: x.ToString(), Origin: x.TypeOf()!.Origin!)).ToArray();
        var receiver = borrows.Where(static x => x.Text == "self.n@ref").Select(static x => x.Origin).ToArray();
        var parameter = Assert.Single(borrows, static x => x.Text == "x@ref").Origin;
        Assert.Equal(2, receiver.Length);
        Assert.NotSame(receiver[0], parameter);
        Assert.All(receiver, static x => Assert.Equal(Binding.ReceiverSlot, x.Slot));
    }

    // SPEC 6.3.4, 15.6.5: a borrow of a construction receiver's Field is no borrow of a parameter, so moving a parameter while the
    // borrow lives is valid, whatever the parameter's position.
    [Theory]
    [InlineData("start: string, other: i32")]
    [InlineData("other: i32, start: string")]
    public void AReceiverFieldBorrowIsNoBorrowOfAParameter(string parameters)
    {
        var source = "group Audit\n    public func see<T>(value: ref/T) => ()\n    public func consume(s: string) => ()\nstruct Tally\n    var count: i32\n" +
            "    var total: i32\n    public init(" + parameters + ")\n        self.count = other\n        self.total = 0\n        let b = self.count@ref\n" +
            "        Audit.consume(start@move)\n        Audit.see(b)\npublic func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // SPEC 7.2.3, 6.3.4: a default sees only the parameters before its own; the construction receiver is no parameter, in any position.
    [Theory]
    [InlineData("public init(a: i32 = self.n)")]
    [InlineData("public init(a: i32, b: i32 = self.n)")]
    public void ADefaultDoesNotSeeTheConstructionReceiver(string header)
    {
        var source = "struct S\n    var n: i32\n    " + header + "\n        self.n = 1\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnresolvedBinding_Kd), source.IndexOf("self.n)", StringComparison.Ordinal)), (error.Code, error.Span!.Value.Start));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BaseConstructionPrecedesOwnInitializersAndDestructionFollowsOwnFields()
    {
        const string Source = """
            struct Resource
                drop => Console.writeLine("resource")
            group Helpers
                public func initialValue() -> i32
                    Console.writeLine("field")
                    return 42
            open struct Base
                let id: i32
                protected init(value: i32)
                    self.id = value
                    Console.writeLine("base init")
                drop
                    require self.id == 12 else => $abort("base storage")
                    Console.writeLine("base")
            struct Leaf: Base
                public let extra: i32 = Helpers.initialValue()
                let resource: Resource = Resource.init()
                public init(value: i32): base(value) => Console.writeLine("leaf init")
                drop => Console.writeLine("leaf")
            let item = Leaf.init(12)
            require item.extra == 42 else => $abort("own storage")
            """;
        NativeAllocationAudit.WriteFixture("InheritedStorageOrder", Source, 0, 0, 0, "base init\nfield\nleaf init\nleaf\nresource\nbase\n");
    }

    [Fact]
    public void NestedBasePrefixesKeepTheirPaddingAndDestructors()
    {
        const string Source = """
            open struct Base
                let small: i8
                let large: i64
                protected init()
                    self.small = 7
                    self.large = 123456
                drop
                    require self.small == 7 and self.large == 123456 else => $abort("prefix")
                    Console.writeLine("base")
            open struct Middle: Base
                let extra: i8
                protected init(): base() => self.extra = 11
                drop
                    require self.extra == 11 else => $abort("middle")
                    Console.writeLine("middle")
            struct Leaf: Middle
                public let extraLeaf: i32
                public init(): base() => self.extraLeaf = 23
                drop => Console.writeLine("leaf")
            let item = Leaf.init()
            require item.extraLeaf == 23 else => $abort("leaf")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStoragePadding", Source, "leaf\nmiddle\nbase\n");
    }

    [Fact]
    public void ZeroSizedBaseStillRunsItsDestructor()
    {
        const string Source = """
            open struct Base
                protected init() => ()
                drop => Console.writeLine("base")
            struct Leaf: Base
                public let extra: i64
                public init(): base() => self.extra = 42
            let item = Leaf.init()
            require item.extra == 42 else => $abort("own storage")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStorageZero", Source, "base\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MovingEveryOwnFieldStillDestroysTheCompleteBaseRemainder()
    {
        const string Source = """
            open struct Base
                let name: string
                protected init() => self.name = "base"
                drop => Console.writeLine(self.name)
            struct Leaf: Base
                public let text: string
                public init(): base() => self.text = "own"
            func run(move: bool)
                let item = Leaf.init()
                if move
                    let taken = item.text@move
                    Console.writeLine(taken)
            run(true)
            run(false)
            """;
        NativeAllocationAudit.WriteFixture("InheritedStoragePartial", Source, 0, 0, 0, "own\nbase\nbase\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DynamicTypeTestsUseTheVerifiedBaseMapAndDestroyTheCompletePayload()
    {
        const string Source = """
            open struct Base
                let value: i32
                protected init() => self.value = 10
                drop
                    require self.value == 10 else => $abort("base contents")
                    Console.writeLine("base")
            struct Leaf: Base
                let extra: i32
                public init(): base() => self.extra = 20
                drop
                    require self.extra == 20 else => $abort("leaf contents")
                    Console.writeLine("leaf")
            struct Other
            let owner = Kimi.Intrinsics.makeObj(Leaf.init())
            require owner is Leaf and owner is Base and owner is not Other else => $abort("dynamic identity")
            """;
        NativeAllocationAudit.WriteFixture("InheritedStorageObject", Source, 1, 1, 24, "leaf\nbase\n");
    }
}
