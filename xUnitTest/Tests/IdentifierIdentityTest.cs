// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class IdentifierIdentityTest
{
    [Theory]
    [InlineData("Dog")]
    [InlineData("日本語")]
    [InlineData("\u00e9")]
    [InlineData("q\u0301")]
    [InlineData("\U00010400name")]
    [InlineData("\uff21")]
    public void AcceptsNfcNamesWithoutChangingTheirSpelling(string name)
    {
        Assert.True(IdentifierHelper.IsValidIdentifier(name));
        var tree = ParseSuccess($"let {name} = 1\n{name}");
        var items = tree.GeneratedFunction!.Body!.Items;
        Assert.Equal(name, Assert.IsType<FieldKoto>(items[0]).NameKoto.IdentifierName);
        Assert.Equal(name, Assert.IsType<IdentifierNameKoto>(items[1]).IdentifierName);
    }

    [Theory]
    [InlineData("e\u0301")]
    [InlineData("\u212b")]
    [InlineData("a\u0315\u0300")]
    [InlineData("a\u200c")]
    [InlineData("a\u200d")]
    [InlineData("a\u202e")]
    [InlineData("a\u2066")]
    [InlineData("a\ufeff")]
    [InlineData("a\U000E0001")]
    public void RejectsInvalidNamesInDeclarationsReferencesTypesAndLabels(string name)
    {
        Assert.False(IdentifierHelper.IsValidIdentifier(name));
        foreach (var source in new[] { $"let {name} = 1", name, $"let item: {name}", $"call({name}: 1)" })
        {
            var diagnostics = Parse(source).DiagnosticCollection.GetArray();
            Assert.Contains(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.InvalidIdentifier_Kd) && x.Entry.Severity == DiagnosticSeverity.Error);
        }
    }

    [Theory]
    [InlineData("Dog", "dog")]
    [InlineData("A", "\uff21")]
    [InlineData("a", "\u0430")]
    [InlineData("I", "\u0131")]
    public void KeepsCaseCompatibilityAndConfusableNamesDistinct(string first, string second)
    {
        var compilation = Compilation.CreateForTest();
        var firstName = compilation.Intern(first);
        var secondName = compilation.Intern(second);
        Assert.NotEqual(firstName, secondName);
        Assert.Same(firstName, compilation.Intern(first));
        Assert.Same(secondName, compilation.Intern(second));
        var tree = ParseSuccess($"let {first} = 1\nlet {second} = 2\n{first}\n{second}");
        Assert.Equal(4, tree.GeneratedFunction!.Body!.Items.Count);
    }

    [Fact]
    public void ValidatesLongUncachedNamesAndRejectsMalformedUtf16()
    {
        var prefix = new string('a', 80);
        ParseSuccess($"let {prefix}\u00e9 = 1");
        foreach (var suffix in new[] { "e\u0301", "\u200d" })
        {
            Assert.Contains(
                Parse($"let {prefix}{suffix} = 1").DiagnosticCollection.GetArray(),
                x => x.Entry.Name == nameof(DiagnosticCode.InvalidIdentifier_Kd));
        }

        Assert.False(IdentifierHelper.IsValidIdentifier("a\ud800"));
        Assert.False(IdentifierHelper.IsValidIdentifier("\udc00"));
    }

    [Fact]
    public void LeavesCommentAndLiteralContentsUnrestrictedByNameRules()
        => ParseSuccess("// e\u0301\u202e\n/* e\u0301\u200d */\nlet text = \"e\u0301\u200d\"\nlet character = '\u200d'");
}
