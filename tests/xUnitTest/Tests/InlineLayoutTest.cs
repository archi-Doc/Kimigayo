// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 21.3.2: a Type that contains itself by value without indirection is a semantic error of the
// declaration (InvalidInlineLayout_Kd); exceeding the inline nesting depth bound is a resource limit.
public class InlineLayoutTest
{
    private const string Box = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n";

    [Theory]
    [InlineData("struct Node\n    let value: i32\n    let next: Node\n", "Node")]
    [InlineData("struct A\n    let b: B\nstruct B\n    let a: A\n", "A,B")]
    [InlineData(Box + "struct E\n    let b: Box<E>\n", "E")]
    [InlineData("struct Tree\n    let left: Option<Tree>\n", "Tree")]
    [InlineData("struct Pair\n    let items: [2 of (i32, Pair)]\n", "Pair")]
    [InlineData("enum E\n    Empty\n    Again(E)\nlet x = E.Empty", "E")]
    [InlineData("enum E<T>\n    Empty\n    Again(E<E<T>>)\nlet x = E<i32>.Empty", "E")]
    [InlineData("enum A\n    Empty\n    Again(B)\nenum B\n    Again(A)\nlet x = A.Empty", "A,B")]
    public void SelfContainingDeclarationsAreDiagnosed(string source, string cyclic)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        var diagnosed = c.Binding.Issues.Where(x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd).Select(x => ((Kimi.Compiler.Parsing.DeclarationContainerKoto)x.Node).Name).Order(StringComparer.Ordinal);
        Assert.Equal(cyclic.Split(','), diagnosed);

        // DECISIONS R5-M37: Binding publishes the same declarations as AdtDef.InlineCycle, Layout's Rejected storage.
        var published = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.DeclarationContainerKoto>()
            .Where(static x => x.SymbolOf()?.Adt is { InlineCycle: true }).Select(static x => x.Name).Order(StringComparer.Ordinal);
        Assert.Equal(cyclic.Split(','), published);
    }

    [Fact]
    public void IndirectionsAndFiniteNestingAreAccepted()
    {
        // A reference layer, an object layer and a Slice are indirections; by-value nesting of distinct Types is finite.
        var c = MinimalEmissionTest.Analyze(Box + "struct Pair\n    let a: Box<i32>\n    let b: (i32, [2 of bool])\n    public init(a: Box<i32>, b: (i32, [2 of bool])) => (self.a, self.b) = (a, b)\nstruct View {source}\n    let items: Slice<View>{source}\nlet p = Pair.init(Box<i32>.init(1), (2, [true, false]))");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd);
    }

    [Fact]
    public void NestingBeyondTheDepthBoundIsAResourceLimit()
    {
        var source = new StringBuilder("struct S0\n    let v: i32\n");
        for (var i = 1; i <= 70; i++)
        {
            source.Append("struct S").Append(i).Append("\n    let v: S").Append(i - 1).Append('\n');
        }

        source.Append("func f(s: S70) -> i32 => 1\nConsole.writeLine(\"deep\")\n");
        MinimalEmissionTest.AssertGenerationResourceLimit(source.ToString(), "depth limit of 64 levels");
    }

    [Fact]
    public async Task ABodyResolvingNestedTypesBottomUpReportsTheDepthLimit()
    {
        // DECISIONS R5-M37 (a declared change of P7c): the old pool resolves each Type once per body, so t65's one-component Tuple,
        // 65 levels high, reused t64's resolution and was generated, while a fresh resolution reports the depth limit; Layout's
        // placement height now decides it whatever the order. MIR code generation reports it at check once per erased Type, at t65 to t70,
        // the locals whose Types are more than 64 levels high (K13b, DECISIONS R5-M44); the old generator's one record names the limit.
        var source = new StringBuilder("func f() -> i32\n    let t0 = 1\n");
        for (var i = 1; i <= 70; i++)
        {
            source.Append("    let t").Append(i).Append(" = (t").Append(i - 1).Append(",)\n");
        }

        source.Append("    return 0\nlet r = f()\nConsole.writeLine(\"deep\")\n");
        var directory = Path.Combine(Path.GetTempPath(), "kimi-inline-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "layout", Directory = directory };
            project.AddSource("main.kimi", source.ToString());
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains("GenerationResourceLimit_Kd", console.Text, StringComparison.Ordinal);
            var records = Regex.Matches(console.Text, @"GenerationResourceLimit_Kd\n(?: --> (?<fact>\S+)|\nNote: (?<fact>[^\n]+))").Select(static x => x.Groups["fact"].Value);
            var located = string.Join(' ', Enumerable.Range(67, 6).Select(static x => $"main.kimi:{x}:5"));
            Assert.Equal(Codegen.Expect(located, "Inline layout exceeds the generation depth limit of 64 levels.", CodegenDeviations.CheckedSetAtCheck), string.Join(' ', records));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("[2147483648 of u8]")]
    [InlineData("[1073741824 of i32]")]
    [InlineData("(bool, [2147483647 of u8])")]
    public void FiniteLayoutsBeyondTheInternalSizeOrCountBoundAreResourceLimits(string type)
        => MinimalEmissionTest.AssertGenerationResourceLimit($"func inspect(value: {type}) -> i32 => 1\nConsole.writeLine(\"finite\")", "2147483647");

    [Fact]
    public async Task AnInferredCLayoutInstanceReportsALaterFieldsResourceLimit()
    {
        // DECISIONS R5-M29 (a declared change of P7b): the old pool visits every Field of P<()> before Layout reports its zero-sized
        // Field, so the later Field's resource limit is reported (GenerationFailed_Kd before); K10b's InvalidCLayout_Kd, at check under MIR
        // code generation (K13b), supersedes both.
        var directory = Path.Combine(Path.GetTempPath(), "kimi-inline-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "layout", Directory = directory };
            project.AddSource("main.kimi", "#Layout(\"C\")\nstruct P<T>\n    var value: T\n    var big: [2147483648 of u8]\nfunc f<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = f(())\nConsole.writeLine(\"inferred\")");
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains(Codegen.Expect("InvalidCLayout_Kd", "GenerationResourceLimit_Kd", CodegenDeviations.InferredCLayoutAtCheck), console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationFailed_Kd", console.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EnumTagAndAlignmentCountTowardsTheSizeLimit()
        => MinimalEmissionTest.AssertGenerationResourceLimit("enum Big\n    A([2147483647 of u8])\n    B\nfunc inspect(value: Big) -> i32 => 1\nConsole.writeLine(\"finite\")", "2147483647");
}
