// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DictionaryBindingTest
{
    [Theory]
    [InlineData("i32", "string", true)]
    [InlineData("ref/i32", "string", false)]
    [InlineData("i32", "ref/i32", false)]
    public void EmptyCollectionOwnershipFollowsBothStoredTypes(string key, string value, bool owned)
    {
        var source = "func keep<T>(value: T) -> isize\n    T is Owned\n    return 1\nlet entries: Dictionary<" + key + ", " + value + "> = [:]\nlet result = keep(entries@move)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(owned, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void CompilerManagedStorageRejectsExtraSourceFields()
    {
        var c = MinimalEmissionTest.Analyze(string.Empty);
        var declaration = (StructKoto)c.Library.GetSymbol(KimiDeclarationId.Dictionary)!.Declaration;
        c.Library.Kotonoha.CreateCodeContext().Parse(declaration, "public let extra: i32");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.Dictionary));
    }

    [Theory]
    [InlineData("struct Key\n    public init() => ()\nlet entries: Dictionary<Key, i32> = [:]")]
    [InlineData("func use<K, V>(value: Dictionary<K, V>) => ()")]
    [InlineData("func copy<T>(value: T)\n    T is Copy\n    ()\nlet entries: Dictionary<i32, i32> = [:]\ncopy(entries)")]
    public void RejectsUnprovenEqualityAndImplicitDuplication(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("var entries: Dictionary<i32, string> = [:]")]
    [InlineData("var entries: Dictionary<i32, string> = [:]\nentries.reserve(additional: 3)")]
    [InlineData("var entries: Dictionary<i32, string> = [:]\n_ = entries.tryInsert(1, \"one\")")]
    [InlineData("var entries: Dictionary<i32, string> = [:]\n_ = entries.insertOrReplace(1, \"one\")")]
    [InlineData("var entries: Dictionary<i32, string> = [:]\n_ = entries.tryGet(1)")]
    [InlineData("var entries: Dictionary<i32, string> = [:]\n_ = entries.remove(1)")]
    public void BindsDesignatedOperations(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.Dictionary));
    }
}
