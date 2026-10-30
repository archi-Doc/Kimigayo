// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 6.2 Finite inline storage: a declaration whose storage reaches the same declaration again by value, with any Type arguments, is
// a Language error of the declaration (InvalidInlineLayout_Kd, DECISIONS BU-65); exceeding the inline nesting depth bound is a resource
// limit (IMPL 21.3.2).
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

    [InlineData("struct R\n    let r: C<u8>\nstruct C<T>\n    let t: (C<i32>, u8)\n", "C")]

    // PLAN G310 (DECISIONS BU-65): R2<T> reaches R2<i32> through M, so M and R2 lie on one cycle, whatever A stores (before BU-65 an
    // instance walk marked M finite when its second visit added no new member, and R2 went unreported).
    [InlineData("struct A\n    let p: (M, M)\nstruct R2<T>\n    let m: M\nstruct M\n    let r: R2<i32>\n", "M,R2")]
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

    // PLAN G310 (DECISIONS BU-65): one verdict in every declaration order (all orders up to four declarations, else each rotation of the
    // declarations and of their reverse): the declarations on a cycle of the inline edges and no other.
    [Theory]
    [InlineData("A/R2/M")]
    [InlineData("A/R2/M single")]
    [InlineData("R/C")]
    [InlineData("F2 argument")]
    [InlineData("F3 Option")]
    [InlineData("F3 Box")]
    [InlineData("F4 growth")]
    [InlineData("recursive Contract")]
    [InlineData("binding")]
    [InlineData("conditional binding")]
    [InlineData("conditional binding finite")]
    [InlineData("inherited binding")]
    [InlineData("two-step binding")]
    [InlineData("pair target")]
    public void EveryDeclarationOrderGivesOneVerdict(string form)
    {
        var (declarations, cyclic) = OrderForms[form];
        foreach (var order in Orders(declarations.Length))
        {
            var c = MirBuildTest.Compile(string.Concat(order.Select(i => declarations[i])) + "public func main() => ()\n");
            var diagnosed = c.Binding.Issues.Where(static x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd)
                .Select(static x => ((Kimi.Compiler.Parsing.DeclarationContainerKoto)x.Node).Name).Order(StringComparer.Ordinal);
            var published = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.DeclarationContainerKoto>()
                .Where(static x => x.SymbolOf()?.Adt is { InlineCycle: true }).Select(static x => x.Name).Order(StringComparer.Ordinal);
            Assert.Equal((string.Join(',', order), cyclic), (string.Join(',', order), string.Join(',', diagnosed)));
            Assert.Equal(cyclic, string.Join(',', published));
            Assert.True(cyclic.Length != 0 || c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        }
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
        MinimalEmissionTest.AssertGenerationResourceLimit(source.ToString(), "S70");
    }

    [Fact]
    public async Task ABodyResolvingNestedTypesBottomUpReportsTheDepthLimit()
    {
        // DECISIONS R5-M37 (a declared change of P7c): the old pool resolves each Type once per body, so t65's one-component Tuple,
        // 65 levels high, reused t64's resolution and was generated, while a fresh resolution reports the depth limit; Layout's
        // placement height now decides it whatever the order. Check reports it once per erased Type, at t65 to t70, the locals whose Types
        // are more than 64 levels high (K13b, DECISIONS R5-M44), on every pipeline (DECISIONS R5-M156).
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
            var records = Regex.Matches(console.Text, @"GenerationResourceLimit_Kd\n --> (?<fact>\S+)").Select(static x => x.Groups["fact"].Value);
            var located = string.Join(' ', Enumerable.Range(67, 6).Select(static x => $"main.kimi:{x}:5"));
            Assert.Equal(located, string.Join(' ', records));
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
        => MinimalEmissionTest.AssertGenerationResourceLimit($"func inspect(value: {type}) -> i32 => 1\nConsole.writeLine(\"finite\")", type);

    [Fact]
    public async Task AnInferredCLayoutInstanceReportsALaterFieldsResourceLimit()
    {
        // SPEC 8.15.3 (DECISIONS R4-M134 Q8): the inferred instance's zero-sized Field is InvalidCLayout_Kd at check on every pipeline (the
        // checked set's layout obligations), before either generator sees the later Field's resource limit (GenerationFailed_Kd before
        // R5-M29, GenerationResourceLimit_Kd under the old generator until the checked set was judged at check).
        var directory = Path.Combine(Path.GetTempPath(), "kimi-inline-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "layout", Directory = directory };
            project.AddSource("main.kimi", "#Layout(\"C\")\nstruct P<T>\n    var value: T\n    var big: [2147483648 of u8]\nfunc f<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = f(())\nConsole.writeLine(\"inferred\")");
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains("InvalidCLayout_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationResourceLimit_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationFailed_Kd", console.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EnumTagAndAlignmentCountTowardsTheSizeLimit()
        => MinimalEmissionTest.AssertGenerationResourceLimit("enum Big\n    A([2147483647 of u8])\n    B\nfunc inspect(value: Big) -> i32 => 1\nConsole.writeLine(\"finite\")", "Big");

    private const string Src = "contract Src\n    associate Item\n";
    private const string Src2 = "contract Src2\n    associate Val\n";
    private const string StoresItem = "struct X<T>\n    T is Src\n    let b: T.Item\n";
    private const string Conditional = "    Self is Src when J is Src2\n        associate Src.Item is J.Val\n";
    private const string ValIsD = "struct S2\n    Self is Src2\n    associate Src2.Val is D\n";

    // Each form's declarations and the declarations on a cycle.
    private static readonly Dictionary<string, (string[] Declarations, string Cyclic)> OrderForms = new()
    {
        ["A/R2/M"] = (["struct A\n    let p: (M, M)\n", "struct R2<T>\n    let m: M\n", "struct M\n    let r: R2<i32>\n"], "M,R2"),
        ["A/R2/M single"] = (["struct A\n    let p: M\n", "struct R2<T>\n    let m: M\n", "struct M\n    let r: R2<i32>\n"], "M,R2"),
        ["R/C"] = (["struct R\n    let r: C<i32>\n", "struct C<T>\n    let t: (C<i32>, u8)\n"], "C"),

        // An argument stored inline is no cycle of the generic declaration: X<u8> under D never meets X<D> (before BU-65 D was rejected in
        // three orders).
        ["F2 argument"] = (["struct R\n    let r: X<D>\n", "struct X<T>\n    let t: T\n", "struct D\n    let x: X<u8>\n"], string.Empty),

        // Nested instances of one generic declaration are no indirection (before BU-65 these infinite layouts were accepted).
        ["F3 Option"] = (["struct Node\n    let next: Option<Option<Node>>\n"], "Node"),
        ["F3 Box"] = (["struct R\n    let x: Box<Box<D>>\n", "struct D\n    let r: R\n", "struct Box<T>\n    let t: T\n"], "D,R"),

        // D and G reach themselves through E's growth, D -> E<i32> -> E<G<i32>> -> G<i32> -> D (before BU-65 only E was reported).
        ["F4 growth"] = (["struct E<T>\n    let t: T\n    let x: E<G<T>>\n", "struct G<T>\n    let d: D\n", "struct D\n    let e: E<i32>\n"], "D,E,G"),

        // SPEC 8.3.3: T.Item through a recursive Contract is no summary part, so the summaries stay finite.
        ["recursive Contract"] = (["contract Src\n    associate Item is Src\n", "struct X<T>\n    T is Src\n    let a: T\n    let b: Y<T.Item>\n",
            "struct Y<U>\n    U is Src\n    let c: X<U>\n"], "X,Y"),

        // A completed projection adds the edge to its binding's node: S's binding stores D.
        ["binding"] = ([Src, StoresItem, "struct S\n    Self is Src\n    associate Src.Item is E<D>\n", "struct D\n    let x: X<S>\n",
            "struct E<U>\n    let u: U\n"], "D"),

        // SPEC 8.8.2: a conditional binding is walked and proven in its conformance scope, where J is Src2; an inherited one on the base
        // clause's instance.
        ["conditional binding"] = ([Src, Src2, "struct K<J>\n" + Conditional, StoresItem, ValIsD, "struct D\n    let x: X<K<S2>>\n"], "D"),
        ["conditional binding finite"] = ([Src, Src2, "struct K<J>\n" + Conditional, StoresItem, "struct S3\n    Self is Src2\n    associate Src2.Val is i32\n",
            "struct D\n    let x: X<K<S3>>\n"], string.Empty),
        ["inherited binding"] = ([Src, Src2, "open struct KB<J>\n" + Conditional, "struct K<J> : KB<J>\n    let z: i32\n", StoresItem, ValIsD,
            "struct D\n    let x: X<K<S2>>\n"], "D"),
        ["two-step binding"] = ([Src, "contract Src3\n    associate Val is Src2\n", "contract Src2\n    associate Val2\n",
            "struct K<J>\n    Self is Src when J is Src3\n        associate Src.Item is J.Val.Val2\n", StoresItem,
            "struct S4\n    Self is Src3\n    associate Src3.Val is S5\n", "struct S5\n    Self is Src2\n    associate Src2.Val2 is D\n",
            "struct D\n    let x: X<K<S4>>\n"], "D"),

        // A projection of a pair target is a summary part (PLAN G327: W's Field is also reported UnprovenConstraint_Kd).
        ["pair target"] = ([Src, "struct W<s/T>\n    T is Src\n    let b: T.Item\n", "struct S\n    Self is Src\n    associate Src.Item is D\n",
            "struct D\n    let w: W<rc/S>\n"], "D"),
    };

    // Every order of up to four declarations, else each rotation of the declarations and of their reverse.
    private static IEnumerable<int[]> Orders(int count)
    {
        if (count <= 4)
        {
            return Permutations(Enumerable.Range(0, count).ToArray());
        }

        return Enumerable.Range(0, count).SelectMany(r => new[] { false, true }.Select(reverse =>
            Enumerable.Range(0, count).Select(i => reverse ? count - 1 - ((i + r) % count) : (i + r) % count).ToArray()));
    }

    private static IEnumerable<int[]> Permutations(int[] items)
        => items.Length <= 1 ? [items] : items.SelectMany((x, i) => Permutations([.. items.Where((_, j) => j != i)]).Select(rest => (int[])[x, .. rest]));
}
