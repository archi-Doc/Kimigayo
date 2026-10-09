// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualApplicabilityDefinitionTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    public void ClosedOverridesKeepDefinitionChecksAndStableSlotPositions(bool block, string type, bool applicable)
    {
        var source = Program(block, type, "2") + "let owner = Derived.init()@obj\nrequire owner.first() == 1 and owner.last() == 3 else => $abort(\"slots\")\n" +
            (applicable ? "require owner.read() == 2 else => $abort(\"override\")" : "()");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        var descriptor = Assert.Single(module.Objects);
        Assert.Equal(3, descriptor.VirtualSlots.Length);
        Assert.Equal(applicable, descriptor.VirtualSlots[1] is not null);
        Assert.NotNull(descriptor.VirtualSlots[2]);
        ScalarEmissionTest.EmitFixture($"VirtualClosedApplicability{block}{type}", source, string.Empty);
    }

    [Theory]
    [InlineData("missing", "UnresolvedBinding_Kd")]
    [InlineData("\"wrong\"", "TypeMismatch_Kd")]
    [InlineData("Console.writeLine(\"effect\")\n        return 2", "UnsatisfiedEffectBound_Kd")]
    [InlineData("let value = \"text\"\n        let copy = value\n        return 2", "TransferRequired_Kd")]
    public void InapplicabilityNeverSuppressesBodyDiagnostics(string body, string code)
    {
        // A failed Binding is a prerequisite of ownership. Exercise acquisition failures in a
        // bound program; Binding failures also retain an independent unresolved Name.
        var ownership = code == "TransferRequired_Kd";
        var source = Program(false, "string", body, statement: body.Contains('\n')) + (ownership ? "()" : "let independent = absent\n");
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        var records = TestDiagnostics.Of(c);
        Assert.Contains(records, x => x.Code == code);
        if (ownership)
        {
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        }
        else
        {
            Assert.Contains(records, x => x.Code == "UnresolvedBinding_Kd" && x.Text == "absent");
        }

        Assert.DoesNotContain(records, x => x.Code is "Unsupported_Kd" or "CheckFaulted_Kd");
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallsAndItemsStillRequireTheOriginalCondition(bool item, bool block)
    {
        var path = Path.GetFullPath("virtual-inapplicable.kimi");
        var source = Program(block, "string", "2") + (item ? "let action = Derived.read" : "let owner = Derived.init()@obj\nlet result = owner.read()");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.DoesNotContain(TestDiagnostics.Of(c), x => x.Code == "Unsupported_Kd");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("Copy", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var shown = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(result.Diagnostics[0].Display!.Range, shown.Range);
            Assert.Contains("Copy", shown.Message, StringComparison.Ordinal);
            output.WriteLine(shown.Message);
        }
    }

    [Fact]
    public void ReplacingTheBaseBindingRebuildsTheApplicability()
    {
        var c = MinimalEmissionTest.Analyze(Program(false, "string", "2"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        var donor = MinimalEmissionTest.Analyze(Program(false, "i32", "2"));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        Assert.True(KotoHelper.Replace(derived, derived.Bases[0], replacement.Bases[0]));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        var implementation = derived.Members.OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out var mapping));
        Assert.Equal(ConstraintProof.Proven, c.Binding.VirtualApplicability(mapping.Slot));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InapplicableDefinitionsReuseAllPhases()
    {
        var c = MinimalEmissionTest.Analyze(Program(false, "string", "2") + "let owner = Derived.init()@obj\nrequire owner.last() == 3 else => $abort(\"slot\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Program(bool block, string type, string body, bool statement = false)
    {
        var slot = block ? "    Self is Marker when T is Copy\n        public virtual func read(self: objref/Self) -> i32\n            effect confined\n            return 1\n"
            : "    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        effect confined\n        return 1\n";
        return "contract Marker\nopen struct Base<T>\n    public init() => ()\n    public virtual func first(self: objref/Self) -> i32 => 1\n" + slot +
            "    public virtual func last(self: objref/Self) -> i32 => 3\n" + $"struct Derived : Base<{type}>\n    public init() => ()\n    override func read(self: objref/Self) -> i32" +
            (statement ? "\n        " : " => ") + body + "\n";
    }
}
