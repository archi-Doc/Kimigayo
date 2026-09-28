// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedCollectionFormationTest
{
    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void SliceFamilyPublishesElementFormationDomain(string relation, bool valid)
    {
        var source = "contract C\n    associate Item(a, b) is Slice<ref/i32 during a> during b\nstruct S\n    Self is C\nfunc f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    " + relation + "\n    $abort(\"unused\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (valid)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
    }

    [Fact]
    public void SliceInputProvesItsElementFormationDomain()
    {
        const string source = "contract C\n    associate Item(a, b) is Slice<ref/i32 during a> during b\nstruct S\n    Self is C\nfunc f(value: Slice<ref/i32 during a> during b) -> S.Item(a, b) => value\nlet value = 7\nlet refs: [1 of ref/i32] = [value@ref]\nlet result = f(refs[..])\nrequire result[0] == 7 else => $abort(\"slice refs\")\nConsole.writeLine(\"slice refs\")";
        ScalarEmissionTest.EmitFixture("AssociatedCollectionSliceReferences", source, "slice refs\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SliceFamilyKeepsBackingLoan(bool conflict)
    {
        var source = "contract C\n    associate Item(a) is Slice<i32> during a\nstruct S\n    Self is C\nfunc f(value: Slice<i32> during a) -> S.Item(a) => value\nvar values: [2 of i32] = [7, 9]\nlet result = f(values[..])\n" + (conflict ? "values[0] = 11\n" : string.Empty) + "require result[1] == 9 else => $abort(\"slice\")\nConsole.writeLine(\"slice\")";
        Check("AssociatedCollectionSlice", source, "slice\n", conflict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayFamilyKeepsElementLoan(bool conflict)
    {
        var source = "contract C\n    associate Item(a) is Array<ref/i32 during a>\nstruct S\n    Self is C\nfunc f(value: ref/i32 during a) -> S.Item(a)\n    var result: Array<ref/i32 during a> = []\n    result.append(value)\n    return result@move\nvar value = 7\nlet result = f(value@ref)\n" + (conflict ? "value = 9\n" : string.Empty) + "require result[0] == 7 else => $abort(\"array\")\nConsole.writeLine(\"array\")";
        Check("AssociatedCollectionArray", source, "array\n", conflict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryFamilyKeepsValueLoan(bool conflict)
    {
        var source = "contract C\n    associate Item(a) is Dictionary<i32, ref/i32 during a>\nstruct S\n    Self is C\nfunc f(value: ref/i32 during a) -> S.Item(a)\n    var result: Dictionary<i32, ref/i32 during a> = [:]\n    _ = result.tryInsert(1, value)\n    return result@move\nvar value = 7\nlet result = f(value@ref)\n" + (conflict ? "value = 9\n" : string.Empty) + "require result[1] == 7 else => $abort(\"dictionary\")\nConsole.writeLine(\"dictionary\")";
        Check("AssociatedCollectionDictionary", source, "dictionary\n", conflict);
    }

    [Theory]
    [InlineData("Array<ref/i32 during a>")]
    [InlineData("Dictionary<i32, ref/i32 during a>")]
    [InlineData("Slice<i32> during a")]
    public void BorrowedCollectionFormationRequiresContainedOrigins(string type)
    {
        foreach (var valid in new[] { false, true })
        {
            var source = "contract C\n    associate Item(a, b) for ref/(" + type + ") during b\nstruct S\n    Self is C\n    associate Item(a, b) is i32\nfunc f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n" + (valid ? "    origin a outlives b\n" : string.Empty) + "    return 42";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        }
    }

    private static void Check(string name, string source, string stdout, bool conflict)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(!conflict, c.Ownership.Result.IsVerified);
        if (!conflict)
        {
            ScalarEmissionTest.EmitFixture(name, source, stdout);
        }
    }
}
