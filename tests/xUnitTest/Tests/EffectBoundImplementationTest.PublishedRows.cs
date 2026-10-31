// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): the library
// bodies verified against the rows SPEC 4.9.6 gives Array `truncate` and `removeAll`.
public partial class EffectBoundImplementationTest
{
    // SPEC 4.9.6 (PLAN G264 U0′): the Kimi bodies of Array `truncate` and `removeAll` are verified against their rows as every published
    // operation's body is (`PublishedOperationBodiesStayWithinTheirRows`): `truncate`'s `pop` and its Option's destruction, and `removeAll`'s
    // calls of `matching` through its Callable input and its `truncate`, stay within what the row publishes. Fail-before: neither has a row.
    [Theory]
    [InlineData("truncate")]
    [InlineData("removeAll")]
    public void ArrayTruncateAndRemoveAllStayWithinTheirRows(string name)
    {
        var c = MinimalEmissionTest.Analyze(Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var array = (DeclarationContainerKoto)c.Library.GetSymbol(KimiDeclarationId.Array)!.Declaration;
        var operation = array.Members.OfType<FunctionKoto>().Single(x => x.Name == name).SymbolOf()!;
        Assert.True(KimiLibraryCatalog.Row(operation).HasValue, $"{name}: no row");
        var (valid, violation, node) = c.Binding.SummarizePublishedOperation(operation);
        Assert.True(valid, $"{name}: {violation} at {node}");
    }
}
