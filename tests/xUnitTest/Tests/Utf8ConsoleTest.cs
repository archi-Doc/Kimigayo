// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class Utf8ConsoleTest
{
    [Theory]
    [InlineData("ref/string", "", KimiDeclarationId.WriteLine)]
    [InlineData("Text.Utf8Slice{r}", "\n    origin r.source == static", KimiDeclarationId.WriteLineUtf8)]
    public void ExpectedFunctionTypeSelectsTheConsoleOverload(string parameter, string origins, KimiDeclarationId selected)
    {
        var c = MinimalEmissionTest.Analyze("alias Output => Kimi.Console\nlet print: (" + parameter + ") -> () = Output.writeLine" + origins);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
            var visitor = new SelectionVisitor();
            visitor.Visit(c.Kotonoha.RootKoto);
            Assert.Equal(selected, visitor.Selected);
        }
    }

    [Fact]
    public void AnUntypedConsoleFunctionReferenceCannotChooseAnOverload()
        => Assert.False(MinimalEmissionTest.Analyze("let print = Console.writeLine").Binding.Result.IsComplete);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BoundedConsoleInterpolationUsesExactlyThirtyThreeStackBytes()
    {
        const string Source = "let n = 42@i64\nConsole.writeLine(\"My number is \\(n)\")";
        NativeAllocationAudit.WriteFixture("Utf8ConsoleStack", Source, 0, 0, 0, "My number is 42\n");
        var c = MinimalEmissionTest.Analyze(Source);
        var outputIr = CompilationTestHelper.WriteIr(c);
        Assert.Contains("alloca [33 x i8], align 1", outputIr); // The region's own name is each generator's (R5-M12 class I).
    }

    // Each side of the limit with a region partly empty (7 writes one of i8's four bytes), and the limit's region full (i8's widest value).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("1023", 1023, "7", 0)]
    [InlineData("1024", 1024, "7", 0)]
    [InlineData("1025", 1025, "7", 1)]
    [InlineData("1024Full", 1024, "-128", 0)]
    public void StackLimitSelectsOnePathBeforeEvaluation(string name, int bound, string value, int allocations)
    {
        var text = new string('x', bound - 4);
        var source = "func next() -> i8\n    Console.writeLine(\"once\")\n    return " + value + "\nConsole.writeLine(\"" + text + "\\(next())\")";
        NativeAllocationAudit.WriteFixture("Utf8ConsoleLimit" + name, source, allocations, allocations, allocations * bound, "once\n" + text + value + "\n");
    }

    // SPEC 22.10.5 (G286): a direct interpolation of one value at its formatting kind's widest value fills its stack region exactly
    // (DECISIONS R5-M131's premise: the HeapBuffer arm of the runtime's reserve never grows a region), so a bound one byte short fails.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("I8", "-128@i8", "-128")]
    [InlineData("U8", "255@u8", "255")]
    [InlineData("I16", "-32768@i16", "-32768")]
    [InlineData("U16", "65535@u16", "65535")]
    [InlineData("I32", "-2147483648@i32", "-2147483648")]
    [InlineData("U32", "4294967295@u32", "4294967295")]
    [InlineData("I64", "-9223372036854775808@i64", "-9223372036854775808")]
    [InlineData("U64", "18446744073709551615@u64", "18446744073709551615")]
    [InlineData("I128", "-170141183460469231731687303715884105728@i128", "-170141183460469231731687303715884105728")]
    [InlineData("U128", "340282366920938463463374607431768211455@u128", "340282366920938463463374607431768211455")]
    [InlineData("F32", "-1e15@f32", "-1000000000000000")]
    [InlineData("F64", "-2.2250738585072014e-308", "-2.2250738585072014e-308")]
    [InlineData("Char", "'😀'", "😀")]
    [InlineData("Bool", "false", "false")]
    [InlineData("Unit", "()", "()")]
    public void StackRegionsHoldEachKindsWidestValue(string kind, string value, string text)
    {
        var source = "let v = " + value + "\nConsole.writeLine(\"\\(v)\")";
        NativeAllocationAudit.WriteFixture("Utf8ConsoleWidest" + kind, source, 0, 0, 0, text + "\n");
        var ir = GeneratedIr.Parse(CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source)));
        var region = Assert.Single(
            ir.Functions.SelectMany(static x => x.Instructions),
            x => x.Callee == ir.Function("__kimi_text_fixed") && x.Operands[1].Opcode == "alloca").Operands[1];
        Assert.Equal($"alloca [{Encoding.UTF8.GetByteCount(text)} x i8], align 1", region.Shape);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReturningDuringStackFormattingDoesNotFreeTheStackOrPrintThePrefix()
    {
        const string Source = """
            func run() -> i32
                Console.writeLine("prefix\(do => return 7)")
            require run() == 7 else => $abort("return target")
            Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleReturn", Source, 0, 0, 0, "done\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void StackFormattingPreservesUserTemporaryDestruction()
    {
        const string Source = """
            struct Value
                public init() => Console.writeLine("create")
                public func get(self: ref/Self) -> i32 => 42
                drop => Console.writeLine("destroy")
            Console.writeLine("n=\(Value.init().get())")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleLifetime", Source, 0, 0, 0, "create\nn=42\ndestroy\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericConcreteBoundedValuesUseTheStack()
    {
        const string Source = """
            func print<T>(value: ref/T)
                T is Utf8Format
                Console.writeLine("value=\(value)")
            print(42)
            print("abc")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleGeneric", Source, 1, 1, 9, "value=42\nvalue=abc\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedAndConditionalStackFormattingDoesNotAllocate()
    {
        const string Source = """
            var i = 0
            while i < 3
                if i != 1 => Console.writeLine("i=\(i)")
                i += 1
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleLoop", Source, 0, 0, 0, "i=0\ni=2\n");
    }

    [Fact]
    public void InterpolatedTemporaryCannotLendAViewPastTheExpression()
    {
        var c = MinimalEmissionTest.Analyze("let text = Text.utf8(\"\\(42)\")\nConsole.writeLine(text)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
    }

    private sealed class SelectionVisitor : KotoVisitor
    {
        internal KimiDeclarationId? Selected { get; private set; }

        public override void Visit(Koto node)
        {
            if (node is MemberAccessKoto && node.SymbolOf()?.Name == "writeLine")
            {
                this.Selected = node.SymbolOf()!.LibraryDeclaration;
            }

            base.Visit(node);
        }
    }
}
