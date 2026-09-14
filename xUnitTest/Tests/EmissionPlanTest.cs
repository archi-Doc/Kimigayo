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
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        Assert.Equal(2, module.FunctionCount);
        var entry = module.GetFunction(0);
        Assert.Same(WindowsLowering.Entry, entry.Abi);
        Assert.False(entry.Exported);
        var slot = Assert.Single(entry.Slots);
        Assert.Equal(new[] { 0, 8, 16 }, slot.Value.Layout.FieldOffsets.ToArray());
        Assert.Equal((24, 8, 24), (slot.Value.Layout.Size, slot.Value.Layout.Alignment, slot.Value.Layout.Stride));
        Assert.Equal([EmissionOpcode.Branch, EmissionOpcode.Label, EmissionOpcode.StoreStaticString, EmissionOpcode.Call, EmissionOpcode.ReturnVoid], entry.Instructions.Select(x => x.Opcode));
        Assert.True(entry.Instructions[2].Operation < entry.Instructions[3].Operation);
        Assert.Same(WindowsLowering.WriteLine, entry.Instructions[3].Callee);
        Assert.Equal(slot.Place, entry.GetOperands(entry.Instructions[3])[0].Value);
        var start = module.GetFunction(1);
        Assert.True(start.Exported);
        Assert.Same(WindowsLowering.Start, start.Abi);
        Assert.Equal([EmissionOpcode.Call, EmissionOpcode.Call, EmissionOpcode.Unreachable], start.Instructions.Select(x => x.Opcode));

        using var output = new StringWriter();
        module.WriteIr(output);
        var ir = output.ToString();
        var begin = ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal);
        var end = ir.IndexOf("attributes #0", begin, StringComparison.Ordinal);
        Assert.Equal($"define internal void @__kimi_entry_body() #0 {{\nentry:\n  %p{slot.Place} = alloca %kimi.string, align 8\n  br label %b0\nb0:\n  store %kimi.string {{ ptr @__kimi_text, i64 13, i8 0 }}, ptr %p{slot.Place}, align 8\n  call void @__kimi_write_line(ptr %p{slot.Place}, ptr @__kimi_location, i64 14)\n  ret void\n}}\ndefine void @__kimi_start() noreturn #0 {{\nentry:\n  call void @__kimi_entry_body()\n  call void @__kimi_exit(i32 0)\n  unreachable\n}}\n", ir[begin..end]);
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
            Assert.Equal(2, ir.Split(abi.GetDefinition(exported: false)).Length); // Exactly one definition, no same-name declare.
        }

        Assert.Contains(WindowsLowering.Start.GetDefinition(exported: true), ir);

        Assert.Null(WindowsLowering.Unit.ArgumentType);
        Assert.DoesNotContain("{{", ir);
        Assert.Throws<InvalidOperationException>(() => new EmissionFunction().AddCall(-1, WindowsLowering.WriteLine, []));
    }

    [Theory]
    [InlineData("bool", "i8", "i1", 1)]
    [InlineData("char", "i32", "i32", 4)]
    [InlineData("u16", "i16", "i16", 2)]
    [InlineData("isize", "i64", "i64", 8)]
    [InlineData("u128", "i128", "i128", 16)]
    [InlineData("f32", "float", "float", 4)]
    [InlineData("f64", "double", "double", 8)]
    public void ScalarRepresentationsFollowTheInitialProfile(string type, string storage, string computation, int size)
    {
        // SPEC 21.1.4: size, alignment and stride are equal; arguments pass the direct computation Type.
        var value = WindowsLowering.GetValue(BoundType.Primitives[type])!;
        Assert.Equal((storage, computation, computation), (value.Layout.StorageType, value.ComputationType, value.ArgumentType));
        Assert.Equal((size, size, size), (value.Layout.Size, value.Layout.Alignment, value.Layout.Stride));
        Assert.Null(WindowsLowering.GetValue(BoundType.Never));
    }

    [Fact]
    public void ConstantPoolSharesBytesAndRetainsNulAndSupplementaryCharacters()
    {
        var pool = new LlvmConstantPool();
        var index = pool.Intern("😀\0", LlvmConstantKind.Text);
        Assert.Equal(5, pool[index].ByteLength);
        Assert.Equal("@__kimi_text = private unnamed_addr constant [5 x i8] c\"\\F0\\9F\\98\\80\\00\", align 1\n", pool[index].Definition);
        Assert.Equal(index, pool.Intern("😀\0", LlvmConstantKind.Location));
        Assert.Equal("__kimi_location", pool[pool.Intern("a", LlvmConstantKind.Location)].Name);
        Assert.Equal("__kimi_text.1", pool[pool.Intern("b", LlvmConstantKind.Text)].Name);
        Assert.Equal(3, pool.Count);
        Assert.Throws<ArgumentException>(() => pool.Intern(string.Empty, LlvmConstantKind.Text));

        pool.Clear();
        index = pool.Intern("x", LlvmConstantKind.Text);
        Assert.Equal((1, "__kimi_text", 1), (pool.Count, pool[index].Name, pool[index].ByteLength));
    }

    [Fact]
    public void SequentialLiteralCallsShareConstantsAndTransferEachArgument()
    {
        var c = MinimalEmissionTest.Analyze("writeLine(\"a\")\nwriteLine(\"a\")\nwriteLine(\"\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var entry = module.GetFunction(0);
        Assert.Equal(3, entry.Slots.Count);
        Assert.Equal(3, entry.Instructions.Count(x => x.Callee == WindowsLowering.WriteLine));
        Assert.DoesNotContain(entry.Instructions, x => x.Callee == WindowsLowering.DestroyString);
        using var output = new StringWriter();
        module.WriteIr(output);
        var ir = output.ToString();
        Assert.Single(ir.Split('\n'), x => x.StartsWith("@__kimi_text", StringComparison.Ordinal));
        Assert.Equal(2, ir.Split("{ ptr @__kimi_text, i64 1, i8 0 }").Length - 1);
        Assert.Contains("{ ptr null, i64 0, i8 0 }", ir); // Empty literal: Static/null/length zero (SPEC 21.5.6).
        Assert.Contains("c\"\\48\\65\\6C\\6C\\6F\\2E\\6B\\69\\6D\\69\\3A\\33\\3A\\31\"", ir); // Hello.kimi:3:1
    }

    [Fact]
    public void UnitApplicationLowersToAReturnOnly()
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var entry = module.GetFunction(0);
        Assert.Empty(entry.Slots);
        Assert.Equal([EmissionOpcode.Branch, EmissionOpcode.Label, EmissionOpcode.ReturnVoid], entry.Instructions.Select(x => x.Opcode));
        Assert.Equal(0, module.Constants.Count);
    }
}
