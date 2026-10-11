// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginFragmentBindingTest
{
    // SPEC 6.1.2, 15.3.2: fragments that repeat one header, or that all omit it and declare no own slots, share one schema.
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a, b")]
    public void FragmentsShareSchemaAcrossSourcesAndReload(string origins)
    {
        var header = origins.Length == 0 ? string.Empty : $" {{{origins}}}";
        foreach (var reverse in new[] { false, true })
        {
            var c = Create();
            AddFragments(c, $"struct S{header}", $"struct S{header}", reverse);
            Verify(c);
            Verify(CompilationTestHelper.Reload(c));
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
                Assert.Equal(origins.Length != 0, declaration.HasOriginHeader);
                Assert.False(declaration.HasIncompatibleBindingHeader);
                Assert.Equal(origins.Length == 0 ? 0 : origins.Split(',').Length, declaration.SymbolOf()!.Schema!.Origins.Count);
                Assert.All(declaration.SymbolOf()!.Schema!.Origins, x => Assert.Same(declaration, x.Origin.Binder));
            }
        }
    }

    [Theory]
    [InlineData("struct S {a, b}", "struct S {b, a}")]
    [InlineData("struct S {a, b}", "struct S {a, c}")]
    [InlineData("struct S {a, b}", "struct S {a}")]
    [InlineData("struct S", "struct S {a}")]
    public void IncompatibleOrOpenFragmentsAreRejectedInEitherOrder(string first, string second)
    {
        foreach (var reverse in new[] { false, true })
        {
            var c = Create();
            AddFragments(c, first, second, reverse);
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(BindingFailure.Duplicate, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").FailureOf());
            Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("a : static")]
    [InlineData("a : b, b")]
    [InlineData("static")]
    [InlineData("a.source")]
    public void HeaderBoundsAndExpressionsAreRejected(string header)
        => Assert.NotEmpty(TestDiagnostics.Of(ParseTestHelper.Parse($"struct S {{{header}}}")));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OriginRelationsHaveOneSharedDefiningRegion(bool reverse)
    {
        var c = Create();
        AddFragments(c, "struct S {a, b}\n    origin a outlives b", "struct S {a, b}", reverse);
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("third.kimi", "struct S {a, b}\n    origin a outlives b"));
        Assert.True(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
    }

    [Fact]
    public void SameSpelledOriginsOnDifferentTypesRemainDistinct()
    {
        var c = Create();
        AddFragments(c, "struct S {a, b}", "struct S<T> {a, b}", false);
        Assert.True(c.Bind().IsComplete);
        var declarations = c.Kotonoha.RootKoto.NestedContainers.Where(x => x.Name == "S").ToArray();
        Assert.Equal(2, declarations.Length);
        Assert.NotSame(declarations[0].SymbolOf()!.Schema!.Origins[0].Origin, declarations[1].SymbolOf()!.Schema!.Origins[0].Origin);
    }

    [Trait("Purpose", "Allocation")]
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
        Assert.Empty(TestDiagnostics.Of(c));
    }

    private static Compilation Create()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        return c;
    }
}
