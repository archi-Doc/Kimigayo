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
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "struct A<T> origin first");
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
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
    [InlineData("a\u0301", true)]
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
            Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
            var field = Assert.IsType<FieldKoto>(Assert.Single(kotonoha.GeneratedFunction!.Body!.Items));
            Assert.Same(cached, field.NameKoto.IdentifierName);
        }
        else
        {
            Assert.NotEmpty(kotonoha.DiagnosticCollection.GetArray());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(20)]
    public void PreservesCompactArgumentsAndBlocksThroughSerialization(int count)
    {
        var arguments = string.Join(", ", Enumerable.Range(0, count).Select(i => i == count / 2 ? $"label: {i}" : i.ToString()));
        var types = string.Join(", ", Enumerable.Range(0, Math.Max(1, count)).Select(i => $"T{i}"));
        var source = $"func Run()\n    call<{types}>({arguments})";
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());

        // Serialize before accessing mutable Arguments, while the parser's compact storage is intact.
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
                Assert.Equal(i == count / 2 ? "label" : null, invocation.GetArgumentLabel(i));
            }
        }
    }

    [Fact]
    public void ReplacesChildrenBeforeAndAfterMaterializingArgumentLists()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "func Run()\n    call<A>(1)\n    replacement<B>(2)");
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        var function = Assert.IsType<FunctionKoto>(Assert.Single(kotonoha.GeneratedFunction!.Body!.Items));
        var body = function.Body!;
        var first = Assert.IsType<InvocationKoto>(body.Items[0]);
        var second = Assert.IsType<InvocationKoto>(body.Items[1]);
        var firstGeneric = Assert.IsType<GenericsKoto>(first.Method);
        var secondGeneric = Assert.IsType<GenericsKoto>(second.Method);

        Assert.True(KotoHelper.Replace(firstGeneric, firstGeneric.TypeArguments[0], secondGeneric.TypeArguments[0]));
        Assert.Equal("call<B>(1)", first.ToString());
        var oldArgument = first.ChildNodes.Last();
        var replacementArgument = second.ChildNodes.Last();
        Assert.True(KotoHelper.Replace(first, oldArgument, replacementArgument));
        Assert.Null(oldArgument.Parent);
        Assert.Same(first, replacementArgument.Parent);
        Assert.Equal("call<B>(2)", first.ToString());

        var mutableArguments = first.Arguments;
        Assert.True(KotoHelper.Replace(first, mutableArguments[0], oldArgument));
        Assert.Same(oldArgument, mutableArguments[0]);
        Assert.True(KotoHelper.Replace(body, first, second));
        Assert.Same(second, body.Items[0]);
        Assert.Same(body, second.Parent);
    }

    [Fact]
    public void AttributeViewsFollowReplacedOperands()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "#First(1) func Run()\n#Second(2) func Other()");
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        var functions = kotonoha.GeneratedFunction!.Body!.Items.Cast<FunctionKoto>().ToArray();
        var attribute = functions[0].AttributeChain!;
        var replacement = functions[1].AttributeChain!.Operand;
        Assert.Equal("First", attribute.IdentifierKoto.ToString());
        Assert.Equal("1", Assert.Single(attribute.Arguments).ToString());
        Assert.True(KotoHelper.Replace(attribute, attribute.Operand, replacement));
        Assert.Equal("Second", attribute.IdentifierKoto.ToString());
        Assert.Equal("2", Assert.Single(attribute.Arguments).ToString());
    }
}
