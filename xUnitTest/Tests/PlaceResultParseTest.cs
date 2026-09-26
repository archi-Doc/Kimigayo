// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

// SPEC 7.1.1: `place ref/T` and `place uniq/T` are result categories, recognized only in result position when
// `place` is followed by `ref` or `uniq` and a slash. SPEC 8.4: a Contract declares Type parameters.
public class PlaceResultParseTest
{
    [Fact]
    public void ParsesPlaceResultsOfFunctionsAndFunctionTypes()
    {
        var tree = ParseSuccess(
            """
            func first<T>(values: ref/Array<T>) -> place ref/T during values
                return values[0]
            func slot(values: uniq/Array<ref/Node>) -> place uniq/ref/Node during values
                return values[0]
            let f: (ref/Array<i32>) -> place ref/i32 = first
            let g = func (values: ref/Array<i32>) -> place uniq/i32 => values[0]
            """);
        var items = GetChildren(tree.RootKoto);
        var first = Assert.IsType<FunctionKoto>(items[0]);
        var place = Assert.IsType<PlaceResultKoto>(first.ReturnType);
        Assert.False(place.IsExclusive);
        Assert.Equal(SemanticsKind.Ref, place.SemanticsKind);
        Assert.Equal("values", place.OriginName);
        Assert.Equal("place ref/T during values", place.ToString());

        var slot = Assert.IsType<FunctionKoto>(items[1]);
        var exclusive = Assert.IsType<PlaceResultKoto>(slot.ReturnType);
        Assert.True(exclusive.IsExclusive);
        var reference = Assert.IsType<TypeSemanticsKoto>(exclusive.Type);
        Assert.Equal(SemanticsKind.Uniq, reference.SemanticsKind);
        Assert.Equal(SemanticsKind.Ref, Assert.IsType<TypeSemanticsKoto>(reference.Type).SemanticsKind);

        var functionType = Assert.IsType<FunctionTypeKoto>(Assert.IsType<FieldKoto>(items[2]).TypeKoto);
        Assert.IsType<PlaceResultKoto>(functionType.ReturnType);
        var anonymous = Assert.IsType<FunctionKoto>(Assert.IsType<FieldKoto>(items[3]).InitializerKoto);
        Assert.True(Assert.IsType<PlaceResultKoto>(anonymous.ReturnType).IsExclusive);
    }

    [Fact]
    public void ParsesContractTypeParametersAndPlaceRequirements()
    {
        var tree = ParseSuccess(
            """
            contract Indexable<Key>
                associate Element
                func index(self, key: ref/Key) -> place ref/Element during self
            contract UniqIndexable<Key>: Indexable<Key>
                func indexUniq(self: uniq/Self, key: ref/Key) -> place uniq/Element during self
            """);
        var contracts = KotoTree.Walk(tree.RootKoto).OfType<ContractKoto>().ToArray();
        Assert.Equal(2, contracts.Length);
        var indexable = contracts[0];
        Assert.Equal("Key", Assert.Single(indexable.GenericParameterNodes).Identifier);
        var index = Assert.Single(indexable.Members.OfType<FunctionKoto>());
        Assert.True(index.IsRequirement);
        Assert.IsType<PlaceResultKoto>(index.ReturnType);

        var refining = contracts[1];
        Assert.Equal("Key", Assert.Single(refining.GenericParameterNodes).Identifier);
        Assert.Single(refining.Bases);
        Assert.True(Assert.IsType<PlaceResultKoto>(Assert.Single(refining.Members.OfType<FunctionKoto>()).ReturnType).IsExclusive);
    }

    [Theory]
    [InlineData("func f() -> place T => 0")]
    [InlineData("func f() -> place owner/T => 0")]
    [InlineData("func f() -> place ref => 0")]
    [InlineData("func f(x: place ref/i32) => 0")]
    [InlineData("let x: place ref/i32 = 0")]
    public void PlaceOutsideResultPositionOrWithoutReferenceIsNotAPlaceResult(string source)
    {
        var tree = Parse(source);
        Assert.DoesNotContain(KotoTree.Walk(tree.RootKoto), x => x is PlaceResultKoto);
    }

    [Fact]
    public void PlaceResultRoundTripsThroughUnparse()
    {
        const string Source = """
            func first<T>(values: ref/Array<T>) -> place ref/T during values
                return values[0]
            """;
        var function = ParseSingleFunction(Source);
        Assert.Contains("-> place ref/T during values", function.ToString());
    }
}
