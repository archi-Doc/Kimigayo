// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StructuredConstraintSubjectBindingTest
{
    // The owner's O-42 (a), SPEC F.6's ConstraintSubject: a constructed Type is no subject. One syntax record at the subject, on which the
    // clause rests, so the declaration reports nothing else.
    [Theory]
    [InlineData("() is Copy")]
    [InlineData("() is ()")]
    [InlineData("(i32, bool) is Copy")]
    [InlineData("(i32, string) is Copy")]
    [InlineData("[2 of i32] is Copy")]
    [InlineData("[2 of i32] is [3 of i32]")]
    [InlineData("[2 of string] is not Copy")]
    [InlineData("Box<i32> is Copy")]
    [InlineData("Box<Box<i32>> is Copy")]
    [InlineData("Box<[2 of i32]> is Copy")]
    [InlineData("Box<[(8 / 2) of i32]> is Copy")]
    [InlineData("Box<string> is Box<string>")]
    [InlineData("Box<string> is Copy or Owned")]
    [InlineData("([2 of i32], Box<i32>) is Copy")]
    [InlineData("ref/i32 during static is Copy")]
    [InlineData("ref/i32 during static is i32")]
    [InlineData("raw/i32 is Copy")]
    [InlineData("((i32)) is Copy")]
    [InlineData("((Self)) is Copy")]
    [InlineData("() -> i32 is Owned")]
    [InlineData("(i32) -> bool is (i32,) -> bool")]
    [InlineData("((i32) -> bool) is not Copy")]
    [InlineData("[2 of (i32) -> bool] is not Copy")]
    [InlineData("Box<i32>.Item<i32> is Copy")]
    [InlineData("Box<i32>.(Copy) is Copy")]
    [InlineData("::i32 is Copy")]
    [InlineData("::string is Copy")]
    [InlineData("::Box<i32> is Copy")]
    public void AConstructedTypeIsNoSubject(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause));
        var record = Assert.Single(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), record.Code);
        Assert.Equal(clause[..clause.LastIndexOf(" is ", StringComparison.Ordinal)], record.Text);
    }

    // SPEC F.5, F.6: an AssociatedTypeReference is a subject with or without a leading `::`, whose qualifier may be a constructed Type, in a
    // clause and in a condition. Binding still fails to resolve a root-headed projection, on the base too (F's open issue O4), so this
    // pins the syntax verdict only: rooted and unrooted get the same one.
    [Theory]
    [InlineData("Y.(C).A")]
    [InlineData("W<Y>.(C).A")]
    [InlineData("W<Y>.C.A")]
    [InlineData("G.V<Y>.(C).A")]
    [InlineData("Q.(L).Item(a)")]
    [InlineData("Q.L.Item(a)")]
    [InlineData("W<Y>.Inner")]
    public void AnAssociatedTypeReferenceIsASubjectRootedOrNot(string subject)
    {
        const string head = "contract C\n    associate A\ncontract L\n    associate Item(step)\ncontract K\nstruct Y\nstruct W<T>\ngroup G\n    struct V<T>\nstruct Q\n";
        foreach (var path in new[] { subject, "::" + subject })
        {
            foreach (var clause in new[] { "struct S<Z>\n    origin a\n    " + path + " is K\n", "struct S<Z>\n    Self is K when " + path + " is K\n" })
            {
                var c = MinimalEmissionTest.Analyze(head + clause);
                Assert.DoesNotContain(TestDiagnostics.Of(c, "Hello.kimi"), static x => x.Code == nameof(DiagnosticCode.ExpectedSyntax_Kd));
            }
        }
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    [InlineData("contract")]
    public void EveryRegionRejectsAConstructedSubject(string kind)
    {
        var c = MinimalEmissionTest.Analyze(Source("Box<i32> is Copy").Replace("struct Target", kind + " Target", StringComparison.Ordinal) + (kind == "enum" ? "\n    A" : string.Empty));
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), Assert.Single(TestDiagnostics.Of(c, "Hello.kimi")).Code);
    }

    [Fact]
    public void AssociateRemainsAContextualTypeNameInAConstructedSubject()
    {
        var c = MinimalEmissionTest.Analyze(Source("Box<i32> is Copy").Replace("Box", "associate", StringComparison.Ordinal));
        var record = Assert.Single(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), record.Code);
        Assert.Equal("associate<i32>", record.Text);
    }

    [Theory]
    [InlineData("(value is Dog)")]
    [InlineData("make() is Dog")]
    public void GenericFunctionRuntimeTestsDoNotBecomeTypeConstraints(string expression)
    {
        var c = MinimalEmissionTest.Analyze("struct Dog\nfunc make() -> Dog => make()\nfunc f<T>(value: Dog)\n    " + expression);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Empty(function.TypeConstraints);
        Assert.Single(KotoTree.Walk(function.Body!).OfType<IsKoto>(), x => x.IsRuntimeTest);
    }

    [Theory]
    [InlineData("Box<i32 is Copy")]
    [InlineData("(i32,, bool) is Copy")]
    [InlineData("[2 i32] is Copy")]
    [InlineData("Box<i32>> is Copy")]
    public void MalformedSubjectsAreDiagnosedWithoutLosingFollowingDeclarations(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause) + "\npublic struct After");
        Assert.NotEmpty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Contains(c.Kotonoha.RootKoto.NestedContainers, x => x.Name == "After");
    }

    [Theory]
    [InlineData("(i32, bool is Copy")]
    [InlineData("[2 of i32 is Copy")]
    [InlineData("Box<[(8 >> 1) of i32]> is Copy")]
    public void UnterminatedGroupsAndUnsupportedLengthOperatorsAreDiagnosed(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause));
        Assert.NotEmpty(TestDiagnostics.Of(c, "Hello.kimi"));
    }

    private static string Source(string clause)
        => "public struct Box<T>\n    T is Copy\n    Self is Copy\n    let value: T\npublic struct Target\n    " + clause;
}
