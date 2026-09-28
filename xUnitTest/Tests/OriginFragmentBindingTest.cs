// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginFragmentBindingTest
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a, b")]
    public void ClosedFragmentsShareSchemaAcrossSourcesAndReload(string origins)
    {
        foreach (var reverse in new[] { false, true })
        {
            var c = Create();
            AddFragments(c, $"struct S {{{origins}}}", $"struct S {{{origins}}}", reverse);
            Verify(c);
            Verify(Reload(c));
            var builder = default(IndentedStringBuilder);
            try
            {
                c.Kotonoha.RootKoto.UnparseAll(ref builder);
                var written = Create();
                written.Kotonoha.AddSource(new SourceDocument("written.kimi", builder.ToString()));
                Verify(written);
            }
            finally
            {
                builder.Dispose();
            }
        }

        void Verify(Compilation c)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
                var declaration = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
                Assert.True(declaration.HasOriginHeader);
                Assert.False(declaration.HasIncompatibleBindingHeader);
                Assert.Equal(origins.Length == 0 ? 0 : origins.Split(',').Length, declaration.BoundSymbol!.Schema!.Origins.Count);
                Assert.All(declaration.BoundSymbol.Schema.Origins, x => Assert.Same(declaration, x.Origin.Binder));
            }
        }
    }

    [Theory]
    [InlineData("struct S {a, b}", "struct S {b, a}")]
    [InlineData("struct S {a, b}", "struct S {a, c}")]
    [InlineData("struct S {a, b}", "struct S {a}")]
    [InlineData("struct S {}", "struct S {a}")]
    [InlineData("struct S", "struct S {}")]
    [InlineData("struct S", "struct S")]
    public void IncompatibleOrOpenFragmentsAreRejectedInEitherOrder(string first, string second)
    {
        foreach (var reverse in new[] { false, true })
        {
            var c = Create();
            AddFragments(c, first, second, reverse);
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(BindingFailure.Duplicate, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").BindingFailure);
            Assert.False(Reload(c).Bind().IsComplete);
        }
    }

    [Theory]
    [InlineData("a : static")]
    [InlineData("a : b, b")]
    [InlineData("static")]
    [InlineData("a.source")]
    public void HeaderBoundsAndExpressionsAreRejected(string header)
        => Assert.NotEmpty(ParseTestHelper.Parse($"struct S {{{header}}}").DiagnosticCollection.GetArray());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OriginRelationsHaveOneSharedDefiningRegion(bool reverse)
    {
        var c = Create();
        AddFragments(c, "struct S {a, b}\n    origin a outlives b", "struct S {a, b}", reverse);
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(Reload(c).Bind().IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("third.kimi", "struct S {a, b}\n    origin a outlives b"));
        Assert.True(c.Kotonoha.HasSourceErrors);
    }

    [Fact]
    public void SameSpelledOriginsOnDifferentTypesRemainDistinct()
    {
        var c = Create();
        AddFragments(c, "struct S {a, b}", "struct S<T> {a, b}", false);
        Assert.True(c.Bind().IsComplete);
        var declarations = c.Kotonoha.RootKoto.NestedContainers.Where(x => x.Name == "S").ToArray();
        Assert.Equal(2, declarations.Length);
        Assert.NotSame(declarations[0].BoundSymbol!.Schema!.Origins[0].Origin, declarations[1].BoundSymbol!.Schema!.Origins[0].Origin);
    }

    [Fact]
    public void WarmFragmentBindingAllocatesNothing()
    {
        var c = Create();
        AddFragments(c, "struct S {a, b}\n    origin a outlives b", "struct S {a, b}", false);
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Origin fragment Binding failed.");
            }
        }));
    }

    private static void AddFragments(Compilation c, string first, string second, bool reverse)
    {
        var left = new SourceDocument("left.kimi", first);
        var right = new SourceDocument("right.kimi", second);
        c.Kotonoha.AddSource(reverse ? right : left);
        c.Kotonoha.AddSource(reverse ? left : right);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    private static Compilation Create()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        return c;
    }

    private static Compilation Reload(Compilation c)
    {
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Create();
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        return restored;
    }
}
