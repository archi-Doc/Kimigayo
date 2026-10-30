// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Every syntax edit revokes the whole source analysis, wherever it is; only Binding's own normalization
/// while it binds is not an edit.</summary>
public class SyntaxEditInvalidationTest
{
    private const string Source =
        "struct S\n    public var f: i32\n    public init(f: i32) => self.f = f\n" +
        "group G\n    func twice(n: i32) -> i32 => n * 2\n" +
        "    func read(s: ref/S) -> i32\n        return twice(s.f)\n";

    [Fact]
    public void AFieldEditOutsideCallablesRevokesTheAnalysis()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        var field = Field(c);
        var donor = Field(MinimalEmissionTest.Analyze(Source.Replace("public var f: i32", "public var f: string", StringComparison.Ordinal)));
        var replacement = ParseTestHelper.Detach(donor.TypeKoto!, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(field, field.TypeKoto!, replacement));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Bind().IsComplete); // twice(s.f) no longer fits.
    }

    [Fact]
    public void AnArgumentEditRevokesTheAnalysis()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        var call = Call(c);
        var donor = Call(MinimalEmissionTest.Analyze(Source.Replace("return twice(s.f)", "return twice(3)", StringComparison.Ordinal)));
        var replacement = donor.ArgumentNodes[0];
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        call.ReplaceArgument(0, replacement);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Analysis.IsVerified);
    }

    // A synthesized call bound by an earlier pass is not reused once an edit removes its role: after `var box` becomes
    // `let box`, the read box[0] no longer binds indexUniq, and no stale exclusive call remains visible.
    [Fact]
    public void AnEditedRoleLeavesNoStaleSynthesizedCall()
    {
        const string Indexed = "struct Box\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32 = 0\n    public init() => ()\n" +
            "    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self => self.value@uniq\n" +
            "var box = Box.init()\nlet n = box[0]\n";
        var c = MinimalEmissionTest.Analyze(Indexed);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var index = Statements(c).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "n").InitializerKoto!;
        Assert.NotNull(c.Binding.IndexerCall(index, true));
        var declaration = Statements(c).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "box");
        var donor = Statements(MinimalEmissionTest.Analyze(Indexed.Replace("var box", "let box", StringComparison.Ordinal))).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "box");
        ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(declaration.Parent!, declaration, donor));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.NotNull(c.Binding.IndexerCall(index, false));
        Assert.Null(c.Binding.IndexerCall(index, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedReplacementPreservesCurrentAnalysis(bool self)
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        var call = Call(c);
        var current = call.ArgumentNodes[0];
        var replacement = self ? current : call.Method;
        var version = c.Kotonoha.Syntax.Version;
        Assert.Throws<InvalidOperationException>(() => KotoHelper.Replace(call, current, replacement));
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        Assert.Equal(version, c.Kotonoha.Syntax.Version);
        Assert.Same(current, call.ArgumentNodes[0]);
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
    }

    private static IReadOnlyList<Koto> Statements(Compilation c) => c.Kotonoha.GeneratedFunction!.Body!.Items;

    private static VariableKoto Field(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<VariableKoto>().Single();

    private static InvocationKoto Call(Compilation c)
    {
        var read = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "read");
        var statement = (ReturnKoto)read.Body!.Items[0];
        return (InvocationKoto)statement.Expression!;
    }
}
