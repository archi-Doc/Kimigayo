// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class ConstraintRecoveryTest(ITestOutputHelper output)
{
    private const string Prefix = "contract C\nstruct S<T>\n    Self is C when ";

    [Theory]
    [InlineData("T")]
    [InlineData("T Copy")]
    [InlineData("T is Copy, T")]
    [InlineData("T, T is Copy")]
    public void MissingIsNeverDiscardsAConditionalPremise(string conditions)
    {
        var source = Prefix + conditions;
        var tree = Parse(source + "\nlet next = 1");
        var error = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Contains("'is'", error.Message, StringComparison.Ordinal);
        Assert.Equal(conditions.EndsWith('T') ? "MissingSyntax_Kd" : "ExpectedSyntax_Kd", error.Code);
        var conditional = Assert.Single(KotoTree.Walk(tree.RootKoto).OfType<SyntaxFormKoto>(), x => x.Akind == KotoKind.ConditionalConformance && x.Parent is StructKoto);
        var premises = Assert.IsType<SyntaxFormKoto>(conditional.Operands[1]);
        Assert.Equal(conditions.Contains(',') ? 2 : 1, premises.Operands.Length);
        var recovered = premises.Operands[conditions.StartsWith("T is Copy", StringComparison.Ordinal) ? 1 : 0];
        Assert.NotNull(recovered.CodeContext.RecoveryCause(recovered));
        Assert.Equal("let next = 1", tree.GeneratedFunction!.Body!.Items.Last().ToString());
        VerifyParents(tree.RootKoto);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("T Copy")]
    [InlineData("T is Copy, T")]
    public void RecoveredConditionsCannotGrantConformanceOrHideIndependentErrors(string conditions)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + conditions + "\nfunc use(x: S<i32>) => ()\nlet wrong: i32 = true");
        c.Binding.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c);
        Assert.Equal([conditions.EndsWith('T') ? "MissingSyntax_Kd" : "ExpectedSyntax_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "use");
        var contract = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ContractKoto>().Single(x => x.Name == "C").BoundSymbol!;
        Assert.Equal(ConstraintProof.Error, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, contract, use, out var path));
        Assert.Null(path);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, TestDiagnostics.Of(c));
    }

    [Theory]
    [InlineData("T is Copy")]
    [InlineData("T is Copy, T is Owned")]
    public void CompleteConditionsKeepTheirOrdinaryMeaning(string conditions)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + conditions + "\nfunc take<U>(x: U)\n    U is C\n    ()\nfunc use(x: S<i32>) => take(x@move)");
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Fact]
    public void MissingIsIsLocatedConsistentlyInCliAndLsp()
    {
        var path = Path.GetFullPath("constraint.kimi");
        var source = Prefix + "T is Copy, T\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["MissingSyntax_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var error = result.Diagnostics[0];
        Assert.Equal(new SourceSpan(Prefix.Length + "T is Copy, T".Length, 0), error.Span);
        Assert.Equal("Missing 'is'", error.Message);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("constraint.kimi:3:32", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), sent.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
