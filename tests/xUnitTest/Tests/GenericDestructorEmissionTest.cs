// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericDestructorEmissionTest
{
    [Fact]
    public void ZeroSizedInstancesSelectTheirOwnSpecializedCalls()
    {
        var source = """
            group Support
                public func tag<T>() -> i32 => 0
                specialize func tag<i32>() -> i32 => 1
                specialize func tag<i64>() -> i32 => 2
            struct Marker<T>
                drop => Console.writeLine("\(Support.tag<T>())")
            let first = Marker<i32>.init()
            let second = Marker<i64>.init()
            """;
        ScalarEmissionTest.EmitFixture("GenericDestructorZeroSize", source, "2\n1\n");
    }

    [Theory]
    [InlineData("T", "ref/T", true)]
    [InlineData("raw/T", "raw/T", true)]
    [InlineData("raw/T", "raw/u8", false)]
    public void DestructorFieldsUseCompleteGenericSelf(string fieldType, string localType, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            struct Box<T>
                let value: {{fieldType}}
                drop
                    let observed: {{localType}} = self.value
            ()
            """);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
    }

    // SPEC 16.3.1: the destruction receiver has access equivalent to uniq/Self, so its initialized fields may be borrowed
    // exclusively or shared; the whole receiver still cannot be passed as an ordinary borrow.
    [Fact]
    public void DestructorBorrowsItsFields()
    {
        var source = """
            struct Counter
                public var n: i32
                public init() => self.n = 0
            group Support
                public func bump(counter: uniq/Counter) => counter.n += 1
                public func show(counter: ref/Counter) => Console.writeLine("count \(counter.n)")
            struct Owner
                var counter: Counter
                public init() => self.counter = Counter.init()
                drop
                    Support.bump(self.counter@uniq)
                    Support.show(self.counter@ref)
            let owner = Owner.init()
            """;
        ScalarEmissionTest.EmitFixture("DestructorFieldBorrows", source, "count 1\n");
    }

    // SPEC 16.3.1, 15.1.5: the general rules judge the whole destruction receiver under its exclusive access (DECISIONS BU-15): a
    // shared borrow is a helper borrow, and a Move, Copy, exclusive acquisition, assignment or uniq/Self argument is their record.
    [Theory]
    [InlineData("let whole = self@ref\n        Support.see(whole)", null)]
    [InlineData("Support.see(self)", null)]
    [InlineData("Support.take(self@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("Support.take(self)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("Support.sink(self@move)", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("Support.sink(self)", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("let copied = self@copy", DiagnosticCode.NonCopyOperand_Kd)]
    [InlineData("self = Owner.init()", DiagnosticCode.InvalidAssignment_Kd)]
    public void TheWholeDestructionReceiverIsJudgedUnderExclusiveAccess(string use, DiagnosticCode? code)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            struct Owner
                var n: i32
                public init() => self.n = 0
                drop
                    {{use}}
            group Support
                public func see(owner: ref/Owner) => ()
                public func take(owner: uniq/Owner) => ()
                public func sink(owner: Owner) => ()
            ()
            """);
        Assert.Equal(code is { } expected ? [expected] : [], c.Binding.Issues.Select(static x => x.Code));
    }

    [Fact]
    public void InstantiatedBodyRunsBeforeInstantiatedFieldCleanup()
        => ScalarEmissionTest.EmitFixture(
            "GenericDestructorOrder",
            "struct Child\n    drop => Console.writeLine(\"child\")\nstruct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    drop => Console.writeLine(\"box\")\nlet a = Box<Child>.init(Child.init())\nlet b = Box<i32>.init(7)\nConsole.writeLine(\"body\")",
            "body\nbox\nbox\nchild\n");

    [Fact]
    public void FieldDependentBodiesUseTheirConcreteReceiverLayouts()
    {
        var source = """
            struct Child
                let tag: i32
                public init(tag: i32) => self.tag = tag
                drop => Console.writeLine("child \(self.tag)")
            group Support
                public func inspect<T>(value: ref/T, tag: i32)
                    Console.writeLine("box \(tag)")
            struct Box<T>
                let value: T
                let tag: i32
                public init(value: T, tag: i32)
                    self.value = value@move
                    self.tag = tag
                drop => Support.inspect(self.value, self.tag)
            let small = Box<i8>.init(7, 1)
            let large = Box<(i64, i64)>.init((8, 9), 2)
            let nested = Box<Child>.init(Child.init(4), 3)
            Console.writeLine("body")
            """;
        ScalarEmissionTest.EmitFixture("GenericDestructorFields", source, "body\nbox 3\nchild 4\nbox 2\nbox 1\n");
    }

    [Fact]
    public void DestructorLocalsDiscoverFurtherConcreteDestructors()
    {
        var source = """
            struct Local<T>
                let value: T
                let tag: i32
                public init(value: T, tag: i32)
                    self.value = value@move
                    self.tag = tag
                drop => Console.writeLine("local \(self.tag)")
            struct Box<T>
                let value: T
                public init(value: T) => self.value = value@move
                drop
                    let local = Local<raw/T>.init(null, 7)
                    Console.writeLine("box")
            let value = Box<i32>.init(1)
            """;
        ScalarEmissionTest.EmitFixture("GenericDestructorLocals", source, "box\nlocal 7\n");
    }

    [Fact]
    public void DeferredFormattingUsesEachConcreteExitAndReceiver()
    {
        var source = """
            struct Child
                let tag: i32
                public init(tag: i32) => self.tag = tag
                drop => Console.writeLine("child \(self.tag)")
            struct Box<T>
                let value: T
                var tag: i32
                public init(value: T, tag: i32)
                    self.value = value@move
                    self.tag = tag
                drop
                    self.tag += 10
                    defer => Console.writeLine("defer \(self.tag)")
                    if self.tag == 11 => return
                    Console.writeLine("body \(self.tag)")
            let small = Box<i8>.init(7, 2)
            let large = Box<(Child, i64)>.init((Child.init(3), 8), 1)
            """;
        ScalarEmissionTest.EmitFixture("GenericDestructorDeferredFormatting", source, "defer 11\nchild 3\nbody 12\ndefer 12\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InstantiatedDestructorsReuseWarmEmissionState()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    let tag: i32\n    public init(value: T)\n        self.value = value@move\n        self.tag = 1\n    drop\n        if self.tag == 1 => Console.writeLine(\"tag\")\nlet a = Box<i8>.init(7)\nlet b = Box<i64>.init(8)");
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.WriteIr(TextWriter.Null, out var error))
            {
                throw new InvalidOperationException(error);
            }
        }));
    }

    [Theory]
    [InlineData("raw/T")]
    [InlineData("(T, T)")]
    public void GrowingDestructorContextsAreResourceDiagnosed(string argument)
        => MinimalEmissionTest.AssertGenerationResourceLimit($"struct Grow<T>\n    let pointer: raw/T\n    public init() => self.pointer = null\n    drop\n        let next = Grow<{argument}>.init()\nlet value = Grow<i32>.init()", $"let next = Grow<{argument}>.init()");

    // SPEC 16.3.1: no Field of drop's self is Moved out, so each Move is Binding's record, in a generic body too.
    [Fact]
    public void GenericDestructorStillRejectsUninitializedFields()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    drop\n        let moved = self.value@move\n        let again = self.value@move\nlet value = Box<i32>.init(7)");
        Assert.Equal([DiagnosticCode.ExclusivePathTake_Kd, DiagnosticCode.ExclusivePathTake_Kd], c.Binding.Issues.Select(static x => x.Code));
        using var output = new StringWriter();
        Assert.False(c.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }
}
