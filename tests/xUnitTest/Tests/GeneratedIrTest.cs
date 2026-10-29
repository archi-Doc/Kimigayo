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
    // Both generators cover these forms (R5 C0b1): scalar, string-borrow, zero-sized, tuple and Never signatures, a Library's roots (an
    // Application generates only what its startup body reaches, K11), and scalar constants in an Application's startup body.
    private const string Definitions =
        "func f(flag: bool, n: i64, text: ref/string, unit: ()) -> bool => flag\nfunc g(pair: (i32, i64)) => ()\nfunc stop() -> Never\n" +
        "    loop => ()\n";

    private const string Program = "let small: u8 = 255\nlet wide: i128 = -5\n";

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

    // The windows-x64 profile of a definition: Windows x64 unwind information and the baseline x86-64 CPU with SSE2 and no other feature
    // (IMPL 21.4.1), denormals not flushed (IMPL 21.2).
    private static readonly string[] Profile =
        ["uwtable(async)", "\"target-cpu\"=\"x86-64\"", "\"target-features\"=\"+sse2\"", "\"denormal-fp-math\"=\"ieee,ieee\""];

    // The same requirement on the text of each generator: every definition's profile attributes (IMPL 21.4.1 and 21.2), the ABI of a
    // Library's signatures (IMPL 21.3), a Never function that loops without a return (IMPL 21.3.1), an Application's constants in two's
    // complement at their width stored into slots of their Type (IMPL 21.1.3), its entry's startup call and __kimi_exit(0) (IMPL 21.4.2),
    // and no termination claim, though every function, value and label is named differently.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneRequirementHoldsForBothGenerators(bool mir)
    {
        var library = GeneratedIr.Parse(Generate(Definitions, OutputKind.Library, mir));
        var ir = GeneratedIr.Parse(Generate(Program, OutputKind.Application, mir));
        foreach (var module in new[] { library, ir })
        {
            Assert.All(module.Functions.Where(static x => x.Blocks.Count != 0), static x => Assert.All(Profile, y => Assert.Contains(y, x.Attributes)));
            module.AssertNoTerminationClaim();
        }

        library.Single("internal i1 (i1, i64, ptr noundef nonnull align 8 dereferenceable(24))");
        Assert.DoesNotContain(library.Single("internal void (ptr)").Instructions, static x => x.Opcode == "alloca");
        var stop = Assert.Single(library.Functions, static x => x.Signature == "internal void ()" && x.Attributes.Contains("noreturn"));
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

    // A signature selects exactly one function; a value or an attribute group read but defined nowhere fails the reading.
    [Fact]
    public void ASelectionIsExact()
    {
        var ir = GeneratedIr.Parse("define internal void @a() {\nentry:\n  ret void\n}\ndefine internal void @b() {\nentry:\n  ret void\n}");
        Assert.ThrowsAny<XunitException>(() => ir.Single("internal void ()"));
        Assert.ThrowsAny<XunitException>(() => ir.Single("internal i32 ()"));
        Assert.ThrowsAny<XunitException>(() => GeneratedIr.Parse("define internal i32 @a() {\nentry:\n  ret i32 %missing\n}"));
        Assert.ThrowsAny<XunitException>(() => GeneratedIr.Parse("define internal void @a() #0 {\nentry:\n  ret void\n}"));
        Assert.ThrowsAny<XunitException>(() => GeneratedIr.Parse("declare void @g()\ndefine internal void @a() {\nentry:\n  call void @g() #1\n  ret void\n}"));
    }

    // The program's text for an output form under one generator, whatever the process's: the old one or MIR code generation.
    private static string Generate(string source, OutputKind output, bool mir)
    {
        var c = Compilation.CreateForTest();
        c.Codegen = mir ? CompilerCodegen.Mir : CompilerCodegen.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(output);
        c.Analyze();
        return CompilationTestHelper.WriteIr(c);
    }
}
