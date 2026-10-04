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
    [InlineData("Arc", "arc", false)]
    [InlineData("Arc", "arc", true)]
    public void GenericFactoriesExecuteWithIndependentErrorsPreserved(string factory, string mode, bool independent)
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
        var emitted = c.Emission.Validate(out var failure);
        Assert.True(emitted == !independent, MinimalEmissionTest.Describe(c, failure));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "GenerationFailed_Kd");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        if (!independent)
        {
            Assert.Empty(result.Diagnostics);
            NativeAllocationAudit.WriteFixture("GenericObjectFactory" + factory, source, 2, 2, 37);
            return;
        }

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("MovedPlace_Kd", error.Code);
        Assert.Equal("first", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            var diagnostic = Assert.Single(sent);
            Assert.Equal(error.Display!.Range, diagnostic.Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(diagnostic));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void NongenericFactoriesRemainExecutable(string factory)
        => NativeAllocationAudit.WriteFixture("GenericObjectCounterpart" + factory, $"func box(value: i32) => Kimi.Intrinsics.make{factory}(value)\nlet boxed = box(7)", 1, 1, 20);

    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void ContainerParametersUseConcreteFactoryPlans(string factory)
    {
        var source = $"struct Box<T>\n    public func create(self: ref/Self) -> {factory.ToLowerInvariant()}/i32 => Kimi.Intrinsics.make{factory}(1)\nlet box = Box<i32>.init()\nlet value = box.create()\nrequire value@follow == 1 else => $abort(\"payload\")";
        NativeAllocationAudit.WriteFixture("GenericObjectContainer" + factory, source, 1, 1, 20);
    }

    [Theory]
    [InlineData("Payload", "let root = Kimi.Intrinsics.makeRc(Outer<i32>.init())", 3, 52)]
    [InlineData("Local", "let root = Outer<i32>.init()", 2, 36)]
    public void DiscoveredDestructorsMayIntroduceFurtherFactories(string name, string root, int allocations, long bytes)
    {
        const string Types = """
            struct Inner<T>
                drop
                    let final = Kimi.Intrinsics.makeRc(3)
                    Console.writeLine("inner")
            struct Outer<T>
                drop
                    let middle = Kimi.Intrinsics.makeObj(Inner<T>.init())
                    Console.writeLine("outer")
            """;
        NativeAllocationAudit.WriteFixture("GenericObjectDestructorRc" + name, Types + "\n" + root, allocations, allocations, bytes, "outer\ninner\n");
        NativeAllocationAudit.WriteFixture("GenericObjectDestructorArc" + name, SharedObjectRuntimeTest.ArcSource(Types + "\n" + root), allocations, allocations, bytes, "outer\ninner\n");
    }

    [Fact]
    public void FactoriesKeepDestructorExpansionBounded()
    {
        const string Source = """
            struct Marker<T>
            struct Expanding<T>
                drop
                    let next = Kimi.Intrinsics.makeObj(Expanding<Marker<T>>.init())
            let root = Expanding<i32>.init()
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var ir = new StringWriter();
        Assert.False(c.Emission.WriteIr(ir, out var failure));
        Assert.True(c.Emission.FailureIsResourceLimit, failure);
        Assert.Contains("destruction", failure, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ir.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmGenericFactoryPlansAllocateNothing(bool atomic)
    {
        const string Source = "func box<T>(value: T) -> rc/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.makeRc(value@move)\nlet first = box(7)\nlet second = box(true)";
        var c = MinimalEmissionTest.Analyze(atomic ? SharedObjectRuntimeTest.ArcSource(Source) : Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedPhysicalFactoriesKeepEachCallsExternalLoans(bool conflict, bool atomic)
    {
        var source = """
            struct View {source}
                public let value: ref/i32 during source
                public init(value: ref/i32 during source) => self.value = value
            func box<T>(value: T) -> rc/T
                T is ObjectPayload
                return Kimi.Intrinsics.makeRc(value@move)
            var first = 7
            let second = 9
            let a = box(View.init(first@ref))
            let b = box(View.init(second@ref))
            """ + (conflict ? "\nfirst = 1" : string.Empty) + "\nrequire a.value == 7 and b.value == 9 else => $abort(\"origins\")";
        source = atomic ? SharedObjectRuntimeTest.ArcSource(source) : source;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (conflict)
        {
            Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
            Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
            Assert.False(c.Emission.Validate(out _));
        }
        else
        {
            Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
            Assert.Single(module.Objects);
            NativeAllocationAudit.WriteFixture("GenericObjectFactoryOrigins" + (atomic ? "Arc" : "Rc"), source, 2, 2, 48);
        }
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
        NativeAllocationAudit.WriteFixture("GenericObjectCloneArc", SharedObjectRuntimeTest.ArcSource(Source), 1, 1, 24);
    }
}
