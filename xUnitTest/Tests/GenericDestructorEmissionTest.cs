// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericDestructorEmissionTest
{
    [Theory]
    [InlineData("T", "ref/T", true)]
    [InlineData("unsafe/T", "unsafe/T", true)]
    [InlineData("unsafe/T", "unsafe/u8", false)]
    public void DestructorFieldsUseCompleteGenericSelf(string fieldType, string localType, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            struct Box<T>
                let value: {{fieldType}}
                deinit
                    let observed: {{localType}} = self.value
            ()
            """);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void InstantiatedBodyRunsBeforeInstantiatedFieldCleanup()
        => ScalarEmissionTest.EmitFixture(
            "GenericDestructorOrder",
            "struct Child\n    deinit => Console.writeLine(\"child\")\nstruct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    deinit => Console.writeLine(\"box\")\nlet a = Box<Child>.init(Child.init())\nlet b = Box<i32>.init(7)\nConsole.writeLine(\"body\")",
            "body\nbox\nbox\nchild\n");

    [Fact]
    public void FieldDependentBodiesUseTheirConcreteReceiverLayouts()
    {
        var source = """
            struct Child
                let tag: i32
                public init(tag: i32) => self.tag = tag
                deinit => Console.writeLine("child \(self.tag)")
            group Support
                public func inspect<T>(value: ref/T, tag: i32)
                    Console.writeLine("box \(tag)")
            struct Box<T>
                let value: T
                let tag: i32
                public init(value: T, tag: i32)
                    self.value = value@move
                    self.tag = tag
                deinit => Support.inspect(self.value, self.tag)
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
                deinit => Console.writeLine("local \(self.tag)")
            struct Box<T>
                let value: T
                public init(value: T) => self.value = value@move
                deinit
                    let local = Local<unsafe/T>.init(null, 7)
                    Console.writeLine("box")
            let value = Box<i32>.init(1)
            """;
        ScalarEmissionTest.EmitFixture("GenericDestructorLocals", source, "box\nlocal 7\n");
    }

    [Fact]
    public void InstantiatedDestructorsReuseWarmEmissionState()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    let tag: i32\n    public init(value: T)\n        self.value = value@move\n        self.tag = 1\n    deinit\n        if self.tag == 1 => Console.writeLine(\"tag\")\nlet a = Box<i8>.init(7)\nlet b = Box<i64>.init(8)");
        for (var i = 0; i < 16; i++)
        {
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Emission.WriteIr(TextWriter.Null, out var error))
            {
                throw new InvalidOperationException(error);
            }
        }));
    }

    [Fact]
    public void DestructorSubstitutionsRespectTheGenerationLimit()
    {
        var limit = GenericStoragePlan.SubstitutionSetLimit;
        GenericStoragePlan.SubstitutionSetLimit = 2;
        try
        {
            var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    deinit => ()\nfunc a(value: Box<i8>) => ()\nfunc b(value: Box<i16>) => ()\nfunc c(value: Box<i32>) => ()\npublic func main() => ()");
            using var output = new StringWriter();
            Assert.False(c.Emission.WriteIr(output, out var error));
            Assert.Empty(output.ToString());
            Assert.True(c.Emission.FailureIsResourceLimit, error);
            GenericStoragePlan.SubstitutionSetLimit = 3;
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
        }
        finally
        {
            GenericStoragePlan.SubstitutionSetLimit = limit;
        }
    }

    [Fact]
    public void GenericDestructorStillRejectsUninitializedFields()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    deinit\n        let moved = self.value@move\n        let again = self.value@move\nlet value = Box<i32>.init(7)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.ErrorCount > 0);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }
}
