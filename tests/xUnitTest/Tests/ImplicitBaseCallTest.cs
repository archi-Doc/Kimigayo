// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ImplicitBaseCallTest
{
    [Fact]
    public void OmittedBaseCallUsesOrdinaryDefaultsBeforeOwnInitializers()
    {
        const string Source = """
            group Helpers
                public func make() -> i32
                    Console.writeLine("default")
                    return 42
                public func own() -> i32
                    Console.writeLine("field")
                    return 7
            open struct Base<T>
                public let item: i32
                protected init(item: i32 = Helpers.make())
                    self.item = item
                    Console.writeLine("base")
                drop => Console.writeLine("base drop")
            open struct Middle<U>: Base<U>
                protected init() => Console.writeLine("middle")
            struct Leaf: Middle<i64>
                public let extra: i32 = Helpers.own()
                public init() => Console.writeLine("leaf")
                drop => Console.writeLine("leaf drop")
            let x = Leaf.init()
            require x.item == 42 and x.extra == 7 else => $abort("initialization")
            """;
        ScalarEmissionTest.EmitFixture("ImplicitBaseDefaults", Source, "default\nbase\nmiddle\nfield\nleaf\nleaf drop\nbase drop\n");
    }

    [Theory]
    [InlineData("protected init(value: i32) => ()", "NoApplicableOverload_Kd")]
    [InlineData("private init() => ()", "NoApplicableOverload_Kd")]
    [InlineData("protected init(value: i32 = 1) => ()\n    protected init(value: string = \"x\") => ()", "AmbiguousBinding_Kd")]
    public void OmissionPreservesBaseSelectionFailures(string declaration, string code)
    {
        var source = "open struct Base\n    " + declaration + "\nstruct Leaf: Base\n    public init() => ()\n()";
        var result = DiagnosticCorpus.Check(source);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal("init()", source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.NotEmpty(record.Related!);
        Assert.NotNull(record.Reason);
        var explicitRecord = Assert.Single(DiagnosticCorpus.Check(source.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal)).Diagnostics);
        Assert.Equal((record.Code, record.Label, record.Message), (explicitRecord.Code, explicitRecord.Label, explicitRecord.Message));
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
    [InlineData("")]
    [InlineData(": base()")]
    public void AccessibleZeroArgumentBaseHasOneInvocation(string clause)
    {
        var source = "open struct Base\n    protected init() => Console.writeLine(\"base\")\nstruct Leaf: Base\n    public init()" + clause + " => ()\nlet x = Leaf.init()";
        ScalarEmissionTest.EmitFixture("ImplicitBaseSingle" + clause.Length, source, "base\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindingReusesTheImplicitCallWithoutChangingWrittenSyntax()
    {
        const string Source = "open struct Base\n    protected init(value: i32 = 42) => ()\nstruct Leaf: Base\n    public init() => ()\nlet x = Leaf.init()";
        var c = MinimalEmissionTest.Analyze(Source);
        var constructor = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.IsConstructor);
        var initializer = constructor.BaseInitializer;
        Assert.NotNull(initializer);
        Assert.DoesNotContain(": base", constructor.ToString(), StringComparison.Ordinal);
        Assert.Equal(constructor.SignatureSpan, initializer.Span);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.Same(initializer, constructor.BaseInitializer);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }
}
