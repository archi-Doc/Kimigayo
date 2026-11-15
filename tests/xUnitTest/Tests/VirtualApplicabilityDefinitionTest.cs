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
    // Where the slot is refuted (`string is Copy`), Derived declares no override: SPEC 6.4.3 makes one a declaration error (the owner's
    // O-370; AnOverrideOfASlotInAnInapplicableBlockIsADeclarationError, VirtualOverrideConditionTest), and the slots around it keep their
    // positions.
    [Theory]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    public void ClosedOverridesKeepDefinitionChecksAndStableSlotPositions(bool block, string type, bool applicable)
    {
        var source = Program(block, type) + (applicable ? Override("2") : string.Empty) +
            "let owner = Derived.init()@obj\nrequire owner.first() == 1 and owner.last() == 3 else => $abort(\"slots\")\n" +
            (applicable ? "require owner.read() == 2 else => $abort(\"override\")" : "()");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture($"VirtualClosedApplicability{block}{type}", source, string.Empty);
    }

    // SPEC 6.4.3 (O-370): an override of a slot declared in a conditional-conformance Block whose condition is Refuted for the derived Type
    // (`string is Copy`) is one declaration error at the override (the Block form of ClosedOverrides...'s former refuted rows).
    [Fact]
    public void AnOverrideOfASlotInAnInapplicableBlockIsADeclarationError()
        => Assert.Equal([DiagnosticCode.InvalidConstraint_Kd], MinimalEmissionTest.Analyze(Program(true, "string") + Override("2") + "()").Binding.Issues.Select(static x => x.Code));

    // SPEC 6.4.3 (O-370): the override of the refuted slot is one declaration error; its body's Binding-time records stand beside it (SPEC
    // 23.1.2), with the program's independent unresolved Name. A rejected definition publishes no body (DECISIONS R4-M18), so the records
    // read from its published body (the effect bound's, the bare move's TransferRequired_Kd, R6-check H1) are dependents of the rejection.
    [Theory]
    [InlineData("missing", "UnresolvedBinding_Kd")]
    [InlineData("\"wrong\"", "TypeMismatch_Kd")]
    [InlineData("Console.writeLine(\"effect\")\n        return 2", null)]
    [InlineData("let value = \"text\"\n        let copy = value\n        return 2", null)]
    public void ARejectedOverrideKeepsItsBodysBindingRecords(string body, string? code)
    {
        var source = Program(false, "string") + Override(body, statement: body.Contains('\n')) + "let independent = absent\n";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        string[] expected = code is null ? ["InvalidConstraint_Kd", "UnresolvedBinding_Kd"] : ["InvalidConstraint_Kd", code, "UnresolvedBinding_Kd"];
        Assert.Equal(expected.Order(), TestDiagnostics.Of(c).Select(static x => x.Code).Order());
        Assert.False(c.Generate(TextWriter.Null));
    }

    // Derived declares no override of the refuted slot (one is a declaration error, SPEC 6.4.3): the call's or Item's record is the one.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallsAndItemsStillRequireTheOriginalCondition(bool item, bool block)
    {
        var path = Path.GetFullPath("virtual-inapplicable.kimi");
        var source = Program(block, "string") + (item ? "let action = Derived.read" : "let owner = Derived.init()@obj\nlet result = owner.read()");
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

    // The refuted slot's override is a declaration error (SPEC 6.4.3); replacing the base binding with one that proves the slot clears it.
    [Fact]
    public void ReplacingTheBaseBindingRebuildsTheApplicability()
    {
        var c = MinimalEmissionTest.Analyze(Program(false, "string") + Override("2"));
        Assert.Equal([DiagnosticCode.InvalidConstraint_Kd], c.Binding.Issues.Select(static x => x.Code));
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        var donor = MinimalEmissionTest.Analyze(Program(false, "i32") + Override("2"));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        Assert.True(KotoHelper.Replace(derived, derived.Bases[0], ParseTestHelper.Detach(replacement.Bases[0], new CodeBlockKoto(derived.CodeContext))));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var implementation = derived.Members.OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out var mapping));
        Assert.Equal(ConstraintProof.Proven, c.Binding.VirtualApplicability(mapping.Slot));
    }

    // The refuted slot's original stays an inapplicable definition; Derived declares no override of it (SPEC 6.4.3).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InapplicableDefinitionsReuseAllPhases()
    {
        var c = MinimalEmissionTest.Analyze(Program(false, "string") + "let owner = Derived.init()@obj\nrequire owner.last() == 3 else => $abort(\"slot\")");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }

    // The base with its three slots and `struct Derived : Base<type>`; Override appends the override of `read`.
    private static string Program(bool block, string type)
    {
        var slot = block ? "    Self is Marker when T is Copy\n        public virtual func read(self: objref/Self) -> i32\n            effect confined\n            return 1\n"
            : "    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        effect confined\n        return 1\n";
        return "contract Marker\nopen struct Base<T>\n    public init() => ()\n    public virtual func first(self: objref/Self) -> i32 => 1\n" + slot +
            "    public virtual func last(self: objref/Self) -> i32 => 3\n" + $"struct Derived : Base<{type}>\n    public init() => ()\n";
    }

    private static string Override(string body, bool statement = false)
        => "    override func read(self: objref/Self) -> i32" + (statement ? "\n        " : " => ") + body + "\n";
}
