// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BindingTest
{
    [Fact]
    public void PipelineBindsExistingNodesAndRetainsCallPlans()
    {
        var compilation = Parse("func add(x: i32, y: i32) -> i32 => x + y\nlet result = add(1, 2)");
        var nodes = All(compilation.Kotonoha.RootKoto).ToArray();
        var call = Assert.Single(nodes.OfType<InvocationKoto>());
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(nodes, All(compilation.Kotonoha.RootKoto));
        Assert.Equal("i32", call.BoundType!.Name);
        Assert.Equal("add", call.BoundCall!.Target.Name);
        Assert.Equal(new[] { 0, 1 }, call.BoundCall.ArgumentToParameter.ToArray());
        var plan = call.BoundCall;
        var symbol = call.BoundSymbol;
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Same(symbol, call.BoundSymbol);
        Assert.Same(plan, call.BoundCall);
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Fact]
    public void UnresolvedSyntaxSurvivesUntilGeneratedDeclarationsAreIntegrated()
    {
        var compilation = Parse("group Demo\n    func read() -> i32 => generated()");
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var group = Assert.Single(compilation.Kotonoha.RootKoto.NestedContainers);
        var provisional = compilation.Binding.Bind(BindingMode.Provisional);
        Assert.False(provisional.IsComplete);
        Assert.Equal(BindingState.Unresolved, call.BindingState);
        Assert.Empty(compilation.Binding.Issues);
        compilation.Kotonoha.CreateCodeContext().Parse(group, "func generated() -> i32 => 42");
        Assert.True(compilation.Binding.Bind(BindingMode.Final).IsComplete, Describe(compilation));
        Assert.Same(call, Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>()));
        Assert.Equal("generated", call.BoundCall!.Target.Name);
    }

    [Fact]
    public void AddedOverloadInvalidatesPreviouslySuccessfulSelection()
    {
        var compilation = Parse("group Demo\n    func f(x: i32) -> i32 => x\n    func read() -> i32 => f(1)");
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        compilation.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(BindingState.Resolved, call.BindingState);
        compilation.Kotonoha.CreateCodeContext().Parse(Assert.Single(compilation.Kotonoha.RootKoto.NestedContainers), "func f(x: i64) -> i32 => 1");
        Assert.False(compilation.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.Null(call.BoundCall);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Theory]
    [InlineData("let result = absent", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("let x: i32 = true", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x = 1\nlet x = 2", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f(x: i32) => x\nfunc f(y: i32) -> i64 => 1", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f<T>(x: T) => x\nfunc f<U>(x: U) => x", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f() -> i32 => 1\nfunc g()\n    let f = 1\n    f()", DiagnosticCode.NotCallable_Kd)]
    [InlineData("group Values\n    var a = b\n    var b = a", DiagnosticCode.CyclicBinding_Kd)]
    [InlineData("func f() => return 1", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x: i8 = 128", DiagnosticCode.InvalidNumericLiteral_Kd)]
    [InlineData("let x: f32 = 1e100", DiagnosticCode.InvalidNumericLiteral_Kd)]
    [InlineData("let x = 1\nfunc f() => x", DiagnosticCode.InvalidCaptureBinding_Kd)]
    [InlineData("let x: u32 = 1\nlet y = -x", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("var x: f64 = 1.0\nx++", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("var x: f64 = 1.0\nx %= 2.0", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("var x: f64 = 1.0\nx <<= 1", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x = true < false", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x = true & false", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x = 'a' + 'b'", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x = \"a\" - \"b\"", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x: i32 = -1.5", DiagnosticCode.TypeMismatch_Kd)]
    public void FinalBindingRejectsInvalidProgramsWithoutDuplicatingIssues(string source, DiagnosticCode code)
    {
        var compilation = Parse(source);
        Assert.False(compilation.Bind().IsComplete);
        Assert.Contains(compilation.Binding.Issues, issue => issue.Code == code);
        var count = compilation.Binding.Issues.Count;
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(count, compilation.Binding.Issues.Count);
    }

    [Theory]
    [InlineData("let x: u64 = 1\nlet count: u8 = 3\nlet y = x << count", "u64")]
    [InlineData("var x: i16 = 1\nlet count: u32 = 3\nx >>= count\nlet y = x", "i16")]
    [InlineData("let y = \"a\" + \"b\"", "string")]
    [InlineData("var s = \"a\"\ns += \"b\"\nlet y = s", "string")]
    [InlineData("let y = 'a' < 'b'", "bool")]
    [InlineData("let y = \"a\" >= \"b\"", "bool")]
    [InlineData("let y = () == ()", "bool")]
    [InlineData("var x: i8 = 1\nlet y = x++", "i8")]
    [InlineData("let x: f32 = 1.0\nlet y = -x", "f32")]
    public void BuiltInOperatorsFollowTheirOperandCategories(string source, string type)
    {
        var compilation = Parse(source);
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var result = All(compilation.Kotonoha.RootKoto).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "y");
        Assert.Equal(type, result.BoundType!.Name);
    }

    [Fact]
    public void LocalsUseLexicalVisibilityAndInitializersSeeOuterBindings()
    {
        var compilation = Parse("func f(x: i32) -> i32\n    if true\n        let x = x + 1\n    return x");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var function = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => !x.IsGenerated);
        var local = Assert.Single(All(function).OfType<FieldKoto>());
        var operand = Assert.IsType<IdentifierNameKoto>(Assert.IsType<PlusKoto>(local.InitializerKoto).Left);
        Assert.Equal(BindingSymbolKind.Parameter, operand.BoundSymbol!.Kind);
        Assert.NotSame(local.BoundSymbol, operand.BoundSymbol);
    }

    [Fact]
    public void SourceLocalFunctionsAreNotImportedFromAnotherDocument()
    {
        var compilation = Parse("func privateToSource() -> i32 => 1");
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, new SourceDocument("second.kimi", "let result = privateToSource()"));
        Assert.False(compilation.Bind().IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Fact]
    public void GenericCallsRetainInferredTypesAndCanonicalTupleTypes()
    {
        var compilation = Parse("func identity<T>(value: T) -> T => value\nlet a = identity(1)\nlet b = identity<i64>(2)\nlet pair: (i32, i32) = (1, 2)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var calls = All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.Equal("i32", calls[0].BoundCall!.TypeArguments[0].Name);
        Assert.Equal("i64", calls[1].BoundCall!.TypeArguments[0].Name);
        var pair = All(compilation.Kotonoha.RootKoto).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "pair");
        Assert.Same(pair.TypeKoto!.BoundType, pair.InitializerKoto!.BoundType);
    }

    [Fact]
    public void UntypedArgumentsAreFittedOnlyAfterUniqueCandidateSelection()
    {
        var compilation = Parse("func f(x: i8) -> i8 => x\nfunc f(x: i64) -> i64 => x\nlet result = f(128)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal("i64", call.ArgumentNodes[0].BoundType!.Name);
    }

    [Fact]
    public void AccessAndWrongRoleDoNotStopOuterLookup()
    {
        var compilation = Parse("group Outer\n    struct T\n    group Inner\n        group T\n        func f(x: T) -> T => x");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var function = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>());
        Assert.IsType<StructKoto>(function.Parameters[0].Type.BoundSymbol!.Declaration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualArgumentTypesPreferNongenericThenFewerDefaults(bool reverse)
    {
        var declarations = new[]
        {
            "func f<T>(x: T) -> T => x",
            "func f(x: i32, extra: i32 = 0) -> i32 => x",
            "func f(x: i32) -> i32 => x",
        };
        if (reverse)
        {
            Array.Reverse(declarations);
        }

        var compilation = Parse(string.Join("\n", declarations) + "\nlet result = f(1)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var selected = Assert.IsType<FunctionKoto>(call.BoundSymbol!.Declaration);
        Assert.Empty(selected.GenericArguments);
        Assert.Single(selected.Parameters);
    }

    [Fact]
    public void ExpectedResultFillsUnresolvedGenericTypeBeforeLiteralDefaulting()
    {
        var compilation = Parse("func identity<T>(value: T) -> T => value\nlet result: i64 = identity(1)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal("i64", call.BoundCall!.TypeArguments[0].Name);
    }

    [Fact]
    public void NamedArgumentsPreserveSourceOrderInCommittedMapping()
    {
        var compilation = Parse("func f(a: i32, b: i32) -> i32 => a + b\nlet result = f(b: 2, a: 1)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(new[] { 1, 0 }, call.BoundCall!.ArgumentToParameter.ToArray());
        Assert.Equal("2", Assert.IsType<NumberLiteralKoto>(call.ArgumentNodes[0]).Literal);
    }

    [Fact]
    public void DirectBorrowOriginsResolveBeforeBoundCheck()
    {
        var compilation = Parse("func borrow(value: ref/i32) -> ref/i32 => value");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Empty(compilation.Binding.Issues);
        var count = compilation.Binding.Issues.Count;
        Assert.True(compilation.Binding.CheckBound().IsComplete);
        Assert.Equal(count, compilation.Binding.Issues.Count);
    }

    [Fact]
    public void DirectTraversalCoversTheSameChildrenAsPublicSyntaxEnumeration()
    {
        var compilation = Parse("group Demo\n    struct Pair<T>\n        var value: T\n        func get(self: ref/Self) -> T => value\nfunc f(x: i32)\n    if x == 0\n        return\n    let values = [1, 2]\n    let map = [1: 2]\n    let text = \"value: \\(x)\"");
        var visitor = new CollectVisitor();
        visitor.Visit(compilation.Kotonoha.RootKoto);
        Assert.Equal(All(compilation.Kotonoha.RootKoto), visitor.Nodes);
    }

    [Fact]
    public void RebindingReusesScratchAndDoesNotAllocatePerNode()
    {
        var source = "func identity<T>(x: T) -> T => x\n" + string.Join("\n", Enumerable.Range(0, 256).Select(i => $"let v{i} = identity({i})"));
        var compilation = Parse(source);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        }

        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            compilation.Binding.Bind(BindingMode.Final);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ParenthesizedArrayLengthsBindEverySyntaxNode()
    {
        var compilation = Parse("func f(value: [(2) of i32]) -> [2 of i32] => value");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var types = All(compilation.Kotonoha.RootKoto).OfType<FixedArrayTypeKoto>().ToArray();
        Assert.Equal(2, types.Length);
        Assert.Same(types[0].BoundType, types[1].BoundType);
        Assert.Equal(2, types[0].BoundType!.Length);
    }

    [Fact]
    public void IndexedReplacementPreservesOwnershipAndSourceWithoutCopyingStorage()
    {
        var compilation = Parse("func f(x: i32) => x\nlet value = f(1)");
        var call = Assert.Single(All(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var arguments = call.ArgumentNodes;
        var old = arguments[0];
        var replacement = new TestReplacement(old.CodeContext);
        call.ReplaceArgument(0, replacement);
        Assert.Same(arguments, call.ArgumentNodes);
        Assert.Same(replacement, call.ArgumentNodes[0]);
        Assert.Null(old.Parent);
        Assert.Same(call, replacement.Parent);
        Assert.Same(old.CodeContext, replacement.CodeContext);
        Assert.Equal(old.Span, replacement.Span);
        Assert.Throws<InvalidOperationException>(() => call.ReplaceArgument(0, call.Method));
    }

    private sealed class TestReplacement(CodeContext context) : ExpressionKoto(context, default)
    {
        public override KotoKind Akind => KotoKind.NumberLiteral;
    }

    private static Compilation Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        return compilation;
    }

    private static string Describe(Compilation compilation)
        => string.Join(Environment.NewLine, compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + $" ({compilation.Binding.Result})";

    private static IEnumerable<Koto> All(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in All(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class CollectVisitor : KotoVisitor
    {
        internal List<Koto> Nodes { get; } = new();

        public override void Visit(Koto node)
        {
            this.Nodes.Add(node);
            base.Visit(node);
        }
    }
}
