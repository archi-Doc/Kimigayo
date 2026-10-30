// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

public class EmissionPlanTest
{
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
}
