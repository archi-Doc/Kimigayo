// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class EmissionPlanTest
{
    [Fact]
    public void LinearBodyContainsOnlyPhysicalOperations()
    {
        var c = MinimalEmissionTest.Analyze("writeLine(\"Hello, world!\")");
        Assert.True(c.Emission.TryPrepare(out var plan, out var error), error);
        var slot = Assert.Single(plan.Slots);
        Assert.Equal(new[] { 0, 8, 16 }, slot.Value.Layout.FieldOffsets.ToArray());
        Assert.Equal((24, 8, 24), (slot.Value.Layout.Size, slot.Value.Layout.Alignment, slot.Value.Layout.Stride));
        Assert.Equal(2, plan.Instructions.Count);
        Assert.True(plan.Instructions[0].Operation < plan.Instructions[1].Operation);
        Assert.Same(WindowsLowering.WriteLine, plan.Instructions[1].Callee);
        Assert.Equal(slot.Place, plan.Instructions[1].Place);
        using var output = new StringWriter();
        plan.WriteIr(output);
        var ir = output.ToString();
        var start = ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal);
        var end = ir.IndexOf("define void @__kimi_start", start, StringComparison.Ordinal);
        Assert.Equal($"define internal void @__kimi_entry_body() #0 {{\nentry:\n  %p{slot.Place} = alloca %kimi.string, align 8\n  store %kimi.string {{ ptr @__kimi_text, i64 13, i8 0 }}, ptr %p{slot.Place}, align 8\n  call void @__kimi_write_line(ptr %p{slot.Place}, ptr @__kimi_location, i64 14)\n  ret void\n}}\n", ir[start..end]);
    }

    [Theory]
    [InlineData("missing-successor")]
    [InlineData("cycle")]
    [InlineData("branch")]
    [InlineData("abort-resumes")]
    [InlineData("missing-cleanup-step")]
    [InlineData("missing-cleanup-plan")]
    [InlineData("conditional-cleanup")]
    public void UnsupportedOrIncompleteLoweringNeverWritesFallbackIr(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("writeLine(\"a\")");
        Assert.True(c.Emission.Validate(out _)); // Exercise reuse after a previously successful plan.
        var body = c.Ownership.Bodies[0];
        var firstEdge = body.EdgeHeads[0];
        // Mutate the continuation after argument acquisition, preserving the converged
        // input state queried by the gate. The test targets lowering, not a forged solver.
        var deliver = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Deliver);
        var terminalEdge = body.EdgeHeads[deliver];
        switch (mutation)
        {
            case "missing-successor":
                body.EdgeHeads[deliver] = -1;
                break;
            case "cycle":
                body.EdgeStorage[terminalEdge] = body.Edges[terminalEdge] with { To = deliver };
                break;
            case "branch":
                body.EdgeStorage[firstEdge] = body.Edges[firstEdge] with { Kind = OwnershipEdgeKind.True };
                break;
            case "abort-resumes":
                var abort = body.EdgeStorage.FindIndex(x => x.Kind == OwnershipEdgeKind.Abort);
                body.EdgeStorage[abort] = body.Edges[abort] with { To = 0 };
                break;
            case "missing-cleanup-step":
                body.OperationSteps[body.CleanupSteps[0].Operation] = -1;
                break;
            case "missing-cleanup-plan":
                body.CleanupPlanStorage.Clear();
                break;
            case "conditional-cleanup":
                body.CleanupStepStorage[0] = body.CleanupSteps[0] with { Action = CleanupAction.Conditional };
                break;
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out var error));
        Assert.NotNull(error);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(c.Emission.TryPrepare(out var rejected, out _));
        Assert.Throws<InvalidOperationException>(() => rejected.WriteIr(output));
    }

    [Fact]
    public void ReanalysisReusesThePlanWithoutDuplicatingSlotsOrInstructions()
    {
        var c = MinimalEmissionTest.Analyze("writeLine(\"日本語\\0\")");
        Assert.True(c.Emission.TryPrepare(out var first, out _));
        using var before = new StringWriter();
        first.WriteIr(before);
        c.Bind();
        Assert.False(c.Emission.Validate(out _));
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.TryPrepare(out var second, out var error), error);
        Assert.Same(first, second);
        using var after = new StringWriter();
        second.WriteIr(after);
        Assert.Equal(before.ToString(), after.ToString());
    }

    [Fact]
    public void RuntimeDefinitionsAndCallsUseThePreparedAbi()
    {
        var c = MinimalEmissionTest.Analyze("writeLine(\"a\")");
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), error);
        var ir = output.ToString();
        foreach (var abi in new[] { WindowsLowering.Entry, WindowsLowering.Exit, WindowsLowering.WriteLine, WindowsLowering.DestroyString })
        {
            var signature = new StringBuilder();
            abi.AppendDefinition(signature);
            Assert.Contains(signature.ToString(), ir);
        }

        Assert.Null(WindowsLowering.Unit.ArgumentType);
        Assert.Null(WindowsLowering.GetValue(BoundType.I32));
        Assert.DoesNotContain("{{", ir);
        Assert.Throws<InvalidOperationException>(() => WindowsLowering.WriteLine.AppendCall(new(), []));
    }

    [Fact]
    public void ConstantEncodingChangesLengthAndRetainsNulAndSupplementaryCharacters()
    {
        var constant = new LlvmConstant("test");
        var text = new StringBuilder();
        constant.SetValue("😀\0");
        constant.AppendTo(text);
        Assert.Equal(5, constant.Length);
        Assert.Equal("@test = private unnamed_addr constant [5 x i8] c\"\\F0\\9F\\98\\80\\00\", align 1\n", text.ToString());
        text.Clear();
        constant.SetValue(string.Empty);
        constant.AppendTo(text);
        Assert.Equal(0, constant.Length);
        Assert.Equal("@test = private unnamed_addr constant [0 x i8] c\"\", align 1\n", text.ToString());
    }
}
