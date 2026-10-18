// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The pipeline-neutral reading of generated LLVM text (R5 E15, <see cref="GeneratedIr"/>): a requirement written with it names no
/// value, label, generated function or generator helper, so it holds for both generators wherever both are correct.</summary>
public sealed class GeneratedIrTest
{
    // Both generators cover these forms (R5 C0b1): scalar, string-borrow, zero-sized, tuple and Never signatures and scalar constants.
    private const string Program =
        "func f(flag: bool, n: i64, text: ref/string, unit: ()) -> bool => flag\nfunc g(pair: (i32, i64)) => ()\nfunc stop() -> Never\n" +
        "    loop => ()\nlet small: u8 = 255\nlet wide: i128 = -5\n";

    private const string Text = """
        %kimi.string = type { ptr, i64, i64 }
        declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)
        define internal i64 @__kimi_f7(i1 %a0, ptr noundef nonnull align 8 dereferenceable(24) %a1) #0 {
        entry:
          br label %head
        head:                ; preds = %entry, %head
          %v = phi i64 [ 0, %entry ], [ %w, %head ]
          %w = add i64 %v, 1
          %p = getelementptr %kimi.string, ptr %a1, i32 0, i32 1
          switch i1 %a0, label %head [
            i1 0, label %done
          ]
        done:
          call void @llvm.memcpy.p0.p0.i64(ptr %p, ptr %a1, i64 24, i1 false) #1
          ret i64 %w
        }
        attributes #0 = { noreturn "target-cpu"="x86-64" }
        attributes #1 = { nounwind }
        """;

    // The same requirement on the text of each generator: the ABI of the signatures (IMPL 21.3), a Never function that loops without a
    // return (IMPL 21.3.1), constants in two's complement at their width stored into slots of their Type (IMPL 21.1.3), the entry's
    // startup call and __kimi_exit(0), and no termination claim, though every function, value and label is named differently.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneRequirementHoldsForBothGenerators(bool mir)
    {
        var ir = GeneratedIr.Parse(Generate(Program, mir));
        ir.Single("internal i1 (i1, i64, ptr noundef nonnull align 8 dereferenceable(24))");
        Assert.DoesNotContain(ir.Single("internal void (ptr)").Instructions, static x => x.Opcode == "alloca");
        var stop = Assert.Single(ir.Functions, static x => x.Signature == "internal void ()" && x.Attributes.Contains("noreturn"));
        var seen = new HashSet<IrBlock>();
        for (var block = stop.Blocks[0]; seen.Add(block); block = Assert.Single(block.Successors))
        {
        }

        var startup = ir.Startup;
        Assert.Equal("internal void ()", startup.Signature);
        Assert.Contains(startup.Instructions, static x => x.Shape == "store i8 -1, ptr %, align 1" && x.Operands[^1].Shape == "alloca i8, align 1");
        Assert.Contains(
            startup.Instructions, static x => x.Shape == "store i128 -5, ptr %, align 16" && x.Operands[^1].Shape == "alloca i128, align 16");
        var start = ir.Function("__kimi_start");
        Assert.Equal("void ()", start.Signature);
        Assert.Equal(["noreturn", .. startup.Attributes], start.Attributes);
        Assert.Equal(["call void @()", "call void @(i32 0)", "unreachable"], Assert.Single(start.Blocks).Instructions.Select(static x => x.Shape));
        Assert.Same(ir.Function("__kimi_exit"), start.Instructions[1].Callee);
        ir.AssertNoTerminationClaim();
    }

