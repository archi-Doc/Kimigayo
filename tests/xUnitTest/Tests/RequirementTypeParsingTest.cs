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
    [InlineData("(S{k}).Item")]
    [InlineData("(S{k}).(C).Item")]
    [InlineData("((S{k}).Item or i32)")]
    [InlineData("(S during y).Item")]
    [InlineData("(S during y).(C).Item")]
    [InlineData("((S during y).Item or i32)")]
    public void RequirementsPreserveSyntaxThroughWritingAndReload(string requirement)
    {
        var tree = ParseTestHelper.ParseSuccess($"func use<T>(value: T)\n    T is {requirement}\n    ()");
        var written = ParseTestHelper.Unparse(tree);
        Assert.Contains(requirement, written);
        Assert.Equal(written, ParseTestHelper.Unparse(ParseTestHelper.ParseSuccess(written)));
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree));
        Assert.NotNull(restored);
        restored.OnDeserialized(Compilation.CreateForTest());
        ParseTestHelper.AssertValid(restored);
        Assert.Equal(written, ParseTestHelper.Unparse(restored));
        AssertParents(restored.RootKoto);
    }

    [Theory]
    [InlineData("()", 0)]
    [InlineData("(i32,)", 1)]
    [InlineData("(i32, string)", 2)]
    public void TupleRequirementsRetainTheirArityAndSpan(string requirement, int arity)
    {
        var function = ParseTestHelper.ParseSingleFunction($"func use<T>(value: T)\n    T is {requirement}\n    ()");
        var clause = Assert.IsType<IsKoto>(Assert.Single(function.TypeConstraints));
        var tuple = Assert.IsType<TupleTypeKoto>(clause.Right);
        Assert.Equal(arity, tuple.ElementNodes.Count);
        Assert.Equal(requirement.Length, tuple.Span.Length);
    }

    [Fact]
    public void RequirementPrecedenceIsUnchanged()
    {
        var function = ParseTestHelper.ParseSingleFunction("func use<T>(value: T)\n    T is not (i32,) or Box<i32>.C.Element and Copy\n    ()");
        var root = ParseTestHelper.Unary(KotoKind.Not, Assert.IsType<IsKoto>(Assert.Single(function.TypeConstraints)).Right);
        var either = ParseTestHelper.Binary(KotoKind.Or, root.Operand);
        Assert.IsType<TupleTypeKoto>(either.Left);
        var both = ParseTestHelper.Binary(KotoKind.And, either.Right);
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
        var c = MinimalEmissionTest.Analyze($"func identity<T>(value: T) -> T\n    T is {requirement}\n    return value@move\nlet result = identity({argument})");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
            Assert.False(c.Generate(TextWriter.Null));
        }
    }

    [Theory]
    [InlineData("(i32, [1 of S.(C).Element])", "public", true)]
    [InlineData("(i32, [1 of S.(C).Element])", "internal", false)]
    [InlineData("Box<i32>.(C).Element", "public", true)]
    [InlineData("Box<i32>.(C).Element", "internal", false)]
    [InlineData("Box<Box<i32>>.(C).Element", "public", true)]
    [InlineData("Box<Box<i32>>.(C).Element", "internal", false)]
    public void NewRequirementFormsRetainAccessAcrossRebindingAndReload(string requirement, string contractAccess, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"{contractAccess} contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func use<T>(value: T)\n        T is {requirement}\n        ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Bind().IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.FailureOf() == BindingFailure.Access);
            Assert.False(c.Generate(TextWriter.Null));
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
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\nstruct Hidden\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func use<T>(value: T)\n        T is Box<Hidden>.(C).Element\n        ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.FailureOf() == BindingFailure.Access);
    }

    // SPEC F.5: a BoundContainerQualifier, `(S{k})` or `(S during w)` (SPEC 15.3.1's single-slot binding), takes a PathSuffix, so
    // `(S{k}).Item` and `(S during w).Item` are an associated specification's Type, and a missing member is Binding's record.
    [Theory]
    [InlineData("(S{k}).Item\n        origin k.s == w", "")]
    [InlineData("(S{k}).Nope\n        origin k.s == w", "InvalidAssociatedType_Kd UnresolvedBinding_Kd")]
    [InlineData("(S during w).Item", "")]
    public void BoundQualifierProjectionsAreSpecificationTypes(string specification, string codes)
    {
        var source = "contract Src\n    associate Item\n    func read(self: ref/Self) -> Self.Item\nstruct S {s}\n    Self is Src\n    public let v: ref/i32 during s\n    public init(v: ref/i32 during s) => self.v = v\n    public func read(self: ref/Self) -> ref/i32 during s => self.v\n" +
            $"struct W {{w}}\n    Self is Src\n    associate Item is {specification}\n    public let v: ref/i32 during w\n    public init(v: ref/i32 during w) => self.v = v\n    public func read(self: ref/Self) -> ref/i32 during w => self.v\npublic func main() => ()\n";
        Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(source).Select(static x => x.Code)));
    }

    // SPEC F.5: one decision covers the whole qualifier, so the same Type parses in every requirement and Type position (a
    // specification, a `when` condition, a local, a parameter, a result, a Field, after a Semantics prefix, in a Type argument and an
    // alias target), and a form outside F does not (G250 (3)'s note). A Contract-qualified projection keeps any grouped base (SPEC
    // 8.5.2: the base may be a Core). An OriginApplication follows the projection in each position (NamedType := NamedReference
    // OriginApplication?; PLAN G459).
    [Theory]
    [InlineData("struct W {w}\n    associate Item is TYPE\n")]
    [InlineData("struct W<T>\n    Self is C when T is TYPE\n")]
    [InlineData("func f(y: ref/i32) -> i32\n    let v: TYPE = y\n    return 0\n")]
    [InlineData("func f(y: ref/i32, v: TYPE) -> i32 => 0\n")]
    [InlineData("func f(y: ref/i32) -> TYPE => y\n")]
    [InlineData("struct W {y}\n    let v: TYPE\n")]
    [InlineData("func f(y: ref/i32, v: ref/TYPE) -> i32 => 0\n")]
    [InlineData("func f(y: ref/i32, v: Option<TYPE>) -> i32 => 0\n")]
    [InlineData("alias A => TYPE\n")]
    public void QualifiersParseInEveryTypePosition(string format)
    {
        foreach (var type in (string[])["(S{k}).Item", "(S during y).Item", "(S during y).(C).Item", "(S during (y and z)).Item", "(i32, u8).(C).Item",
            "(S{k}).Item(y)", "(S during y).Item(y)", "(S during y).(C).Item(y)", "(i32, u8).(C).Item(y)"])
        {
            Assert.Contains(type, ParseTestHelper.Unparse(ParseTestHelper.ParseSuccess(format.Replace("TYPE", type, StringComparison.Ordinal))));
        }

        foreach (var type in (string[])["(S{k}?).Item", "(S{k} during y).Item", "(S?).Item", "(ref/S during y).Item", "(S).Item", "(S during y).Item()"])
        {
            Assert.True(ParseTestHelper.Parse(format.Replace("TYPE", type, StringComparison.Ordinal)).Compilation.Diagnostics.HasErrors, type);
        }
    }

    // SPEC F.5's note: an expression qualifier takes only binding sets, also in a qualifier nested in it.
    [Theory]
    [InlineData("(S during y).init(y)", true)]
    [InlineData("(S during y).make()", true)]
    [InlineData("((S during y).Inner{j}).init(y)", true)]
    [InlineData("((S during y).Inner{j}).make(y)", true)]
    [InlineData("((S{k}).Inner during y).init(y)", true)]
    [InlineData("(S{k}).init(y)", false)]
    [InlineData("(S{k}).make()", false)]
    [InlineData("((S{k}).Inner{j}).init(y)", false)]
    public void ExpressionQualifiersTakeOnlyABindingSet(string expression, bool error)
        => Assert.Equal(error, ParseTestHelper.Parse($"func f(y: ref/i32) -> i32\n    let v = {expression}\n    return 0\n").Compilation.Diagnostics.HasErrors);

    [Theory]
    [InlineData("(S{k}?).Item")]
    [InlineData("(S{k} during y).Item")]
    [InlineData("(S).Item")]
    [InlineData("(i32).Item")]
    [InlineData("((i32) -> i32).Item")]
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
        var source = $"func use<T>(value: T)\n    T is {requirement}\n    ()";
        var tree = ParseTestHelper.Parse(source);
        Assert.True(tree.Compilation.Diagnostics.HasErrors);
        Assert.All(TestDiagnostics.Of(tree), x => Assert.InRange(x.Span.Start, 0, source.Length));
        Assert.False(MinimalEmissionTest.Analyze(source).Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTupleAndConstructedProjectionBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func use<T>(value: T)\n        T is (Box<i32>.(C).Element, string)\n        ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Requirement Binding failed.");
            }
        }));
    }

    // G621: a requirement waits for the conformances by what its Types contain (Binding.ContainsProjection over Koto.TypeComponent). Every
    // Type position BindTypeStructure binds is published, and the extras are exactly the named over-approximation: a GenericsKoto's head
    // and length arguments (a GenericArgument's kind is the slot's), a RootName's path and a PathSuffix's qualifier. The bound positions
    // are this table, not an observation of binding: the semantic-slot ratchet (ArchitectureRulesTest) keeps tests from reading a node's
    // bound state, and a test-only seam into BindTypeStructure would be one more reader of it.
    [Fact]
    public void TypeComponentsPublishEveryBoundTypePosition()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct Outer<T>\n    public struct Inner\nstruct Zbox<T>\nfunc f(a: ref/i32?, b: (i32, u8), c: [2 of i32], d: (i32) -> u8, e: ::Zbox<i32>, g: Outer<i32>.Inner, h: (i32), k: Zbox<(i32)>) => ()\n");
        var kinds = new HashSet<Type>();
        Visit(c.Kotonoha.RootKoto);
        Assert.Superset(new HashSet<Type> { typeof(TypeSemanticsKoto), typeof(ParenthesizedTypeKoto), typeof(OptionalTypeKoto), typeof(FixedArrayTypeKoto), typeof(TupleTypeKoto), typeof(FunctionTypeKoto), typeof(GenericsKoto), typeof(SyntaxFormKoto), typeof(MemberAccessKoto) }, kinds);

        void Visit(Koto node)
        {
            Koto?[] expected = node switch
            {
                TypeSemanticsKoto { Type: { } inner } => [inner],
                ParenthesizedTypeKoto group => [group.Type],
                OptionalTypeKoto optional => [optional.Type],
                FixedArrayTypeKoto array => [array.ElementType], // The length is bound as a length, and is no Type position.
                TupleTypeKoto tuple => [.. tuple.ElementNodes],
                FunctionTypeKoto function => [function.Parameters, function.ReturnType],
                OriginApplicationKoto application => [application.Type],
                GenericsKoto generic => [generic.Identifier, .. generic.TypeArguments], // Extras: the head and a length argument.
                SyntaxFormKoto { Akind: KotoKind.RootName } root => [root.Operands[0]], // Extra: BindTypeStructure binds a rooted generic.
                MemberAccessKoto member => [member.Left], // Extra: a PathSuffix's qualifier, searched for a projection in its arguments.
                _ => [],
            };

            var published = new List<Koto?>();
            for (var i = 0; i < node.TypeComponentCount; i++)
            {
                published.Add(node.TypeComponent(i));
            }

            Assert.Equal(expected.Where(static x => x is not null), published);
            if (published.Count != 0)
            {
                kinds.Add(node.GetType());
            }

            foreach (var child in node.ChildNodes)
            {
                Visit(child);
            }
        }
    }

    // G621, open question 4: a constant path where a Type argument is expected names no declared Type, so its Constraint waits for the
    // conformances (the over-approximation); waiting changes none of its records, the same as the base's.
    [Fact]
    public void ConstantPathArgumentWaitsWithTheSameRecords()
    {
        const string Source = "group Sizes\n    public let N = 2\nstruct Zbox<T>\nfunc g<T>(x: T)\n    T is Zbox<Sizes.N>\n    ()\npublic func main() => ()\n";
        Assert.Equal("InvalidConstraint_Kd InvalidConstraint_Kd UnresolvedBinding_Kd", string.Join(' ', ContextualCaptureTest.PublishedRecords(Source).Select(static x => x.Code)));
    }

    private static void AssertParents(Koto node)
    {
        foreach (var child in node.ChildNodes)
        {
            Assert.Same(node, child.Parent);
            AssertParents(child);
        }
    }
}
