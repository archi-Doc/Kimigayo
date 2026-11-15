// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ParserOptimizationTest
{
    [Fact]
    public void PreservesPreviouslyExposedDeclarationLists()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var structure = kotonoha.RootKoto.GetOrAddDeclarationContainer("A", TokenKind.Struct, default, default);
        var arguments = structure.GenericArguments;
        var origins = structure.Origins;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "struct A<T> {first}");
        Assert.Empty(TestDiagnostics.Of(kotonoha));
        Assert.Same(arguments, structure.GenericArguments);
        Assert.Same(origins, structure.Origins);
        Assert.Equal("T", Assert.Single(arguments).Identifier);
        Assert.Equal("first", Assert.Single(origins));
        Assert.All(arguments, argument => Assert.Same(structure, argument.Parent));
    }

    [Theory]
    [InlineData("identifier", true)]
    [InlineData("変数", true)]
    [InlineData("a😀", false)]
    [InlineData("a\u0301", false)]
    [InlineData("\u00e1", true)]
    [InlineData("a\u200d", false)]
    [InlineData("\u0301a", false)]
    public void ValidatesPreviouslyInternedSpellings(string spelling, bool valid)
    {
        var compilation = Compilation.CreateForTest();
        var cached = compilation.Intern(spelling);
        // Grow the intern table while other threads read existing entries.
        Parallel.For(0, 1024, i =>
        {
            compilation.Intern($"entry{i}");
            Assert.Same(cached, compilation.Intern(spelling));
        });

        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, $"var {spelling} = 1");
        if (valid)
        {
            Assert.Empty(TestDiagnostics.Of(kotonoha));
            var field = Assert.IsType<FieldKoto>(Assert.Single(kotonoha.GeneratedFunction!.Body!.Items));
            Assert.Same(cached, field.NameKoto.IdentifierName);
        }
        else
        {
            Assert.NotEmpty(TestDiagnostics.Of(kotonoha));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(20)]
    public void RebuildsCompactArgumentsAndBlocksFromSerializedSources(int count)
    {
        var arguments = string.Join(", ", Enumerable.Range(0, count).Select(i => i >= count / 2 ? $"label{i}: {i}" : i.ToString()));
        var types = string.Join(", ", Enumerable.Range(0, Math.Max(1, count)).Select(i => $"T{i}"));
        var source = $"func Run()\n    call<{types}>({arguments})";
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        Assert.Empty(TestDiagnostics.Of(kotonoha));

        // Restore the saved sources before accessing mutable Arguments, preserving compact parser storage.
        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        Assert.NotNull(restored);
        restored.OnDeserialized(compilation);

        foreach (var tree in new[] { kotonoha, restored })
        {
            var function = Assert.IsType<FunctionKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items));
            var body = Assert.IsType<CodeBlockKoto>(function.Body);
            var invocation = Assert.IsType<InvocationKoto>(Assert.Single(body.Items));
            var generics = Assert.IsType<GenericsKoto>(invocation.Method);
            Assert.IsAssignableFrom<ApplicationKoto>(invocation);
            Assert.IsAssignableFrom<ApplicationKoto>(generics);
            Assert.Equal(Math.Max(1, count), generics.TypeArguments.Count);
            Assert.Equal(count, invocation.ArgumentNodes.Count);
            Assert.Equal($"call<{types}>({arguments})", invocation.ToString());
            Assert.All(invocation.Children(), child => Assert.Same(invocation, child.Parent));
            Assert.All(generics.Children(), child => Assert.Same(generics, child.Parent));
            Assert.Same(function, body.Parent);
            Assert.Same(body, invocation.Parent);

            var mutableArguments = invocation.Arguments;
            Assert.Same(mutableArguments, invocation.Arguments);
            Assert.Equal(count, mutableArguments.Count);
            for (var i = 0; i < count; i++)
            {
                Assert.Equal(i.ToString(), mutableArguments[i].ToString());
                Assert.Equal(i >= count / 2 ? $"label{i}" : null, invocation.GetArgumentLabel(i));
            }
        }
    }

    [Fact]
    public void ReplacesChildrenBeforeAndAfterMaterializingArgumentLists()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "func Run()\n    call<A>(1)\n    replacement<B>(2)");
        Assert.Empty(TestDiagnostics.Of(kotonoha));
        var function = Assert.IsType<FunctionKoto>(Assert.Single(kotonoha.GeneratedFunction!.Body!.Items));
        var body = function.Body!;
        var first = Assert.IsType<InvocationKoto>(body.Items[0]);
        var second = Assert.IsType<InvocationKoto>(body.Items[1]);
        var firstGeneric = Assert.IsType<GenericsKoto>(first.Method);
        var secondGeneric = Assert.IsType<GenericsKoto>(second.Method);

        var type = ParseTestHelper.Detach(secondGeneric.TypeArguments[0], new CodeBlockKoto(secondGeneric.CodeContext));
        Assert.True(KotoHelper.Replace(firstGeneric, firstGeneric.TypeArguments[0], type));
        Assert.Equal("call<B>(1)", first.ToString());
        var oldArgument = first.Children().Last();
        var replacementArgument = second.Children().Last();
        ParseTestHelper.Detach(replacementArgument, new CodeBlockKoto(replacementArgument.CodeContext));
        Assert.True(KotoHelper.Replace(first, oldArgument, replacementArgument));
        Assert.Null(oldArgument.Parent);
        Assert.Same(first, replacementArgument.Parent);
        Assert.Equal("call<B>(2)", first.ToString());

        var mutableArguments = first.Arguments;
        Assert.True(KotoHelper.Replace(first, mutableArguments[0], oldArgument));
        Assert.Same(oldArgument, mutableArguments[0]);
        ParseTestHelper.Detach(second, new CodeBlockKoto(second.CodeContext));
        Assert.True(KotoHelper.Replace(body, first, second));
        Assert.Same(second, body.Items[0]);
        Assert.Same(body, second.Parent);
        ParseTestHelper.VerifyParents(kotonoha.RootKoto);
    }

    [Fact]
    public void AttributeViewsFollowReplacedOperands()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "#First(1) func Run() => ()\n#Second(2) func Other() => ()");
        Assert.Empty(TestDiagnostics.Of(kotonoha));
        var functions = kotonoha.GeneratedFunction!.Body!.Items.Cast<FunctionKoto>().ToArray();
        var attribute = functions[0].AttributeChain!;
        var replacement = functions[1].AttributeChain!.Operand;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.Equal("First", attribute.IdentifierKoto.ToString());
        Assert.Equal("1", Assert.Single(attribute.Arguments).ToString());
        Assert.True(KotoHelper.Replace(attribute, attribute.Operand, replacement));
        Assert.Equal("Second", attribute.IdentifierKoto.ToString());
        Assert.Equal("2", Assert.Single(attribute.Arguments).ToString());
    }

    [Theory]
    [InlineData("#Layout(\"C\") struct S", AttributeKind.Layout)]
    [InlineData("#Layout struct S", AttributeKind.Layout)]
    [InlineData("#Layout(\"D\", 1) func f() => ()", AttributeKind.Layout)]
    [InlineData("#Test func f() => ()", AttributeKind.Test)]
    [InlineData("#Test(1) func f() => ()", AttributeKind.Test)]
    [InlineData("#LibraryImport(\"a\", \"b\") func f()", AttributeKind.LibraryImport)]
    [InlineData("#Other func f() => ()", AttributeKind.Unknown)]
    [InlineData("#Layout(\"C\")(1) struct S", AttributeKind.Unknown)]
    // SPEC 6.7: the implementation Attributes are recognized only in the Kimi library's own sources (rows A1-A4).
    [InlineData("#Lang(\"Option\") func f() => ()", AttributeKind.Unknown)]
    [InlineData("#Intrinsic(\"Abort\") func f() => ()", AttributeKind.Unknown)]
    [InlineData("#CallerLocation func f() => ()", AttributeKind.Unknown)]
    [InlineData("#Unsupported func f() => ()", AttributeKind.Unknown)]
    public void ParserClassifiesAnAttributeByItsName(string source, AttributeKind expected)
    {
        var kotonoha = Compilation.CreateForTest().Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        Assert.Equal(expected, Assert.Single(KotoTree.Walk(kotonoha.RootKoto).OfType<AttributeKoto>()).Kind);
    }

    // SPEC 6.7, IMPL 21.3.4: a source parsed into the Kimi library, whose table is the library's from its creation, classifies the
    // implementation Attributes; every other Name stays as in a user source.
    [Theory]
    [InlineData("#Lang(\"Option\") struct S", AttributeKind.Lang)]
    [InlineData("#Intrinsic(\"Abort\") struct S", AttributeKind.Intrinsic)]
    [InlineData("#Intrinsic(\"Fail\", \"KIMI_E_INDEX_BOUNDS\") struct S", AttributeKind.Intrinsic)]
    [InlineData("#CallerLocation struct S", AttributeKind.CallerLocation)]
    [InlineData("#Unsupported struct S", AttributeKind.Unsupported)]
    [InlineData("#Layout(\"C\") struct S", AttributeKind.Layout)]
    [InlineData("#Other struct S", AttributeKind.Unknown)]
    public void TheLibraryClassifiesTheImplementationAttributes(string source, AttributeKind expected)
        => Assert.Equal(expected, ParseAttribute(Compilation.CreateForTest().Library.Kotonoha, source).Kind);

    // SPEC 6.7 (R8-G1 row L7 and row L6's rule): a constructor parsed into the Kimi library also takes the implementation Attributes,
    // in their order; every other Attribute before init is misplaced and attaches to nothing. Before a user init each one is
    // misplaced (the syntax corpus' attribute-on-constructor, DiagnosticPrecisionTest.UnrecognizedAttributesPublishTheirOwnRecords).
    [Theory]
    [InlineData("#Lang(\"ArrayInit\")", "Lang", 0)]
    [InlineData("#Intrinsic(\"Abort\")", "Intrinsic", 0)]
    [InlineData("#CallerLocation", "CallerLocation", 0)]
    [InlineData("#Unsupported", "Unsupported", 0)]
    [InlineData("#Layout(\"C\")", "", 1)]
    [InlineData("#Test", "", 1)]
    [InlineData("#Foo", "", 1)]
    [InlineData("#LibraryImport(\"k\", \"s\")", "", 1)]
    [InlineData("#CallerLocation\n    #Test\n    #Lang(\"ArrayInit\")", "CallerLocation Lang", 1)]
    public void ALibraryConstructorTakesOnlyTheImplementationAttributes(string attributes, string taken, int misplaced)
    {
        var c = Compilation.CreateForTest();
        var library = c.Library.Kotonoha;
        library.CreateCodeContext().Parse(library.RootKoto, $"public struct Probe\n    public let x: i32\n    {attributes}\n    public init(x: i32)\n        self.x = x\n");
        var probe = Assert.Single(library.RootKoto.NestedContainers, static x => x is StructKoto { Name: "Probe" });
        var init = Assert.Single(probe.Members.OfType<FunctionKoto>(), static x => x.IsConstructor);
        var chain = new List<string>();
        for (var attribute = init.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            chain.Insert(0, attribute.Kind.ToString());
        }

        Assert.Equal(taken, string.Join(' ', chain));
        ParseTestHelper.VerifyParents(init);
        Assert.Equal(misplaced, TestDiagnostics.Of(c).Count(static x => x.Code == nameof(Kimi.DiagnosticCode.MisplacedSyntax_Kd)));
        Assert.Equal(misplaced, TestDiagnostics.Of(c).Length);
    }

    // SPEC 6.7, 22.1 (R8-G1 row L6): the library's own constructors with Attributes are Array's two linked ones (SPEC 4.9.5; IMPL
    // 21.3.4), each carrying its `#Lang` alone, and the library parses without a record.
    [Fact]
    public void TheLibrarysLinkedConstructorsCarryLang()
    {
        var c = Compilation.CreateForTest();
        var array = Assert.Single(c.Library.Kotonoha.RootKoto.NestedContainers, static x => x is StructKoto { Name: "Array" });
        var constructors = array.Members.OfType<FunctionKoto>().Where(static x => x.IsConstructor && !x.IsImplicitConstructor).ToArray();
        Assert.Equal(2, constructors.Length);
        Assert.All(constructors, static x =>
        {
            Assert.Equal(AttributeKind.Lang, Assert.IsType<AttributeKoto>(x.AttributeChain).Kind);
            Assert.Null(x.AttributeChain!.AttributeChain);
            ParseTestHelper.VerifyParents(x);
        });
        Assert.Empty(TestDiagnostics.Of(c));
    }

    // SPEC 22.1, IMPL 21.3.4 (R8-G1 G1-5, the Body omission): in the Kimi library a function or constructor carrying `#Intrinsic` or
    // `#Unsupported`, or a constructor carrying `#Lang` (a linked constructor), may omit its Body without a record, and a block after its
    // header with only its clauses (SPEC 7.5's leading region) is no Body; a block holding an executable item stays its Body. Every other
    // declaration needs its Body, and its block is that Body, clauses only or not (SPEC 7.5, 14.2.1): a library declaration that does not
    // omit, and a user declaration, whose implementation Attributes are unknown, so its missing Body keeps its record. Probe is generic so
    // that a constructor's clause is rooted in the declaring Type's parameter (SPEC 7.5).
    [Theory]
    [InlineData(true, "#Intrinsic(\"Abort\")\n    public func f() -> i32", 0, 0, false)]
    [InlineData(true, "#Unsupported\n    public func f() -> i32", 0, 0, false)]
    [InlineData(true, "#Lang(\"ArrayInit\")\n    public init(x: i32)", 0, 0, false)]
    [InlineData(true, "#Intrinsic(\"Abort\")\n    public init(x: i32)", 0, 0, false)]
    [InlineData(true, "#Intrinsic(\"MakeObj\")\n    public func f<U>(value: U) -> i32\n        U is Copy", 0, 1, false)]
    [InlineData(true, "#Lang(\"ArrayInit\")\n    public init(x: i32)\n        T is Copy", 0, 1, false)]
    [InlineData(true, "#Intrinsic(\"Abort\")\n    public func f() -> i32\n        return 1", 0, 0, true)]
    [InlineData(true, "#Lang(\"ArrayInit\")\n    public init(x: i32)\n        T is Copy\n        self.x = x", 0, 1, true)]
    [InlineData(false, "#Intrinsic(\"MakeObj\")\n    public func f<U>(value: U) -> i32\n        U is Copy", 0, 1, true)]
    [InlineData(true, "#Lang(\"ArrayInit\")\n    public func f<U>(value: U) -> i32\n        U is Copy", 0, 1, true)]
    [InlineData(true, "#Lang(\"ArrayInit\")\n    public func f() -> i32", 1, 0, false)]
    [InlineData(true, "#CallerLocation\n    public init(x: i32)", 1, 0, false)]
    [InlineData(true, "public func f() -> i32", 1, 0, false)]
    [InlineData(false, "#Intrinsic(\"Abort\")\n    public func f() -> i32", 1, 0, false)]
    [InlineData(false, "#Unsupported\n    public init(x: i32)", 1, 0, false)]
    public void OnlyALibraryImplementationKindOmitsTheBody(bool library, string declaration, int missing, int constraints, bool body)
    {
        var c = Compilation.CreateForTest();
        var kotonoha = library ? c.Library.Kotonoha : c.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, $"public struct Probe<T>\n    public let x: i32\n    {declaration}\n");
        var probe = Assert.Single(kotonoha.RootKoto.NestedContainers, static x => x is StructKoto { Name: "Probe" });
        var function = Assert.Single(probe.Members.OfType<FunctionKoto>(), static x => !x.IsImplicitConstructor);
        Assert.Equal(missing, TestDiagnostics.Of(c).Count(static x => x.Code == nameof(Kimi.DiagnosticCode.MissingSyntax_Kd)));
        Assert.Equal((missing == 1, constraints, body), (function.MissingBody, function.TypeConstraints.Count, function.Body is not null));
        ParseTestHelper.VerifyParents(function);
    }

    // The re-classification after an edit (Koto.ReplaceChild) reads the same mark: the node's own Kotonoha.
    [Fact]
    public void AReplacedNameIsClassifiedByItsKotonoha()
    {
        var c = Compilation.CreateForTest();
        foreach (var (kotonoha, expected) in new[] { (c.Library.Kotonoha, AttributeKind.Unsupported), (c.Kotonoha, AttributeKind.Unknown) })
        {
            var attribute = ParseAttribute(kotonoha, "#Test(1) struct S");
            Assert.Equal(AttributeKind.Test, attribute.Kind);
            var call = Assert.IsType<InvocationKoto>(attribute.Operand);
            Assert.True(KotoHelper.Replace(call, call.Method, new IdentifierNameKoto(call, "Unsupported")));
            Assert.Equal(expected, attribute.Kind);
        }
    }

    [Fact]
    public void AttributeKindFollowsAReplacedName()
    {
        var kotonoha = Compilation.CreateForTest().Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "#Layout(\"C\") func Run() => ()");
        var attribute = Assert.Single(KotoTree.Walk(kotonoha.RootKoto).OfType<AttributeKoto>());
        Assert.Equal("C", attribute.LayoutMode);
        var call = Assert.IsType<InvocationKoto>(attribute.Operand);
        Assert.True(KotoHelper.Replace(call, call.Method, new IdentifierNameKoto(call, "Test")));
        Assert.Equal(AttributeKind.Test, attribute.Kind);
        Assert.Null(attribute.LayoutMode);

        // SyntaxNesting's cut of the operand, which leaves no Name.
        Assert.True(KotoHelper.Replace(attribute, attribute.Operand, new ErrorKoto(attribute.CodeContext, attribute.Span)));
        Assert.Equal(AttributeKind.Unknown, attribute.Kind);
    }

    [Fact]
    public void ReplacingTheHeadAttributeKeepsTheRemainingChain()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "#First #Second func Run() => ()\n#Third func Other() => ()");
        Assert.Empty(TestDiagnostics.Of(kotonoha));
        var functions = kotonoha.GeneratedFunction!.Body!.Items.Cast<FunctionKoto>().ToArray();
        var head = functions[0].AttributeChain!;
        var rest = head.AttributeChain!;
        var replacement = functions[1].AttributeChain!;
        Assert.True(functions[1].RemoveAttribute(replacement));

        Assert.True(KotoHelper.Replace(functions[0], head, replacement));
        Assert.Same(replacement, functions[0].AttributeChain);
        Assert.Same(rest, replacement.AttributeChain);
        Assert.Same(replacement, rest.Parent);
        Assert.Same(functions[0], replacement.Parent);
        Assert.Null(head.Parent);
        Assert.Null(head.AttributeChain);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedReplacementLeavesBothSlotsAndSourceMetadataUnchanged(bool sameParent)
    {
        var tree = ParseTestHelper.ParseSuccess("func Run() => call(1, 22)");
        var other = sameParent ? tree : ParseTestHelper.ParseSuccess("func Other() => call(333)");
        var target = Assert.IsType<InvocationKoto>(Assert.IsType<FunctionKoto>(tree.GeneratedFunction!.Body!.Items[0]).ExpressionBody);
        var donor = Assert.IsType<InvocationKoto>(Assert.IsType<FunctionKoto>(other.GeneratedFunction!.Body!.Items[0]).ExpressionBody);
        var old = target.ArgumentNodes[0];
        var replacement = donor.ArgumentNodes[^1];
        var context = replacement.CodeContext;
        var span = replacement.Span;
        var version = tree.Syntax.Version;
        var donorVersion = other.Syntax.Version;
        var source = ParseTestHelper.Unparse(tree);
        var donorSource = ParseTestHelper.Unparse(other);

        Assert.Throws<InvalidOperationException>(() => KotoHelper.Replace(target, old, replacement));

        Assert.Same(old, target.ArgumentNodes[0]);
        Assert.Same(replacement, donor.ArgumentNodes[^1]);
        Assert.Same(target, old.Parent);
        Assert.Same(donor, replacement.Parent);
        Assert.Same(context, replacement.CodeContext);
        Assert.Equal(span, replacement.Span);
        Assert.Equal(version, tree.Syntax.Version);
        Assert.Equal(donorVersion, other.Syntax.Version);
        Assert.Equal(source, ParseTestHelper.Unparse(tree));
        Assert.Equal(donorSource, ParseTestHelper.Unparse(other));
        ParseTestHelper.VerifyParents(tree.RootKoto);
        ParseTestHelper.VerifyParents(other.RootKoto);
    }

    [Fact]
    public void AttachedAttributeReplacementLeavesBothChainsUnchanged()
    {
        var tree = ParseTestHelper.ParseSuccess("#First #Second func Run() => ()\n#Third func Other() => ()");
        var functions = tree.GeneratedFunction!.Body!.Items.Cast<FunctionKoto>().ToArray();
        var old = functions[0].AttributeChain!;
        var rest = old.AttributeChain!;
        var replacement = functions[1].AttributeChain!;
        var context = replacement.CodeContext;
        var span = replacement.Span;
        var version = tree.Syntax.Version;

        Assert.Throws<InvalidOperationException>(() => KotoHelper.Replace(functions[0], old, replacement));

        Assert.Same(old, functions[0].AttributeChain);
        Assert.Same(rest, old.AttributeChain);
        Assert.Same(old, rest.Parent);
        Assert.Same(replacement, functions[1].AttributeChain);
        Assert.Null(replacement.AttributeChain);
        Assert.Same(context, replacement.CodeContext);
        Assert.Equal(span, replacement.Span);
        Assert.Equal(version, tree.Syntax.Version);
        ParseTestHelper.VerifyParents(tree.RootKoto);
    }

    // The one attribute a parse of the source adds to the Kotonoha's tree.
    private static AttributeKoto ParseAttribute(Kotonoha kotonoha, string source)
    {
        var before = KotoTree.Walk(kotonoha.RootKoto).OfType<AttributeKoto>().ToHashSet();
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        return Assert.Single(KotoTree.Walk(kotonoha.RootKoto).OfType<AttributeKoto>(), x => !before.Contains(x));
    }
}
