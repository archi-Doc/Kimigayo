// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualGenericDispatchTest(ITestOutputHelper output)
{
    private const string Hierarchy = """
        open struct Base<T>
            T is Owned
            public init() => ()
            public virtual func read(self: objref/Self, bias: i32 = 1) -> i32 => bias
            public virtual func nested(self: objref/Self) -> i32 => self.read(2) + 100
        open struct Middle<T> : Base<T>
            T is Owned
            public init() => ()
            override func read(self: objref/Self, bias: i32) -> i32 => base.read(bias) + 10
        struct Leaf<T> : Middle<T>
            T is Owned
            public init() => ()
            override func read(self: objref/Self, bias: i32) -> i32 => base.read(bias) + 20
            public func direct(self: objref/Self) -> i32 => base.read()

        """;

    [Fact]
    public void ClosedTablesRetainGenericOverridesWithoutDirectCalls()
    {
        const string Source = Hierarchy + "let a = Leaf<i32>.init()@obj\nlet b = Leaf<string>.init()@rc\nrequire a.read() == 31 and b.nested() == 132 and a.direct() == 11 else => $abort(\"dispatch\")\nlet f = Leaf<i32>.read\nrequire f(a@objref/Base<i32>, 3) == 33 else => $abort(\"item\")\nlet erased: (objref/Base<string>, i32) -> i32 = Leaf<string>.read\nrequire erased(b@objref/Base<string>, 4) == 34 else => $abort(\"erased\")";
        ScalarEmissionTest.EmitFixture("VirtualGenericDispatch", Source, string.Empty);
    }

    [Fact]
    public void ReceiverAbiFollowsTheClosedInputAndResultTypes()
    {
        const string Source = "open struct Base<T>\n    T is Owned\n    public init() => ()\n    public virtual func take(value: T, self: objref/Self) -> T => value@move\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func take(value: T, self: objref/Self) -> T\n        Console.writeLine(\"override\")\n        return value@move\nlet a = Leaf<()>.init()@obj\nlet b = Leaf<string>.init()@arc\na.take(())\nlet f: (string, objref/Base<string>) -> string = Leaf<string>.take\nrequire f(\"payload\", b@objref/Base<string>) == \"payload\" else => $abort(\"result\")";
        ScalarEmissionTest.EmitFixture("VirtualGenericDispatchAbi", Source, "override\noverride\n");
    }

    [Fact]
    public void AFactoryItemDiscoveredInsideAnInstanceRetainsItsTableBodies()
    {
        const string Source = Hierarchy + "func build<T>() -> obj/Leaf<T>\n    T is Owned\n    let factory = Kimi.Intrinsics.makeObj<Leaf<T>>\n    return factory(Leaf<T>.init())\nlet a = build<i32>()\nrequire a.nested() == 132 else => $abort(\"factory item\")";
        ScalarEmissionTest.EmitFixture("VirtualGenericDispatchFactory", Source, string.Empty);
    }

    [Fact]
    public void DescriptorDependenciesReuseFiniteCyclesAndRejectGrowingKeys()
    {
        var finite = DependencyProgram("i32");
        ScalarEmissionTest.EmitFixture("VirtualGenericDispatchCycle", finite, string.Empty);
        var path = Path.GetFullPath("virtual-growing-table.kimi");
        var c = MinimalEmissionTest.Analyze(DependencyProgram("Next<T>"), path);
        using var ir = new StringWriter();
        Assert.False(c.Emission.WriteIr(ir, out var failure));
        Assert.Empty(ir.ToString());
        Assert.True(c.Emission.FailureIsResourceLimit, failure);
        Assert.Contains("substitutions", failure, StringComparison.Ordinal);
        c.Emission.ReportFailure(failure);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "GenerationResourceLimit_Kd");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(last: DiagnosticPartition.Emission);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Resource, record.Category);
        Assert.Contains("generated dependencies", record.Note, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Contains("generated dependencies", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void UnreferencedGenericSlotsNeedNoConcreteInstance()
    {
        var c = MinimalEmissionTest.Analyze(Hierarchy + "()");
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        Assert.Empty(module.Objects);
        Assert.DoesNotContain(Enumerable.Range(0, module.FunctionCount), i => module.GetFunction(i).Abi.Name.StartsWith("__kimi_generic_entry", StringComparison.Ordinal));
    }

    [Fact]
    public void AReplacedGenericBodyAddsNoGenerationDependencies()
    {
        const string Source = "struct Next<T>\n    T is Owned\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n    public virtual func read(self: objref/Self) -> i32\n        let child = Base<Next<T>>.init()@obj\n        return 1\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func read(self: objref/Self) -> i32 => 7\nlet a = Leaf<string>.init()@obj\nrequire a.read() == 7 else => $abort(\"replaced\")";
        ScalarEmissionTest.EmitFixture("VirtualGenericDispatchReplaced", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmClosedTablesReuseGenericEntries()
    {
        var c = MinimalEmissionTest.Analyze(Hierarchy + "let a = Leaf<i32>.init()@obj\nrequire a.nested() == 132 else => $abort(\"dispatch\")");
        Assert.True(c.Emission.Validate(out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string DependencyProgram(string argument)
        => "struct Next<T>\n    T is Owned\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func read(self: objref/Self) -> i32\n        let child = Leaf<" + argument + ">.init()@obj\n        return 7\nlet a = Leaf<string>.init()@obj\nrequire a.read() == 7 else => $abort(\"cycle\")";
}
