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
            Assert.All(invocation.ChildNodes, child => Assert.Same(invocation, child.Parent));
            Assert.All(generics.ChildNodes, child => Assert.Same(generics, child.Parent));
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
        var oldArgument = first.ChildNodes.Last();
        var replacementArgument = second.ChildNodes.Last();
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
}
