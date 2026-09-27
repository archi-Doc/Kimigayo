// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Every syntax edit revokes the whole source analysis, wherever it is; only Binding's own normalization
/// while it binds is not an edit.</summary>
public class SyntaxEditInvalidationTest
{
    private const string Source =
        "struct S\n    public var f: i32\n    public init(f: i32) => self.f = f\n" +
        "group G\n    func twice(n: i32) -> i32 => n * 2\n" +
        "    func read(s: ref/S) -> i32\n        return twice(s.f)\n";

    [Fact]
    public void AFieldEditOutsideCallablesRevokesTheAnalysis()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        var field = Field(c);
        var donor = Field(MinimalEmissionTest.Analyze(Source.Replace("public var f: i32", "public var f: string", StringComparison.Ordinal)));
        Assert.True(KotoHelper.Replace(field, field.TypeKoto!, donor.TypeKoto!));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Bind().IsComplete); // twice(s.f) no longer fits.
    }

    [Fact]
    public void AnArgumentEditRevokesTheAnalysis()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        var call = Call(c);
        // Detach a donor argument by swapping another throwaway node into its slot.
        var donor = Call(MinimalEmissionTest.Analyze(Source.Replace("return twice(s.f)", "return twice(3)", StringComparison.Ordinal)));
        var replacement = donor.ArgumentNodes[0];
        var filler = Call(MinimalEmissionTest.Analyze(Source.Replace("return twice(s.f)", "return twice(4)", StringComparison.Ordinal))).ArgumentNodes[0];
        Assert.True(KotoHelper.Replace(donor, replacement, filler));
        call.ReplaceArgument(0, replacement);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
    }

    private static VariableKoto Field(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<VariableKoto>().Single();

    private static InvocationKoto Call(Compilation c)
    {
        var read = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "read");
        var statement = (ReturnKoto)read.Body!.Items[0];
        return (InvocationKoto)statement.Expression!;
    }
}
