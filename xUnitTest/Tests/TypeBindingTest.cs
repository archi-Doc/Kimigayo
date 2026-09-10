// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TypeBindingTest
{
    [Theory]
    [InlineData("func f origin a(x: ref/i32 from a)")]
    [InlineData("func f origin a, b(x: ref/(uniq/i32 from b) from a)")]
    [InlineData("func f origin a(x: owner/(ref/i32 from a))")]
    [InlineData("struct View<T> origin source\n    let value: ref/T from source\nfunc f origin a(x: View<i32> from a)")]
    [InlineData("struct View<T> origin source\n    let value: ref/T from source\n    func f(self: ref/Self) -> ref/T from self.source => value")]
    [InlineData("func identity<s/T>(x: s/T) -> s/T => x\nlet x = identity(1)")]
    [InlineData("struct Box<T>\n    let value: T\nfunc f origin a(x: Box<ref/i32 from a>)")]
    [InlineData("func f(x: unsafe/obj/Thing)\nstruct Thing")]
    public void SupportedCompleteTypesBind(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("func f(x: ref/ref/i32)", DiagnosticCode.MissingOriginBinding_Kd)]
    [InlineData("struct S\n    let value: ref/i32", DiagnosticCode.MissingOriginBinding_Kd)]
    [InlineData("func f()\n    var value: ref/i32", DiagnosticCode.MissingOriginBinding_Kd)]
    [InlineData("func f origin a, a()", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f<s/s>(x: s/s)", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f<T>(x: T)\nfunc f<s/U>(x: s/U)", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f origin a(x: ref/i32 from a)\nfunc f origin b(x: ref/i32 from b)", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f(x: ref/i32 from absent)", DiagnosticCode.MissingOriginBinding_Kd)]
    [InlineData("func f(x: i32, y: ref/i32 from x)", DiagnosticCode.InvalidOriginBinding_Kd)]
    [InlineData("func f(x: obj/(ref/i32 from static))", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("func f(x: i32 from static)", DiagnosticCode.InvalidOriginBinding_Kd)]
    [InlineData("group G\n    var x: ref/i32 from static", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("struct View origin source\n    let value: ref/i32 from source\nfunc f(x: View)", DiagnosticCode.MissingOriginBinding_Kd)]
    [InlineData("struct View origin source\n    let value: ref/i32 from source\nfunc f origin a(x: View from (wrong => a))", DiagnosticCode.InvalidOriginBinding_Kd)]
    [InlineData("struct View origin source\n    let value: ref/i32 from source\nfunc f origin a(x: View from (source => a, source => a))", DiagnosticCode.InvalidOriginBinding_Kd)]
    public void InvalidTypeAndOriginFormsAreDiagnosed(string source, DiagnosticCode code)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void AppendedStorageChangesInvalidateOriginRequirements()
    {
        var c = Parse("struct View origin source\n    let shared: ref/i32 from source\nfunc inspect(value: View from static) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var view = c.Kotonoha.RootKoto.NestedContainers.Single();
        c.Kotonoha.CreateCodeContext().Parse(view, "let exclusive: uniq/i32 from source");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(LoanRequirement.Uniq, view.BoundSymbol!.Schema!.Origins[0].LoanRequirement);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidOriginBinding_Kd);
    }

    [Fact]
    public void OriginDeclarationSpansAndExpressionBindingsAreRetained()
    {
        var c = Parse("func f origin first, second(x: ref/i32 from first and second) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var schema = f.BoundSymbol!.Schema!;
        var document = f.CodeContext.SourceDocument!;
        Assert.Equal("second", document.AsSpan().Slice(schema.Origins[1].Span.Start, schema.Origins[1].Span.Length).ToString());
        var origins = Nodes(c).OfType<IdentifierNameKoto>().Where(x => x.BoundOrigin is not null).ToArray();
        Assert.Equal(2, origins.Length);
        Assert.Same(schema.Origins[0].Origin, origins[0].BoundOrigin);
    }

    [Fact]
    public void StaticStorageDoesNotTreatPointerPointeesAsRetainedBorrows()
    {
        var c = Parse("struct PointerBox<T>\n    let value: unsafe/T\ngroup Values\n    var p: PointerBox<ref/i32 from static>");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var retained = Parse("struct Box<T>\n    let value: T\ngroup Values\n    var p: Box<ref/i32 from static>");
        Assert.False(retained.Bind().IsComplete);
        Assert.Contains(retained.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    [Fact]
    public void ExclusiveReferentsRemainInvariantWhileSharedOriginsMayShorten()
    {
        var c = Parse("func f origin a, b(x: ref/i32 from a, y: ref/i32 from a and b, u: uniq/(ref/i32 from a) from a, v: uniq/(ref/i32 from a and b) from a) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.True(Binding.FitsType(f.Parameters[0].Type.BoundType!, f.Parameters[1].Type.BoundType!));
        Assert.False(Binding.FitsType(f.Parameters[2].Type.BoundType!, f.Parameters[3].Type.BoundType!));
    }

    [Fact]
    public void GenericWholeTypeSubstitutionPreservesBorrowOrigins()
    {
        var c = Parse("func identity<s/T>(value: s/T) -> s/T => value\nfunc f(value: ref/i32) -> ref/i32 => identity(value)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var call = Nodes(c).OfType<InvocationKoto>().Single();
        Assert.Same(f.Parameters[0].Type.BoundType, call.BoundType);
        Assert.Equal(GenericSlotKind.Pair, call.BoundCall!.Target.Schema!.GenericSlots[0].Kind);
    }

    [Fact]
    public void LocalOmissionsRetainFixedVariablesAndInitializerConstraints()
    {
        var c = Parse("func f(x: ref/i32)\n    var local: ref/i32 = x");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var local = Nodes(c).OfType<FieldKoto>().Single();
        Assert.Equal(OriginKind.Inference, local.BoundType!.Origin!.Kind);
        Assert.Contains(c.Binding.Obligations, x => x.Kind == BindingObligationKind.OriginInference);
        Assert.Contains(c.Binding.Obligations, x => x.Kind == BindingObligationKind.OriginOutlives && ReferenceEquals(x.Shorter, local.BoundType.Origin));
        var origin = local.BoundType.Origin;
        var count = c.Binding.Obligations.Count;
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(origin, local.BoundType.Origin);
        Assert.Equal(count, c.Binding.Obligations.Count);
    }

    [Fact]
    public void LengthSlotsRemainSymbolicAndSignaturesUseSlotIdentity()
    {
        var c = Parse("func f<length N>(x: [(N + 1) of i32]) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Equal(GenericSlotKind.Length, f.BoundSymbol!.Schema!.GenericSlots[0].Kind);
        Assert.NotNull(f.Parameters[0].Type.BoundType!.LengthExpression);
        Assert.Contains(c.Binding.Obligations, x => x.Deadline == BindingDeadline.Instantiation);
        var duplicate = Parse("func f<length N>(x: [(N + 1) of i32]) => ()\nfunc f<length M>(x: [(M + 1) of i32]) => ()");
        Assert.False(duplicate.Bind().IsComplete);
        Assert.Contains(duplicate.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Theory]
    [InlineData("struct A origin a\nstruct A origin b")]
    [InlineData("struct A<T>\nstruct A<U>")]
    public void FragmentSchemasMustAgree(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void RebindingOriginRichHeadersReusesAllScratchStorage()
    {
        var text = new System.Text.StringBuilder("struct View<T> origin a, b\n    let x: ref/T from a\n    let y: ref/T from b\n");
        for (var i = 0; i < 128; i++)
        {
            text.Append($"func function{i} origin a, b(x: View<i32> from (a => a, b => b), y: ref/(ref/i32 from a) from b) => ()\n");
        }

        var c = Parse(text.ToString());
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OriginMappingsAreOrderedByDeclarationAndInterned()
    {
        var c = Parse("struct Pair origin left, right\n    let x: ref/i32 from left\n    let y: ref/i32 from right\nfunc f origin a, b(x: Pair from (left => a, right => b), y: Pair from (right => b, left => a))");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Same(f.Parameters[0].Type.BoundType, f.Parameters[1].Type.BoundType);
        Assert.Equal("a", f.Parameters[0].Type.BoundType!.OriginArguments[0].Name);
        var schema = f.BoundSymbol!.Schema;
        var type = f.Parameters[0].Type.BoundType;
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(schema, f.BoundSymbol.Schema);
        Assert.Same(type, f.Parameters[0].Type.BoundType);
    }

    [Fact]
    public void ResultElisionUsesOnlyDirectInputsAndPreservesExplicitMappings()
    {
        var c = Parse("struct Pair origin left, right\n    let x: ref/i32 from left\n    let y: ref/i32 from right\nfunc f(x: ref/i32, y: ref/i32, result: Pair from (left => x, right => x and y)) -> Pair from (left => x) => result");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var result = f.ReturnType!.BoundType!;
        Assert.Same(f.Parameters[0].Type.BoundType!.Origin, result.OriginArguments[0]);
        Assert.Equal(OriginKind.Intersection, result.OriginArguments[1].Kind);
        Assert.Equal(2, result.OriginArguments[1].Operands.Count);
    }

    [Fact]
    public void IntersectionsNormalizeWithoutLosingNestedOrigins()
    {
        var c = Parse("func f origin a, b(x: ref/(ref/i32 from a) from a and b, y: ref/(ref/i32 from a) from b and a and a)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Same(f.Parameters[0].Type.BoundType, f.Parameters[1].Type.BoundType);
        Assert.Same(f.BoundSymbol!.Schema!.Origins[0].Origin, f.Parameters[0].Type.BoundType!.Components[0].Origin);
    }

    [Fact]
    public void RequirementsPropagateThroughForwardAndRecursiveDeclarations()
    {
        var c = Parse("struct A origin a\n    let b: B from a\nstruct B origin b\n    let a: unsafe/(A from b)\n    let value: uniq/i32 from b");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var a = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "A");
        Assert.Equal(LoanRequirement.Uniq, a.BoundSymbol!.Schema!.Origins[0].LoanRequirement);
    }

    private static Compilation Parse(string source)
    {
        // Header fixtures use a trivial Unit body unless an executable body is supplied.
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("func ", StringComparison.Ordinal))
            {
                continue;
            }

            var depth = 0;
            var hasBody = false;
            for (var j = 0; j + 1 < lines[i].Length; j++)
            {
                if (lines[i][j] == '(')
                {
                    depth++;
                }

                if (lines[i][j] == ')')
                {
                    depth--;
                }

                if (depth == 0 && lines[i][j] == '=' && lines[i][j + 1] == '>')
                {
                    hasBody = true;
                }
            }

            if (hasBody)
            {
                continue;
            }

            var indent = lines[i].Length - lines[i].TrimStart().Length;
            if (i + 1 < lines.Length && lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent)
            {
                continue;
            }

            lines[i] += " => ()";
        }

        source = string.Join('\n', lines);
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));

    private static IEnumerable<Koto> Nodes(Compilation c) => Walk(c.Kotonoha.RootKoto);

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
