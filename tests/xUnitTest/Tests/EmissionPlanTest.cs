// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

public class EmissionPlanTest
{
    [Fact]
    public void LinearBodyContainsOnlyPhysicalOperations()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"Hello, world!\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        Assert.Equal(2, module.FunctionCount);
        var entry = module.GetFunction(0);
        Assert.Same(RuntimeAbi.Entry, entry.Abi);
        Assert.False(entry.Exported);
        var slot = Assert.Single(entry.Slots);
        Assert.Equal(new[] { 0, 8, 16 }, slot.Value.Layout.FieldOffsets.ToArray());
        Assert.Equal((24, 8, 24), (slot.Value.Layout.Size, slot.Value.Layout.Alignment, slot.Value.Layout.Stride));
        // SPEC 22.1: writeLine borrows the literal; the caller destroys the temporary after the call.
        Assert.Equal([EmissionOpcode.Branch, EmissionOpcode.Label, EmissionOpcode.StoreStaticString, EmissionOpcode.Call, EmissionOpcode.Call, EmissionOpcode.ReturnVoid], entry.Instructions.Select(x => x.Opcode));
        Assert.True(entry.Instructions[2].Operation < entry.Instructions[3].Operation);
        Assert.Same(RuntimeAbi.WriteLine, entry.Instructions[3].Callee);
        Assert.Equal(slot.Place, entry.GetOperands(entry.Instructions[3])[0].Value);
        Assert.Same(RuntimeAbi.DestroyString, entry.Instructions[4].Callee);
        Assert.Equal(slot.Place, entry.GetOperands(entry.Instructions[4])[0].Value);
        var start = module.GetFunction(1);
        Assert.True(start.Exported);
        Assert.Same(RuntimeAbi.Start, start.Abi);
        Assert.Equal([EmissionOpcode.Call, EmissionOpcode.Call, EmissionOpcode.Unreachable], start.Instructions.Select(x => x.Opcode));

        using var output = new StringWriter();
        module.WriteIr(output);
        var ir = output.ToString();
        var begin = ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal);
        var end = ir.IndexOf("attributes #0", begin, StringComparison.Ordinal);
        Assert.Equal($"define internal void @__kimi_entry_body() #0 {{\nentry:\n  %p{slot.Place} = alloca %kimi.string, align 8\n  br label %b0\nb0:\n  store %kimi.string {{ ptr @__kimi_text, i64 13, i8 0 }}, ptr %p{slot.Place}, align 8\n  call void @__kimi_write_line(ptr noundef nonnull align 8 dereferenceable(24) %p{slot.Place}, ptr @__kimi_location, i64 14)\n  call void @__kimi_destroy_string(ptr %p{slot.Place}, ptr @__kimi_location, i64 14)\n  ret void\n}}\ndefine void @__kimi_start() noreturn #0 {{\nentry:\n  call void @__kimi_entry_body()\n  call void @__kimi_exit(i32 0)\n  unreachable\n}}\n", ir[begin..end]);
    }

    [Fact]
    public void ReanalysisReusesThePlanWithoutDuplicatingSlotsOrInstructions()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"日本語\\0\")");
        Assert.True(c.Emission.TryPrepare(out var first, out _));
        using var before = new StringWriter();
        first.WriteIr(before);
        c.Bind();
        Assert.False(c.Validate(out _));
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.True(c.Emission.TryPrepare(out var second, out var error), error);
        Assert.Same(first, second);
        using var after = new StringWriter();
        second.WriteIr(after);
        Assert.Equal(before.ToString(), after.ToString());
    }

    [Fact]
    public void RuntimeDefinitionsAndCallsUseThePreparedAbi()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"a\")");
        var ir = CompilationTestHelper.WriteIr(c);
        foreach (var abi in new[] { RuntimeAbi.Exit, RuntimeAbi.WriteLine, RuntimeAbi.DestroyString })
        {
            Assert.Equal(2, ir.Split(abi.GetDefinition(exported: false)).Length); // Exactly one definition, no same-name declare.
        }

        Assert.NotEmpty(GeneratedIr.Parse(ir).Startup.Blocks); // The program body, defined once whatever its symbol (IMPL 21.3.1).

        Assert.Contains(RuntimeAbi.Start.GetDefinition(exported: true), ir);

        Assert.Null(RuntimeAbi.Unit.ArgumentType);
        Assert.DoesNotContain("{{", ir);
        Assert.Throws<InvalidOperationException>(() => new EmissionFunction().AddCall(-1, RuntimeAbi.WriteLine, []));
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
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"a\")\nConsole.writeLine(\"a\")\nConsole.writeLine(\"\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var entry = module.GetFunction(0);
        Assert.Equal(3, entry.Slots.Count);
        Assert.Equal(3, entry.Instructions.Count(x => x.Callee == RuntimeAbi.WriteLine));
        Assert.Equal(3, entry.Instructions.Count(x => x.Callee == RuntimeAbi.DestroyString)); // Each borrowed literal is destroyed by the caller.
        using var output = new StringWriter();
        module.WriteIr(output);
        var ir = output.ToString();
        Assert.Single(ir.Split('\n'), x => x.StartsWith("@__kimi_text", StringComparison.Ordinal));
        Assert.Equal(2, ir.Split("{ ptr @__kimi_text, i64 1, i8 0 }").Length - 1);
        Assert.Contains("{ ptr null, i64 0, i8 0 }", ir); // Empty literal: Static/null/length zero (SPEC 21.4).
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