    // Shapes abstract values, labels and symbols but keep named types; an operand is its definition (a phi's later one included, its
    // blocks not), a successor its block, a callee its function; attributes resolve their groups; a switch may continue over lines.
    [Fact]
    public void NamesAreAbstractedAndResolved()
    {
        var ir = GeneratedIr.Parse(Text);
        var f = ir.Single("internal i64 (i1, ptr noundef nonnull align 8 dereferenceable(24))");
        Assert.Equal(["noreturn", "\"target-cpu\"=\"x86-64\""], f.Attributes);
        Assert.Equal(["br label %"], f.Blocks[0].Instructions.Select(static x => x.Shape));
        var head = f.Blocks[1].Instructions;
        Assert.Equal(
            ["phi i64 [ 0, % ], [ %, % ]", "add i64 %, 1", "getelementptr %kimi.string, ptr %, i32 0, i32 1", "switch i1 %, label % [ i1 0, label % ]"],
            head.Select(static x => x.Shape));
        Assert.Equal([head[1]], head[0].Operands);
        Assert.Equal([head[0]], head[1].Operands);
        Assert.Equal([f.Parameters[1]], head[2].Operands);
        Assert.Equal([f.Parameters[0]], head[3].Operands);
        Assert.Equal([[f.Blocks[1]], [f.Blocks[1], f.Blocks[2]], []], f.Blocks.Select(static x => x.Successors));
        var call = f.Blocks[2].Instructions[0];
        Assert.Equal("call void @(ptr %, ptr %, i64 24, i1 false)", call.Shape);
        Assert.Same(ir.Function("llvm.memcpy.p0.p0.i64"), call.Callee);
        Assert.Equal([head[2], f.Parameters[1]], call.Operands);
        Assert.Equal(["nounwind"], call.Attributes);
        Assert.Equal([head[1]], f.Blocks[2].Terminator.Operands);
        Assert.Equal("void (ptr, ptr, i64, i1)", Assert.Single(ir.Functions, static x => x.Blocks.Count == 0).Signature);
        ir.AssertNoTerminationClaim();
    }

    // Each place a termination claim can take fails: a definition's or a declaration's attributes, a group, a call site and loop metadata.
    [Theory]
    [InlineData("define internal void @f() mustprogress {\nentry:\n  ret void\n}")]
    [InlineData("define internal void @f() #0 {\nentry:\n  ret void\n}\nattributes #0 = { nounwind willreturn }")]
    [InlineData("declare void @g() willreturn\ndefine internal void @f() {\nentry:\n  call void @g()\n  ret void\n}")]
    [InlineData("declare void @g()\ndefine internal void @f() {\nentry:\n  call void @g() #1\n  ret void\n}\nattributes #1 = { mustprogress }")]
    [InlineData("define internal void @f() {\nentry:\n  br label %entry, !llvm.loop !0\n}\n!0 = distinct !{!0, !1}\n!1 = !{!\"llvm.loop.mustprogress\"}")]
    public void ATerminationClaimFails(string text)
    {
        var ir = GeneratedIr.Parse(text);
        Assert.ThrowsAny<XunitException>(ir.AssertNoTerminationClaim);
        ir = GeneratedIr.Parse(text.Replace("mustprogress", "nounwind").Replace("willreturn", "nounwind"));
        ir.AssertNoTerminationClaim();
    }

    // A signature selects exactly one function; a name read but defined nowhere fails the reading.
    [Fact]
    public void ASelectionIsExact()
    {
        var ir = GeneratedIr.Parse("define internal void @a() {\nentry:\n  ret void\n}\ndefine internal void @b() {\nentry:\n  ret void\n}");
        Assert.ThrowsAny<XunitException>(() => ir.Single("internal void ()"));
        Assert.ThrowsAny<XunitException>(() => ir.Single("internal i32 ()"));
        Assert.ThrowsAny<XunitException>(() => GeneratedIr.Parse("define internal i32 @a() {\nentry:\n  ret i32 %missing\n}"));
    }

    // The program's text under one generator, whatever the process's: the old one or MIR code generation (sel).
    private static string Generate(string source, bool mir)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = mir ? CompilerPipeline.Mir : CompilerPipeline.Legacy;
        c.Codegen = mir ? CompilerCodegen.Mir : CompilerCodegen.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        return CompilationTestHelper.WriteIr(c);
    }
}
