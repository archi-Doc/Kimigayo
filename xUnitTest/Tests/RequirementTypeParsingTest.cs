// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class RequirementTypeParsingTest
{
    [Theory]
    [InlineData("()")]
    [InlineData("(i32,)")]
    [InlineData("(i32, [1 of S.C.Element])")]
    [InlineData("((i32, string), [1 of (bool, i32)])")]
    [InlineData("Box<i32>.C.Element")]
    [InlineData("Box<Box<i32>>.C.Element")]
    [InlineData("(Box<i32>.C.Element or (i32, string))")]
    [InlineData("not (i32, string) or Box<i32>.C.Element")]
    [InlineData("(((i32,)))")]
    [InlineData("(Pair<i32, string> or [1 of (bool, i32)])")]
    [InlineData("(::Api.Box<(i32, string)>.C.Element and Copy)")]
    [InlineData("(i32, string) and not Box<i32>.C.Element")]
    public void RequirementsPreserveSyntaxThroughWritingAndReload(string requirement)
    {
        var tree = ParseTestHelper.ParseSuccess($"func use<T>(value?: T)\n    T is {requirement}\n    ()");
        var written = Write(tree);
        Assert.Contains(requirement, written);
        Assert.Equal(written, Write(ParseTestHelper.ParseSuccess(written)));
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree));
        Assert.NotNull(restored);
        restored.OnDeserialized(Compilation.CreateForTest());
        ParseTestHelper.AssertValid(restored);
        Assert.Equal(written, Write(restored));
        AssertParents(restored.RootKoto);
    }

    [Theory]
    [InlineData("()", 0)]
    [InlineData("(i32,)", 1)]
    [InlineData("(i32, string)", 2)]
    public void TupleRequirementsRetainTheirArityAndSpan(string requirement, int arity)
    {
        var function = ParseTestHelper.ParseSingleFunction($"func use<T>(value?: T)\n    T is {requirement}\n    ()");
        var clause = Assert.IsType<IsKoto>(Assert.Single(function.TypeConstraints));
        var tuple = Assert.IsType<TupleTypeKoto>(clause.Right);
        Assert.Equal(arity, tuple.ElementNodes.Count);
        Assert.Equal(requirement.Length, tuple.Span.Length);
    }

    [Fact]
    public void RequirementPrecedenceIsUnchanged()
    {
        var function = ParseTestHelper.ParseSingleFunction("func use<T>(value?: T)\n    T is not (i32,) or Box<i32>.C.Element and Copy\n    ()");
        var root = Assert.IsType<NotKoto>(Assert.IsType<IsKoto>(Assert.Single(function.TypeConstraints)).Right);
        var either = Assert.IsType<OrKoto>(root.Operand);
        Assert.IsType<TupleTypeKoto>(either.Left);
        var both = Assert.IsType<AndKoto>(either.Right);
        var projection = Assert.IsType<MemberAccessKoto>(both.Left);
        Assert.IsType<GenericsKoto>(Assert.IsType<MemberAccessKoto>(projection.Left).Left);
    }

    [Theory]
    [InlineData("()", "()", true)]
    [InlineData("(i32,)", "(1,)", true)]
    [InlineData("(i32, string)", "(1, \"text\")", true)]
    [InlineData("(i32, string)", "(1, 2)", false)]
    [InlineData("not (i32, string)", "(1, \"text\")", false)]
    [InlineData("not (i32, string)", "(1, 2)", true)]
    [InlineData("((i32,) or string) and not bool", "(1,)", true)]
    public void CallsDischargeTupleIdentityRequirements(string requirement, string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"func identity<T>(value?: T) -> T\n    T is {requirement}\n    return value\nlet result = identity({argument})");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
            Assert.False(c.Emission.Validate(out _));
        }
    }

    [Theory]
    [InlineData("(i32, [1 of S.C.Element])", "public", true)]
    [InlineData("(i32, [1 of S.C.Element])", "internal", false)]
    [InlineData("Box<i32>.C.Element", "public", true)]
    [InlineData("Box<i32>.C.Element", "internal", false)]
    [InlineData("Box<Box<i32>>.C.Element", "public", true)]
    [InlineData("Box<Box<i32>>.C.Element", "internal", false)]
    public void NewRequirementFormsRetainAccessAcrossRebindingAndReload(string requirement, string contractAccess, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"{contractAccess} contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func use<T>(value?: T)\n        T is {requirement}\n        ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Bind().IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
            Assert.False(c.Emission.Validate(out _));
        }

        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.Equal(valid, restored.Bind().IsComplete);
    }

    [Fact]
    public void ConstructedQualifierCannotHideARestrictedArgument()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\nstruct Hidden\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func use<T>(value?: T)\n        T is Box<Hidden>.C.Element\n        ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
    }

    [Theory]
    [InlineData("(i32,, string)")]
    [InlineData("(i32, string")]
    [InlineData("(i32 and string, bool)")]
    [InlineData("(i32, string or bool)")]
    [InlineData("Box<i32>.")]
    [InlineData("Box<i32>.C.")]
    [InlineData("Box<i32.C.Element")]
    [InlineData("(Box<i32>.C.Element or)")]
    public void MalformedRequirementsReportSourceErrorsAndCannotEmit(string requirement)
    {
        var source = $"func use<T>(value?: T)\n    T is {requirement}\n    ()";
        var tree = ParseTestHelper.Parse(source);
        Assert.True(tree.DiagnosticCollection.HasErrors);
        Assert.All(tree.DiagnosticCollection.GetArray(), x => Assert.InRange(x.Span.Start, 0, source.Length));
        Assert.False(MinimalEmissionTest.Analyze(source).Emission.Validate(out _));
    }

    [Fact]
    public void WarmTupleAndConstructedProjectionBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func use<T>(value?: T)\n        T is (Box<i32>.C.Element, string)\n        ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Requirement Binding failed.");
            }
        }));
    }

    private static void AssertParents(Koto node)
    {
        foreach (var child in node.ChildNodes)
        {
            Assert.Same(node, child.Parent);
            AssertParents(child);
        }
    }

    private static string Write(Kotonoha tree)
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}
