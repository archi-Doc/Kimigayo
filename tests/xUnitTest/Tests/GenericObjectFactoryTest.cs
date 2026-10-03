// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class GenericObjectFactoryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Obj", "obj", false)]
    [InlineData("Rc", "rc", false)]
    [InlineData("Obj", "obj", true)]
    [InlineData("Rc", "rc", true)]
    public void GenericFactoriesHaveLocatedUnsupportedRecords(string factory, string mode, bool independent)
    {
        var source = $"func box<T>(value: T) -> {mode}/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.make{factory}(value@move)\n" +
            $"func forward<T>(value: T) -> {mode}/T\n    T is ObjectPayload\n    return box(value@move)\n" +
            "func read(value: ref/i32) -> i32 => value\nfunc readFlag(value: ref/bool) -> bool => value\n" +
            "let number = forward(7)\nlet flag = box(true)\nrequire read(number@follow@ref) == 7 and readFlag(flag@follow@ref) else => $abort(\"payload\")";
        if (independent)
        {
            source += "\nlet first = Kimi.Intrinsics.makeObj(1)\nlet moved = first@move\nlet again = first@move";
        }

        var path = Path.GetFullPath("generic-factory.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics, x => x.Code == "UnsupportedOwnership_Kd");
        Assert.Equal($"Kimi.Intrinsics.make{factory}(value@move)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "GenerationFailed_Kd");
        Assert.Equal(independent, result.Diagnostics.Any(x => x.Code == "MovedPlace_Kd"));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Object factories in generic function or container bodies are not implemented", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            var diagnostic = Assert.Single(sent, x => x.Message.Contains("Object factories in generic function or container bodies", StringComparison.Ordinal));
            Assert.Equal(error.Display!.Range, diagnostic.Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(diagnostic));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    public void NongenericFactoriesRemainExecutable(string factory)
        => NativeAllocationAudit.WriteFixture("GenericObjectCounterpart" + factory, $"func box(value: i32) => Kimi.Intrinsics.make{factory}(value)\nlet boxed = box(7)", 1, 1, 20);

    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    public void ContainerParametersAlsoRequireConcreteFactoryPlans(string factory)
    {
        var c = MinimalEmissionTest.Analyze($"struct Box<T>\n    public func create(self: ref/Self) => Kimi.Intrinsics.make{factory}(1)\nlet box = Box<i32>.init()\nlet value = box.create()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Equal($"Kimi.Intrinsics.make{factory}(1)", issue.Source.ToString());
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericCloneUsesTheSubstitutedHandleModeAndExternalOrigins()
    {
        const string Source = """
            struct View {source}
                public let value: ref/i32 during source
                public init(value: ref/i32 during source) => self.value = value
            func duplicate<T>(value: ref/rc/T) -> rc/T
                T is ObjectPayload
                return Kimi.Intrinsics.clone(value)
            let n = 7
            let copy = label result: do
                let first = Kimi.Intrinsics.makeRc(View.init(n@ref))
                exit to result duplicate(first@ref)
            require copy.value == 7 else => $abort("result")
            """;
        NativeAllocationAudit.WriteFixture("GenericObjectCloneRc", Source, 1, 1, 24);
    }
}
