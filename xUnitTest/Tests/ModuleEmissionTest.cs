// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ModuleEmissionTest
{
    [Theory]
    [InlineData("Explicit", "public func main() => Lib.Api.run()")]
    [InlineData("Implicit", "Lib.Api.run()")]
    public void EmitsCommonModuleWithOneStartup(string name, string root)
    {
        var c = ModuleBindingTest.Create(root, "public group Api\n    public func run() => Child.Api.run()", "public group Api\n    public func run() => Console.writeLine(\"child\")");
        var ir = Emit(c);
        ScalarEmissionTest.WriteFixture("Module" + name, ir, "child\n");
    }

    [Theory]
    [InlineData("Owned", "let text = Lib.Api.make()\nLib.Api.take(text)", "public group Api\n    public func make() -> string => \"owned\"\n    public func take(text: string) => Console.writeLine(text)", "owned\n")]
    [InlineData("Generic", "require Lib.Api.keep(42) == 42 else => $abort(\"generic\")", "public group Api\n    public func keep<T>(value: T) -> T => value", "")]
    [InlineData("Default", "require Lib.Api.add(40) == 42 else => $abort(\"default\")", "public group Api\n    public func add(value: i32, extra: i32 = 2) -> i32 => value + extra", "")]
    [InlineData("Main", "require Lib.main(41) == 42 else => $abort(\"library main\")", "public func main(value: i32) -> i32 => value + 1", "")]
    [InlineData("Static", "require Lib.Api.answer == 42 else => $abort(\"static\")", "public group Api\n    public let answer: i32 = 42", "")]
    public void EmitsDependencyOperations(string name, string root, string library, string output)
    {
        var ir = Emit(ModuleBindingTest.Create(root, library));
        ScalarEmissionTest.WriteFixture("Module" + name, ir, output);
    }

    [Fact]
    public void DependencyAbortRetainsSourceLocation()
    {
        var ir = Emit(ModuleBindingTest.Create("Lib.Api.fail()", "public group Api\n    public func fail() => $abort(\"dependency\")"));
        ScalarEmissionTest.WriteFixture("ModuleAbort", ir, string.Empty, 1, "library.kimi:2:27: abort KIMI_E_ABORT: dependency\n");
    }

    [Fact]
    public void DependencyRebindRequiresFreshVerification()
    {
        var c = ModuleBindingTest.Create("Lib.Api.run()", "public group Api\n    public func run() => ()");
        var first = Emit(c);
        Assert.True(c.Bind().IsComplete);
        using var rejected = new StringWriter();
        Assert.False(c.Emission.WriteIr(rejected, out _));
        Assert.Empty(rejected.ToString());
        Assert.Equal(first, Emit(c));
        c.SourceModules[1].AddSource(new SourceDocument("bad.kimi", "public func unused() => missing()"));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Emission.WriteIr(rejected, out _));
        Assert.Empty(rejected.ToString());
    }

    [Theory]
    [InlineData("public func unused()\n    let text = \"moved\"\n    Console.writeLine(text)\n    Console.writeLine(text)")]
    [InlineData("public group State\n    public var counter: i32 = 0")]
    [InlineData("Console.writeLine(\"runtime\")")]
    public void RejectsInvalidOrUnsupportedUnusedDependency(string library)
    {
        var c = ModuleBindingTest.Create("public func main() => ()", library);
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void DependencyDiagnosticsBlockPreviouslyVerifiedEmission()
    {
        var c = ModuleBindingTest.Create("Lib.Api.run()", "public group Api\n    public func run() => ()");
        Emit(c);
        c.SourceModules[1].DiagnosticCollection.Add(default, DiagnosticCode.LibraryRuntimeBody_Kd);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void WarmModuleEmissionReusesStorage()
    {
        var c = ModuleBindingTest.Create("Lib.Api.run()", "public group Api\n    public func run() => ()");
        Emit(c);
        void Write()
        {
            if (!c.Emission.WriteIr(TextWriter.Null, out var failure))
            {
                throw new InvalidOperationException(failure);
            }
        }

        for (var i = 0; i < 100; i++)
        {
            Write();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Write));
    }

    private static string Emit(Compilation c)
    {
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var failure), MinimalEmissionTest.Describe(c, failure));
        return writer.ToString();
    }
}
