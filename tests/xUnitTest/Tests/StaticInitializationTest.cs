// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StaticInitializationTest
{
    private const string DynamicScalar = "group Values\n    public let value: i64 = initialize()\n    func initialize() -> i64 => 42\nrequire Values.value == 42 else => $abort(\"Value\")";

    [Fact]
    public void GenericCallersShareOneNongenericStaticSlot()
    {
        const string Source = """
            group Values
                public let value: i32 = initialize()
                func initialize() -> i32
                    Console.writeLine("initialize")
                    return 42
            func read<T>(value: T) -> i32 => Values.value
            Console.writeLine("before")
            require read(1) == 42 and read(true) == 42 else => $abort("Value")
            """;
        ScalarEmissionTest.EmitFixture("StaticInitializationGenericCaller", Source, "before\ninitialize\n");
    }

    [Fact]
    public void InitializationOrderFollowsDependencies()
    {
        const string Source = """
            group Values
                public let outer: i32 = initializeOuter()
                let inner: i32 = initializeInner()
                func initializeOuter() -> i32
                    Console.writeLine("outer begins")
                    let value = inner
                    Console.writeLine("outer completes")
                    return value + 1
                func initializeInner() -> i32
                    Console.writeLine("inner")
                    return 41
            require Values.outer == 42 else => $abort("Value")
            """;
        ScalarEmissionTest.EmitFixture("StaticInitializationOrder", Source, "outer begins\ninner\nouter completes\n");
    }

    [Theory]
    [InlineData("bool", "true", "Boolean")]
    [InlineData("f64", "1.25", "Float")]
    [InlineData("u64", "18446744073709551615", "Unsigned")]
    [InlineData("i128", "170141183460469231731687303715884105727", "Wide")]
    public void InitializesScalarRepresentations(string type, string value, string name)
    {
        var source = "group Values\n    public let value = initialize()\n    func initialize() -> " + type + " => " + value + "\nrequire Values.value == " + value + " else => $abort(\"Value\")";
        ScalarEmissionTest.EmitFixture("StaticInitialization" + name, source, string.Empty);
    }

    [Fact]
    public void RebindingAndReloadKeepInitializerViewsCurrent()
    {
        var c = MinimalEmissionTest.Analyze(DynamicScalar);
        var original = CompilationTestHelper.WriteIr(c);
        var restored = CompilationTestHelper.Reload(c);
        foreach (var compilation in new[] { c, restored })
        {
            compilation.Bind();
            compilation.Binding.CheckStartup(OutputKind.Application);
            Assert.True(compilation.Ownership.Analyze().IsVerified);
            Assert.Equal(original, CompilationTestHelper.WriteIr(compilation));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAnalysisAndEmissionReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze(DynamicScalar);
        var valid = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                valid &= c.Ownership.Analyze().IsVerified;
                valid &= c.Emission.WriteIr(TextWriter.Null, out _);
            },
            iterations: 128,
            warmupIterations: 100);
        Assert.True(valid);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void InitializesOnceAtFirstRead()
    {
        const string Source = """
            group Values
                public let value: i64 = initialize()
                func initialize() -> i64
                    Console.writeLine("initialize")
                    return 42
            Console.writeLine("before")
            require Values.value == 42 else => $abort("Value")
            require Values.value == 42 else => $abort("Value")
            """;
        ScalarEmissionTest.EmitFixture("StaticInitializationOnce", Source, "before\ninitialize\n");
    }

    [Fact]
    public void DoesNotExecuteUnusedInitializer()
    {
        const string Source = """
            group Values
                public let value: i64 = initialize()
                func initialize() -> i64
                    $abort("Unused initializer")
            Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("StaticInitializationUnused", Source, "done\n");
    }

    // SPEC 22.2.3, 22.5.4: reading a slot that is Initializing Aborts at the start of that read's Place expression, qualifier
    // included, neither at the declaration nor at the first access.
    [Theory]
    [InlineData("StaticInitializationCycle", "")]
    [InlineData("StaticInitializationCycleQualified", "Values.")]
    public void DetectsIndirectCycle(string name, string qualifier)
    {
        var source = $"group Values\n    public let first: i64 = {qualifier}second + 1\n    public let second: i64 = {qualifier}first + 1\nlet value = Values.first";
        ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, "Hello.kimi:3:30: abort KIMI_E_STATIC_CYCLE: Static initialization cycle\n");
    }
}
