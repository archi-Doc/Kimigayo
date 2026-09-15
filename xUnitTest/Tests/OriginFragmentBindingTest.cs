// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginFragmentBindingTest
{
    [Theory]
    [InlineData("a, b", -1, -1)]
    [InlineData("a : static, b", -2, -1)]
    [InlineData("a, b : a", -1, 0)]
    [InlineData("a : b, b", 1, -1)]
    [InlineData("a : a, b", 0, -1)]
    [InlineData("a : b, b : a", 1, 0)]
    public void MatchingFragmentsResolveToSharedSlotsAcrossSources(string origins, int firstTarget, int secondTarget)
    {
        foreach (var reverse in new[] { false, true })
        {
            var c = Create();
            AddFragments(c, $"struct S<a> origin {origins}", $"struct S<a> origin {origins}", reverse);
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
                var bounded = firstTarget != -1 || secondTarget != -1;
                Assert.Equal(!bounded, c.Bind().IsComplete);
                Assert.DoesNotContain(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Duplicate);
                Assert.DoesNotContain(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidOrigin);
                var declaration = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
                Assert.False(declaration.HasIncompatibleBindingHeader);
                Assert.Equal(bounded ? BindingFailure.Unsupported : BindingFailure.None, declaration.BindingFailure);
                var schema = declaration.BoundSymbol!.Schema!;
                Assert.Equal(2, schema.Origins.Count);
                CheckBound(0, firstTarget);
                CheckBound(1, secondTarget);

                void CheckBound(int slot, int target)
                {
                    Assert.Same(declaration, schema.Origins[slot].Origin.Binder);
                    Assert.Equal(slot, schema.Origins[slot].Origin.Slot);
                    Assert.Same(target == -1 ? null : target == -2 ? BoundOrigin.Static : schema.Origins[target].Origin, schema.Origins[slot].Bound);
                }
            }
        }
    }

    [Theory]
    [InlineData("a, b : a", "a, b")]
    [InlineData("a, b : a", "a, b : static")]
    [InlineData("a, b : a", "a, b : b")]
    [InlineData("a : b, b : a", "a : a, b : b")]
    [InlineData("a : static, b", "a, b : static")]
    [InlineData("a, b", "b, a")]
    [InlineData("a, b", "a, c")]
    [InlineData("a, b", "a")]
    public void IncompatibleOriginHeadersAreRejectedInEitherOrder(string first, string second)
    {
        foreach (var reverse in new[] { false, true })
        {
            var c = Create();
            AddFragments(c, $"struct S origin {first}", $"struct S origin {second}", reverse);
            Verify(c);
            Verify(Reload(c));
        }

        static void Verify(Compilation c)
        {
            Assert.False(c.Bind().IsComplete);
            var declaration = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            Assert.True(declaration.HasIncompatibleBindingHeader);
            Assert.Equal(BindingFailure.Duplicate, declaration.BindingFailure);
        }
    }

    [Theory]
    [InlineData("a : missing", false)]
    [InlineData("a : b", false)]
    [InlineData("a, a", true)]
    public void MatchingInvalidHeadersNeverBecomeValidThroughMerging(string origins, bool duplicate)
    {
        var c = Create();
        AddFragments(c, $"struct S origin {origins}", $"struct S origin {origins}", false);
        Verify(c);
        Verify(Reload(c));

        void Verify(Compilation c)
        {
            Assert.False(c.Bind().IsComplete);
            var declaration = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            Assert.Equal(duplicate ? BindingFailure.Duplicate : BindingFailure.InvalidOrigin, declaration.BindingFailure);
            Assert.All(declaration.BoundSymbol!.Schema!.Origins, x => Assert.Null(x.Bound));
        }
    }

    [Fact]
    public void SameSpelledOriginsOnDifferentTypeIdentitiesRemainDistinct()
    {
        var c = Create();
        AddFragments(c, "struct S origin a, b : a", "struct S<T> origin a, b : a", false);
        Assert.False(c.Bind().IsComplete);
        var declarations = c.Kotonoha.RootKoto.NestedContainers.Where(x => x.Name == "S").ToArray();
        Assert.Equal(2, declarations.Length);
        var left = declarations[0].BoundSymbol!.Schema!;
        var right = declarations[1].BoundSymbol!.Schema!;
        Assert.Same(left.Origins[0].Origin, left.Origins[1].Bound);
        Assert.Same(right.Origins[0].Origin, right.Origins[1].Bound);
        Assert.NotSame(left.Origins[1].Bound, right.Origins[1].Bound);
    }

    [Fact]
    public void AddingABoundedFragmentInvalidatesTheUnboundedDeclaration()
    {
        var c = Create();
        c.Kotonoha.AddSource(new SourceDocument("first.kimi", "struct S origin a, b"));
        Assert.True(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("second.kimi", "struct S origin a, b : a"));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Duplicate, Assert.Single(c.Kotonoha.RootKoto.NestedContainers).BindingFailure);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void WarmUnboundedFragmentBindingAllocatesNothing()
    {
        var c = Create();
        AddFragments(c, "struct S origin a, b", "struct S origin a, b", false);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

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
        c.Kotonoha.AddSource(new SourceDocument("types.kimi", "group Left\n    public struct a\n    public struct b\ngroup Right\n    public struct a\n    public struct b"));
        var left = new SourceDocument("left.kimi", "alias Left\n" + first);
        var right = new SourceDocument("right.kimi", "alias Right\n" + second);
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
