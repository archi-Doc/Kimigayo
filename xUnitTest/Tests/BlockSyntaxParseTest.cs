// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BlockSyntaxParseTest
{
    [Fact]
    public void PreservesBlockKindsScopesAndBodyForms()
    {
        var tree = ParseSuccess("""
            func process()
                unsafe:
                    work()
                defer: close()
                defer: unsafe: releaseRaw(pointer)
                defer:
                    defer: log("end")
                    exit
                let result = resolve:
                    if ready()
                        exit 1 from resolve
                    exit 2 from resolve
            """);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(Items(tree)));
        var body = function.Body!;
        var unsafeBlock = Assert.IsType<UnsafeBlockKoto>(body.Items[0]);
        Assert.False(unsafeBlock.IsInline);
        Assert.False(unsafeBlock.Body.IsExpressionBody);
        Assert.IsType<InvocationKoto>(Assert.Single(unsafeBlock.Body.Items));
        var deferred = Assert.IsType<DeferredBlockKoto>(body.Items[1]);
        Assert.True(deferred.IsInline);
        Assert.Same(deferred, deferred.Body.Parent);
        Assert.Same(deferred.Body, deferred.Body.Items[0].Parent);
        var nested = Assert.IsType<DeferredBlockKoto>(body.Items[2]);
        Assert.True(Assert.IsType<UnsafeBlockKoto>(Assert.Single(nested.Body.Items)).IsInline);
        var cleanup = Assert.IsType<DeferredBlockKoto>(body.Items[3]);
        Assert.IsType<ExitKoto>(cleanup.Body.Items[1]);
        var field = Assert.IsType<FieldKoto>(body.Items[4]);
        var labeled = Assert.IsType<LabeledKoto>(field.InitializerKoto);
        var labeledBody = Assert.IsType<CodeBlockKoto>(labeled.Target);
        var resultExit = Assert.IsType<ExitKoto>(labeledBody.Items[1]);
        Assert.Equal("resolve", resultExit.Label);
        Assert.IsType<NumberLiteralKoto>(resultExit.Expression);
        Assert.Same(labeledBody, KotoHelper.ResolveTransferTarget(resultExit));

        var text = function.ToString();
        var roundTrip = ParseSuccess(text);
        Assert.Equal(text, Assert.Single(Items(roundTrip)).ToString());
    }

    [Theory]
    [InlineData("defer: ()")]
    [InlineData("unsafe: ()")]
    [InlineData("defer: let value = 1")]
    [InlineData("defer: count += 1")]
    [InlineData("defer: defer: work()")]
    [InlineData("defer: let result = if ready() => 1 else => 2")]
    [InlineData("unsafe: return *pointer")]
    [InlineData("let unsafe = 1\nlet defer = unsafe")]
    [InlineData("let pointer: unsafe/i32 = obtainPointer()")]
    [InlineData("let dictionary = [unsafe:1, defer:2]")]
    [InlineData("call(defer: work(), unsafe: 1)")]
    public void AcceptsInlineStatementsAndContextualNames(string source)
        => ParseSuccess(source);

    [Theory]
    [InlineData("let value = unsafe: work()")]
    [InlineData("call((defer: work()))")]
    [InlineData("let value = 1 + unsafe: work()")]
    [InlineData("return defer: work()")]
    [InlineData("let value = unsafe:\n    work()")]
    public void RejectsStatementBlocksInValuePositions(string source)
        => Assert.Contains(Parse(source).DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.BlockStatementInExpression_Kd));

    [Theory]
    [InlineData("defer: work(); other()")]
    [InlineData("unsafe: func helper() => 1")]
    [InlineData("defer: group Example")]
    [InlineData("defer: label:\n    work()")]
    [InlineData("defer: if ready()\n    work()")]
    [InlineData("unsafe: call(\n    1)")]
    [InlineData("defer: ;")]
    public void RejectsInvalidInlineBodies(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("defer:")]
    [InlineData("unsafe:")]
    [InlineData("blockA:")]
    [InlineData("if ready()")]
    [InlineData("while ready()")]
    [InlineData("loop")]
    [InlineData("for value in values")]
    [InlineData("func empty()")]
    [InlineData("defer:\n    // Only a comment.")]
    [InlineData("unsafe:\n    // Empty body")]
    [InlineData("if ready()\n    ()\nelse")]
    [InlineData("match value\n    0 =>")]
    [InlineData("struct Example\n    func empty()")]
    public void RejectsEmptyExecutableBodies(string source)
        => Assert.Contains(Parse(source).DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.EmptyExecutableBlock_Kd));

    [Theory]
    [InlineData("defer:")]
    [InlineData("unsafe:")]
    [InlineData("blockA:")]
    [InlineData("if ready()")]
    [InlineData("while ready()")]
    [InlineData("loop")]
    public void MissingBodyDoesNotConsumeFollowingStatement(string header)
    {
        var tree = Parse($"func process()\n    {header}\n    nextProcess()\n");
        Assert.Contains(tree.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.EmptyExecutableBlock_Kd));
        var function = Assert.IsType<FunctionKoto>(Assert.Single(Items(tree)));
        Assert.Equal(2, function.Body!.Items.Count);
        Assert.IsType<InvocationKoto>(function.Body.Items[1]);
    }

    [Theory]
    [InlineData("defer:\n    #if false\n        work()")]
    [InlineData("unsafe:\n    #if false\n    work()")]
    [InlineData("func process()\n    #if false\n        work()")]
    [InlineData("if ready()\n    #if false\n        work()")]
    [InlineData("defer:\n    #match\n        #case false\n            work()\n        #case _\n            ()")]
    public void ChecksSourceItemsBeforeConditionalSelection(string source)
        => ParseSuccess(source);

    [Theory]
    [InlineData("defer:\n    #if false")]
    [InlineData("defer:\n    #match\n        #case true")]
    [InlineData("defer:\n    #Inline")]
    [InlineData("defer:\n    public")]
    [InlineData("defer:\n    #if false\n        ;")]
    [InlineData("#if false\ndefer:")]
    [InlineData("#if false\n    defer:\n    nextProcess()")]
    [InlineData("#if false\n    #if\n        work()")]
    [InlineData("#if false\n    if ready() => 1 else")]
    [InlineData("#if false\n    func missing()")]
    public void ExclusionDoesNotHideEmptyBodiesOrIncompleteDirectives(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("#LibraryImport(LibraryName) func imported()")]
    [InlineData("#if false\n#LibraryImport(LibraryName) func imported()")]
    [InlineData("#if false\n    #LibraryImport(LibraryName)\n    func imported()")]
    [InlineData("#if false\n    struct Empty")]
    [InlineData("#if false\n    public struct Empty\n        // No members")]
    [InlineData("#if false\n    var value: i32\n        get")]
    public void EmptyBodyRulePreservesBodylessDeclarations(string source)
        => ParseSuccess(source);

    [Theory]
    [InlineData("defer: call(")]
    [InlineData("defer: let value =")]
    [InlineData("let value = unsafe: call()")]
    public void InvalidInlineBodyDoesNotConsumeFollowingStatement(string source)
    {
        var tree = Parse(source + "\nnextProcess()");
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        Assert.Equal("nextProcess()", Items(tree)[^1].ToString());
    }

    [Fact]
    public void ParsesUnsafeFunctionModifierWithoutReservingNamesOrSemantics()
    {
        var tree = ParseSuccess("""
            public unsafe func read(pointer: unsafe/i32) -> i32
                unsafe: return *pointer
            """);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(Items(tree)));
        Assert.True(function.Modifier.HasFlag(ModifierKind.Unsafe));
        Assert.StartsWith("public unsafe func", function.ToString());
        ParseSuccess(function.ToString());
    }

    [Theory]
    [InlineData("group Example\n    defer: close()")]
    [InlineData("struct Example\n    unsafe:\n        work()")]
    public void StatementBlocksAreNotDeclarationContainerMembers(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    private static Kotonoha Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        var tree = compilation.Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, source);
        return tree;
    }

    private static Kotonoha ParseSuccess(string source)
    {
        var tree = Parse(source);
        Assert.True(tree.DiagnosticCollection.GetArray().Length == 0, string.Join(Environment.NewLine, tree.DiagnosticCollection.GetArray().Select(x => x.ToString())));
        return tree;
    }

    private static IReadOnlyList<Koto> Items(Kotonoha tree) => tree.GeneratedFunction!.Body!.Items;
}
