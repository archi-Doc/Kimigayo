// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
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

    // A pattern node indexed by an earlier pass keeps no pattern row once an edit moves it into an expression: after `1 => 5`
    // becomes `2 => 1`, the old pattern `1` now the arm's body, the body binds as in a fresh compilation (ResetMatches empties the
    // IndexedPatterns family; a stale row would trip BindNodeCore's pattern guard).
    [Fact]
    public void AnEditedRoleLeavesNoStalePatternRow()
    {
        const string Matched = "func pick(v: i32) -> i32\n    return match v\n        1 => 5\n        _ => 0\n";
        var c = MinimalEmissionTest.Analyze(Matched);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var match = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>().Single();
        var (pattern, body) = (match.Arms[0].Pattern, match.Arms[0].Body);
        var donor = KotoTree.Walk(ParseTestHelper.ParseSuccess(Matched.Replace("1 => 5", "2 => 5", StringComparison.Ordinal)).RootKoto).OfType<MatchKoto>().Single().Arms[0].Pattern;
        ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(match, pattern, donor));
        Assert.True(KotoHelper.Replace(match, body, pattern));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
    }

    // PLAN G1278 (G1278-A review N6): a construction qualifier whose path formed its Type in an earlier pass keeps no FormedTypes row
    // once an edit makes it a carrier. `O.In` (no own parameters) forms its Type; after `O.In.A` becomes `P.In.A`, the qualifier's own
    // list is omitted and `P.In<S>` supplies it (SPEC 6.5.2 steps 2-3, 9.7), so the written `P.In<S>` is the one site and its
    // UnsatisfiedConstraint_Kd the one record (SPEC 8.6.3, 23.1.2), as in a fresh compilation of the edited source (ResetPass empties
    // the FormedTypes family; a stale row would judge the qualifier as a second site).
    [Fact]
    public void AnEditedQualifierLeavesNoStaleFormationRow()
    {
        const string Before = "contract C\nstruct S\nstruct O\n    public enum In\n        A\nstruct P\n    public enum In<T>\n        T is C\n        A\n" +
            "let v: P.In<S> = O.In.A\n";
        var after = Before.Replace("= O.In.A", "= P.In.A", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(Before);
        Assert.False(c.Binding.Result.IsComplete); // `O.In` is not `P.In<S>`.
        var qualifier = Qualifier(c.Kotonoha.RootKoto);
        var path = Qualifier(ParseTestHelper.ParseSuccess(after).RootKoto).Left;
        ParseTestHelper.Detach(path, new CodeBlockKoto(path.CodeContext));
        Assert.True(KotoHelper.Replace(qualifier, qualifier.Left, path));
        Assert.False(c.Bind().IsComplete);
        var fresh = MinimalEmissionTest.Analyze(after);
        Assert.Single(fresh.Binding.Issues, static x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
        Assert.Equal(Records(fresh), Records(c));

        static MemberAccessKoto Qualifier(Koto root)
            => (MemberAccessKoto)KotoTree.Walk(root).OfType<MemberAccessKoto>().Single(x => x.Right.ToString() == "A").Left;

        static string Records(Compilation c) => string.Join("\n", c.Binding.Issues.Select(static x => x.Code.ToString()).Order());
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
