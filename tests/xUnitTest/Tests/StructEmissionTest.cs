// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class StructEmissionTest
{
    internal const string Resource = "struct Resource\n    public var value: i32\n    public init()\n        self.value = 7\n        Console.writeLine(\"created\")\n    drop => Console.writeLine(\"destroyed\")\n";

    [Theory]
    [InlineData("Local", "let value = Resource.init()\nif value.value == 7 => Console.writeLine(\"ok\")", "created\nok\ndestroyed\n")]
    [InlineData("Move", "func take(value: Resource)\n    defer => Console.writeLine(\"defer\")\n    if value.value == 7 => Console.writeLine(\"ok\")\nlet value = Resource.init()\ntake(value@move)\nConsole.writeLine(\"done\")", "created\nok\ndefer\ndestroyed\ndone\n")]
    [InlineData("Update", "var value = Resource.init()\nvalue.value = value.value + 2\nif value.value == 9 => Console.writeLine(\"ok\")", "created\nok\ndestroyed\n")]
    [InlineData("Return", "func make() -> Resource\n    let value = Resource.init()\n    return value@move\nlet value = make()\nConsole.writeLine(\"done\")", "created\ndone\ndestroyed\n")]
    [InlineData("Replacement", "var value = Resource.init()\nvalue = Resource.init()\nConsole.writeLine(\"done\")", "created\ncreated\ndestroyed\ndone\ndestroyed\n")]
    [InlineData("ConditionalMove", "func take(value: Resource) => ()\nfunc f(flag: bool)\n    let value = Resource.init()\n    if flag => take(value@move)\n    Console.writeLine(\"done\")\nf(true)\nf(false)", "created\ndestroyed\ndone\ncreated\ndone\ndestroyed\n")]
    public void ExecutesOwnedStruct(string name, string body, string stdout)
    {
        var compilation = MinimalEmissionTest.Analyze(Resource + body);
        Assert.Empty(compilation.Ownership.ControlFlow!.Issues);
        Assert.Empty(compilation.Ownership.ControlFlow.PendingBinding);
        ScalarEmissionTest.EmitFixture("Struct" + name, Resource + body, stdout);
    }

    [Theory]
    [InlineData("Arguments", "struct S\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop\n        if self.value == 9 => Console.writeLine(\"nine\")\nlet s = S.init(9)", "nine\n")]
    [InlineData("LetField", "struct S\n    public let value: i32\n    public init() => self.value = 8\nlet s = S.init()\nif s.value == 8 => Console.writeLine(\"eight\")", "eight\n")]
    [InlineData("EarlyReturn", "struct S\n    public var value: i32\n    public init(flag: bool)\n        self.value = 8\n        defer => self.value = 9\n        if flag => return\n    drop\n        if self.value == 9 => Console.writeLine(\"nine\")\nlet a = S.init(true)\nlet b = S.init(false)", "nine\nnine\n")]
    [InlineData("Empty", "struct S\n    public init() => Console.writeLine(\"create\")\n    drop => Console.writeLine(\"drop\")\nlet s = S.init()", "create\ndrop\n")]
    [InlineData("Nested", "struct Inner\n    public var value: i32\n    public init() => self.value = 1\n    drop => Console.writeLine(\"inner\")\nstruct Outer\n    public var child: Inner\n    public init() => self.child = Inner.init()\n    drop => Console.writeLine(\"outer\")\nlet s = Outer.init()", "outer\ninner\n")]
    [InlineData("DistinctDestructors", "struct A\n    public var value: i32\n    public init() => self.value = 0\n    drop => Console.writeLine(\"A\")\nstruct B\n    public var value: i32\n    public init() => self.value = 0\n    drop => Console.writeLine(\"B\")\nlet a = A.init()\nlet b = B.init()", "B\nA\n")]
    [InlineData("DestructorReturn", "struct Inner\n    public init() => ()\n    drop => Console.writeLine(\"inner\")\nstruct S\n    var child: Inner\n    public init() => self.child = Inner.init()\n    drop\n        defer => Console.writeLine(\"defer\")\n        return\nlet s = S.init()", "defer\ninner\n")]
    [InlineData("StringField", "struct S\n    public var text: string\n    public init(text: string) => self.text = text@move\n    drop => Console.writeLine(\"drop\")\nlet s = S.init(\"held\")", "drop\n")]
    public void ExecutesConstructionAndDestruction(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(!c.Diagnostics.HasErrors, string.Join("\n", TestDiagnostics.Of(c)));
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(
            !c.Diagnostics.HasSyntaxErrors(c.Kotonoha) && c.Ownership.Result.IsVerified &&
                !c.Binding.Obligations.Any(x => !ReferenceEquals(x.Use.CodeContext.Kotonoha, c.Library.Kotonoha)),
            $"SourceErrors={c.Diagnostics.HasSyntaxErrors(c.Kotonoha)}; ownership={c.Ownership.Result}; obligations={string.Join(';', c.Binding.Obligations)}");
        ScalarEmissionTest.EmitFixture("Struct" + name, source, stdout);
    }

    [Theory]
    [InlineData("struct S\n    var value: i32\n    public init() => ()\nlet s = S.init()")]
    [InlineData("struct S\n    var value: i32\n    public init(flag: bool)\n        if flag => self.value = 1\nlet s = S.init(true)")]
    [InlineData("struct S\n    var value: i32\n    public init()\n        defer => self.value = 1\nlet s = S.init()")]
    [InlineData("struct S\n    var value: i32\n    public init() => self.value = self.value + 1\nlet s = S.init()")]
    [InlineData("struct S\n    let value: i32\n    public init()\n        self.value = 1\n        self.value = 2\nlet s = S.init()")]
    [InlineData(Resource + "func take(value: Resource) => ()\nlet value = Resource.init()\ntake(value@move)\nlet x = value.value")]
    [InlineData(Resource + "func take(value: Resource) => ()\nlet value = Resource.init()\ntake(value@move)\ntake(value@move)")]
    [InlineData(Resource + "func take(value: Resource) => ()\nlet value = Resource.init()\ntake(value)")]
    [InlineData(Resource + "let value = Resource.init()\nvalue.value = 1")]
    [InlineData("struct S\n    public let value: i32 = 1\n    public init() => ()\nvar s = S.init()\ns.value = 2")]
    [InlineData("struct S\n    var value: i32\n    init() => self.value = 1\nlet s = S.init()")]
    [InlineData(Resource + "let value = Resource.init()\nvalue.drop()")]
    [InlineData(Resource + "let value = Resource.init()\nvalue.init()")]
    [InlineData(Resource + "let ctor = Resource.init")]
    [InlineData("struct S\n    Self is Copy\n    public init() => ()\n    drop => ()\nlet s = S.init()")]
    [InlineData("struct S\n    public init() => ()\n    drop => ()\n    drop => ()\nlet s = S.init()")]
    [InlineData("struct S\n    public var text: string\n    public init() => self.text = \"held\"\n    drop => ()\nlet s = S.init()\nlet taken = s.text@move")]
    [InlineData("struct S\n    var value: i32\n    public init()\n        let escaped = self\n        self.value = 1\nlet s = S.init()")]
    public void RejectsInvalidStructOperations(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified && !c.Diagnostics.HasErrors);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("self.value = self.value + 1")]
    [InlineData("if false => self.value = 1")]
    [InlineData("defer => self.value = 1")]
    public void ConstructionMustBeCompleteBeforeCleanup(string body)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    var value: i32\n    public init()\n        " + body + "\nlet s = S.init()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
    }

    // A missing field is one record per exit that completes construction: at the constructor's signature for the normal end,
    // checked before and after the body's cleanup, and at an early return.
    [Theory]
    [InlineData("if false => self.value = 1")]
    [InlineData("defer => self.value = 1")]
    public void IncompleteConstructionIsReportedOncePerExit(string body)
    {
        var source = "struct S\n    var value: i32\n    public init(flag: bool)\n        " + body + "\n        if flag => return\nlet s = S.init(true)\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics.OrderBy(static x => x.Span!.Value.Start).ToArray();
        Assert.Equal(["UninitializedPlace_Kd", "UninitializedPlace_Kd"], records.Select(static x => x.Code));
        Assert.Equal("init(flag: bool)", source.Substring(records[0].Span!.Value.Start, records[0].Span!.Value.Length));
        Assert.Equal("return", source.Substring(records[1].Span!.Value.Start, records[1].Span!.Value.Length));
    }

    [Fact]
    public void MovedFieldReadUsesTheOwnershipDiagnostic()
    {
        var c = MinimalEmissionTest.Analyze(Resource + "func take(value: Resource) => ()\nlet value = Resource.init()\ntake(value@move)\nlet n = value.value");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
    }

    [Fact]
    public void FieldCleanupReleasesItsOwnedStringExactlyOnce()
    {
        const string Source = "struct S\n    var text: string\n    public init(text: string) => self.text = text@move\n    drop => Console.writeLine(\"drop\")\nlet s = S.init(\"held\")";
        var ir = ScalarEmissionTest.EmitFixture("StructStringRelease", Source, "drop\n");
        StringEmissionTest.WriteAuditedFixture("StructStringRelease", Source, ir, "drop\n", "held=1;drop=1");
    }

    [Fact]
    public void ConstructorAbortSkipsPartialStorageAndDrop()
    {
        const string Source = "struct S\n    var text: string\n    public init()\n        self.text = \"held\"\n        $abort(\"stop\")\n    drop => Console.writeLine(\"drop\")\nlet s = S.init()";
        const string Stderr = "Hello.kimi:5:9: abort KIMI_E_ABORT: stop\n";
        var ir = ScalarEmissionTest.EmitFixture("StructConstructorAbort", Source, string.Empty, 1, Stderr);
        StringEmissionTest.WriteAuditedFixture("StructConstructorAbort", Source, ir, string.Empty, "held=0;stop=0;drop=0", 1, Stderr);
    }

    [Fact]
    public void ExplicitCLayoutUsesItsSelectedRepresentation()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var value: i32\n    public init() => self.value = 0\nlet s = S.init()");
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains("{ i32 }", ir);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindReloadAndWarmPassesPreserveStructPlans()
    {
        var c = MinimalEmissionTest.Analyze(Resource + "let value = Resource.init()\nif value.value == 7 => Console.writeLine(\"ok\")");
        c = CompilationTestHelper.Reload(c);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var success = true;
        var allocated = AllocationMeasurement.Measure(
            () =>
            {
                success &= c.Ownership.Analyze().IsVerified;
                success &= c.Emission.WriteIr(TextWriter.Null, out _);
            },
            iterations: 128,
            warmupIterations: 100);
        Assert.True(success);
        Assert.Equal(0, allocated);
    }
}
