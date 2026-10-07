// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class HoverImprovementsTest
{
    [Fact]
    public void ParameterAndReferencesKeepCompleteTypesCoreDocumentationAndParameterItem()
    {
        const string source = "/// The counter.\nstruct Counter\n/// Borrows a counter.\n/// - counter: The supplied counter.\n/// - other: Not this parameter.\nfunc borrowCounter(counter: ref/Counter, other: i32) -> ref/Counter during counter\n    return counter\n";
        var document = Project(source);
        var declared = At(document, source.IndexOf("counter: ref", StringComparison.Ordinal));
        var used = At(document, source.LastIndexOf("counter", StringComparison.Ordinal));
        Assert.Equal(declared.Info.Variable, used.Info.Variable);
        Assert.StartsWith("counter: ref/Counter during ", used.Info.Variable!.Declaration.Header);
        Assert.EndsWith("borrowCounter", used.Info.Variable.Declaration.Owner);
        Assert.StartsWith("ref — Shared, non-owning", used.Info.Variable.Semantics);
        Assert.Equal("Core: Counter", used.Info.Variable.Target);
        foreach (var markdown in new[] { false, true })
        {
            var rendered = Render(used, markdown);
            var body = markdown ? Markdig.Markdown.ToHtml(rendered) : rendered;
            Assert.Contains("The supplied counter.", body);
            Assert.DoesNotContain("Not this parameter", body);
            Assert.DoesNotContain("Borrows a counter.", body);
            Assert.Contains("The counter.", body);
            Assert.True(body.IndexOf("The supplied counter.", StringComparison.Ordinal) < body.IndexOf("Semantics:", StringComparison.Ordinal));
            Assert.DoesNotContain("## Function", body);
            Assert.DoesNotContain("Built-in type", body);
            Assert.EndsWith(markdown ? "(main\\.kimi)" : "(main.kimi)", rendered);
        }
    }

    [Fact]
    public void InferredLocalsShadowedNamesAndNestedReferencesKeepTheirOwnBindings()
    {
        const string source = "struct Counter\nfunc inspect(counter: ref/obj/Counter)\n    let value = 1\n    _ = value\n    if true\n        let value = false\n        _ = value\n    _ = counter\n";
        var document = Project(source);
        var outer = At(document, source.IndexOf("value =", StringComparison.Ordinal));
        var inner = At(document, source.LastIndexOf("value =", StringComparison.Ordinal));
        Assert.Equal("value: i32", outer.Info.Variable!.Declaration.Header);
        Assert.Equal("value: bool", inner.Info.Variable!.Declaration.Header);
        Assert.Same(outer.Info, At(document, source.IndexOf("_ = value", StringComparison.Ordinal) + 4).Info);
        Assert.False(new HoverAgreement().Equal(outer, inner with { Span = outer.Span }));
        var nested = At(document, source.LastIndexOf("counter", StringComparison.Ordinal));
        Assert.Equal("obj/Counter", nested.Info.Variable!.Referent);
        Assert.Equal("Core: Counter", nested.Info.Variable.Target);
        Assert.Contains("ref/obj/Counter during ", nested.Info.CopyType);
    }

    [Fact]
    public void GenericTypesAndSemanticsKeepDeclarationsAndConstraints()
    {
        const string source = "func inspect<s/T>(value: s/T)\n    s is value or valueborrow\n    T is Copy\n    _ = value\n";
        var entry = At(Project(source), source.LastIndexOf("value", StringComparison.Ordinal));
        Assert.StartsWith("s/T", entry.Info.CopyType);
        Assert.StartsWith("s — Semantics parameter.", entry.Info.Variable!.Semantics);
        Assert.Contains("s is value or valueborrow", entry.Info.Variable.Semantics);
        Assert.Contains("T is Copy", entry.Info.Declarations[0].Details);
        var applied = At(Project(source), source.IndexOf("value: s/T", StringComparison.Ordinal) + 7);
        Assert.Contains("Semantics: s — Semantics parameter.", applied.Info.Use);
    }

    [Fact]
    public void WholeTypeParametersDoNotInventOwnerSemanticsOrAConcreteCore()
    {
        const string source = "func inspect<T>(value: T)\n    T is Copy\n    _ = value\n";
        var entry = At(Project(source), source.LastIndexOf("value", StringComparison.Ordinal));
        Assert.Equal("Type parameter: T", entry.Info.Variable!.Target);
        Assert.StartsWith("Undetermined for T.", entry.Info.Variable.Semantics);
        Assert.Contains("T is Copy", entry.Info.Declarations[0].Details);
    }

    [Fact]
    public void RenamedParameterUsesItsInternalBindingAndExternalDocumentationName()
    {
        const string source = "/// - input: The argument.\nfunc inspect(input => value: i32) => value\n";
        var document = Project(source);
        var external = At(document, source.LastIndexOf("input", StringComparison.Ordinal));
        var internalName = At(document, source.IndexOf("value:", StringComparison.Ordinal));
        var reference = At(document, source.LastIndexOf("value", StringComparison.Ordinal));
        Assert.Equal("value: i32", reference.Info.Variable!.Declaration.Header);
        Assert.Same(external.Info, internalName.Info);
        Assert.Same(external.Info, reference.Info);
        Assert.Contains("The argument.", Render(reference, false));
    }

    [Fact]
    public void CallableVariableCallKeepsContractEffectsAndVariableType()
    {
        const string source = "func apply<F>(callback: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return callback()\n";
        var document = Project(source);
        var entry = At(document, source.LastIndexOf("callback", StringComparison.Ordinal));
        Assert.NotNull(entry.Info.Variable);
        Assert.Contains(entry.Info.Declarations, static d => d.Header == "() -> i32");
        Assert.Contains("Available bound: confined", entry.Info.Effects);
        Assert.Contains("Premise:", entry.Info.Effects);
        Assert.StartsWith("ref/F during ", entry.Info.CopyType);
        Assert.Same(entry.Info, At(document, entry.Span.End).Info);
    }

    [Theory]
    [InlineData("ref", "Creates a shared borrow")]
    [InlineData("uniq", "Creates an exclusive, mutable borrow")]
    [InlineData("obj", "Infers the object target")]
    [InlineData("rc", "Infers the object target")]
    [InlineData("arc", "Infers the object target")]
    [InlineData("objref", "Obtains shared, non-owning access")]
    [InlineData("objuniq", "Obtains exclusive, mutable")]
    [InlineData("move", "Transfers a movable place")]
    [InlineData("copy", "Copies the operand's value")]
    [InlineData("follow", "Selects the referent place")]
    [InlineData("raw", "Obtains a raw pointer")]
    [InlineData("wrap<u8>", "wrapping modulo")]
    [InlineData("bits<f32>", "preserving the bit pattern")]
    public void OperationsHaveSeparateExactTokensEvenWhenOperandTypeFails(string operation, string explanation)
    {
        var source = "func main()\n    _ = missing@" + operation + "\n";
        var document = Project(source, accepted: false);
        var at = source.IndexOf('@');
        var entry = At(document, at);
        Assert.Equal(new SourceSpan(at, 1), entry.Span);
        var name = At(document, at + 1);
        Assert.Equal(new SourceSpan(at + 1, operation.Split('<')[0].Length), name.Span);
        Assert.Same(entry.Info, name.Info);
        Assert.Contains(explanation, Assert.Single(entry.Info.Declarations).Details);
        Assert.Empty(entry.Info.Declarations[0].Origins);
        Assert.Empty(entry.Info.Declarations[0].Owner);
        Assert.Equal(-1, document.Find(at - "missing".Length - 1));
    }

    [Fact]
    public void FullTargetsTypeArgumentsAndChainedOperationsKeepTokenOwnership()
    {
        const string source = "func main()\n    var value = 1\n    _ = value@ref/i32\n    _ = value@move@ref\n    _ = value@wrap<u8>\n";
        var document = Project(source);
        var operation = At(document, source.IndexOf("@ref/", StringComparison.Ordinal));
        Assert.Equal("@ref/T", operation.Info.Declarations[0].Header);
        Assert.Contains("written target", operation.Info.Declarations[0].Details);
        var type = At(document, source.IndexOf("/i32", StringComparison.Ordinal) + 1);
        Assert.Equal("i32", type.Info.CopyType);
        Assert.Equal("@move", At(document, source.IndexOf("@move", StringComparison.Ordinal)).Info.Declarations[0].Header);
        Assert.Equal("@ref", At(document, source.IndexOf("@move@ref", StringComparison.Ordinal) + 5).Info.Declarations[0].Header);
        Assert.Equal("u8", At(document, source.LastIndexOf("u8", StringComparison.Ordinal)).Info.CopyType);
    }

    [Fact]
    public void CommentsStringsExcludedCodeAndSameSpelledNamesDoNotBecomeOperations()
    {
        const string source = "func copy(value: i32) -> i32 => value\nfunc main()\n    // value@ref\n    let text = \"value@move\"\n    _ = copy(1)\n#if false\n    func excluded() => missing@ref\n";
        var document = Project(source);
        Assert.Equal(-1, document.Find(source.IndexOf("@ref", StringComparison.Ordinal)));
        Assert.Equal(-1, document.Find(source.IndexOf("@move", StringComparison.Ordinal)));
        Assert.Equal(-1, document.Find(source.LastIndexOf("@ref", StringComparison.Ordinal)));
        Assert.Equal("func copy(value: i32) -> i32", At(document, source.LastIndexOf("copy(", StringComparison.Ordinal)).Info.Declarations[0].Header);
    }

    [Fact]
    public void PatternAndLoopBindingsHaveBothDeclarationAndReferenceTargets()
    {
        const string source = "func read(pair: (i32, bool)) -> i32 => match pair\n    (let number, _) => number\nfunc iterate(items: [2 of i32])\n    for item in items\n        _ = item\n";
        var document = Project(source);
        foreach (var name in new[] { "number", "item" })
        {
            var declaration = At(document, source.IndexOf(name == "item" ? "item in" : "number,", StringComparison.Ordinal));
            var reference = At(document, source.LastIndexOf(name, StringComparison.Ordinal));
            Assert.NotNull(declaration.Info.Variable);
            Assert.Equal(declaration.Info.Variable, reference.Info.Variable);
        }
    }

    [Theory]
    [InlineData("(i32, bool)", "Core: (i32, bool)")]
    [InlineData("() -> i32", "Core: () -> i32")]
    [InlineData("[2 of i32]", "Core: [2 of i32]")]
    public void StructuralTypesHaveAccurateTargets(string type, string target)
    {
        var source = "contract View\nfunc inspect(value: " + type + ") => ()\n";
        var entry = At(Project(source), source.IndexOf("value:", StringComparison.Ordinal));
        Assert.Equal(target, entry.Info.Variable!.Target);
        Assert.DoesNotContain('$', Render(entry, false));
    }

    [Fact]
    public void DeferredRuntimeContractViewsDoNotInventEstablishedVariables()
    {
        const string source = "contract View\nfunc inspect(value: objref/View) => ()\n";
        var document = Project(source, accepted: false);
        Assert.Equal(-1, document.Find(source.IndexOf("value:", StringComparison.Ordinal)));
    }

    [Fact]
    public void MissingTypesAndDuplicateBindingsPublishNoGuessedVariableFacts()
    {
        const string source = "func inspect()\n    let missing = unknown\n    _ = missing\n    let duplicate = 1\n    let duplicate = false\n    _ = duplicate\n";
        var document = Project(source, accepted: false);
        Assert.Equal(-1, document.Find(source.IndexOf("missing =", StringComparison.Ordinal)));
        Assert.Equal(-1, document.Find(source.LastIndexOf("missing", StringComparison.Ordinal)));
        Assert.Equal(-1, document.Find(source.IndexOf("duplicate =", StringComparison.Ordinal)));
        Assert.Equal(-1, document.Find(source.LastIndexOf("duplicate", StringComparison.Ordinal)));
    }

    [Fact]
    public void SplitDeclarationsAndCommentlessSourcesKeepEveryKnownFileWithoutDuplicates()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        compilation.CollectHover = true;
        compilation.CollectDocumentation = true;
        compilation.Kotonoha.AddSource(new("z/second.kimi", "/// Second part.\nstruct Split\n"));
        compilation.Kotonoha.AddSource(new("a/first.kimi", "struct Split\n"));
        Assert.True(compilation.Bind().IsComplete);
        var snapshot = compilation.Binding.CreateHoverSnapshot();
        var document = snapshot.Documents[SourceIdentity.FromPath("a/first.kimi")];
        var entry = At(document, 7);
        var body = Render(entry, false);
        Assert.Contains("Second part.", body);
        Assert.True(body.IndexOf("(a/first.kimi)", StringComparison.Ordinal) < body.IndexOf("(z/second.kimi)", StringComparison.Ordinal));
        Assert.EndsWith("(z/second.kimi)", body);
        var original = entry.Info.Declarations[0];
        var duplicated = original with { Origins = [original.Origins[0], original.Origins[0], original.Origins[1]] };
        Assert.Equal(body, HoverRenderer.Render(entry.Info with { Declarations = [duplicated] }, false).Body);
    }

    [Fact]
    public void EquivalentTypeSpellingsShareSemanticsWithoutDependingOnCollectionOrder()
    {
        const string source = "func first(value: i32) => ()\nfunc second(value: owner/i32) => ()\n";
        var document = Project(source);
        var plain = At(document, source.IndexOf("i32", StringComparison.Ordinal));
        var applied = At(document, source.IndexOf("owner/", StringComparison.Ordinal));
        Assert.Contains("Semantics: owner — Direct ownership of a value.", applied.Info.Use);
        Assert.Equal(plain.Info.Use, applied.Info.Use);
    }

    [Fact]
    public void DependencyAndUnknownSourceBasesNeverUseTheConsumingProjectAsAFallback()
    {
        var compilation = Compilation.CreateForTest();
        var root = Path.GetFullPath("temp/dependency-hover");
        var known = new Kotonoha(compilation, "Dependency", Path.Combine(root, "Dependency.kimiproj"));
        known.AddSource(new(Path.Combine(root, "src", "Model.kimi"), "struct Model\n"));
        Assert.Equal("src/Model.kimi", known.HoverSourceName(0));
        var unknown = new Kotonoha(compilation, "Unknown", "unmapped-project");
        unknown.AddSource(new(Path.Combine(root, "Model.kimi"), "struct Model\n"));
        Assert.Null(unknown.HoverSourceName(0));
        var embedded = new Kotonoha(compilation, "Embedded", string.Empty);
        embedded.AddSource(new("compiler://Kimi/Model.kimi", "struct Model\n"));
        Assert.Null(embedded.HoverSourceName(0));
        if (OperatingSystem.IsWindows())
        {
            var otherDrive = new Kotonoha(compilation, "OtherDrive", @"Y:\Dependency\Dependency.kimiproj");
            otherDrive.AddSource(new(@"Z:\src\Model.kimi", "struct Model\n"));
            Assert.Null(otherDrive.HoverSourceName(0));
        }
    }

    [Fact]
    public void DifferentVariableSemanticsAndBindingsCannotAgreeByRenderedNameAlone()
    {
        const string source = "func read(value: ref/i32) => ()\n";
        var entry = At(Project(source), source.IndexOf("value", StringComparison.Ordinal));
        var changed = entry.Info with { Variable = entry.Info.Variable! with { Semantics = "different constraint" } };
        Assert.False(new HoverAgreement().Equal(entry, entry with { Info = changed }));
        changed = entry.Info with { TypeIdentity = new("different binding", []) };
        Assert.False(new HoverAgreement().Equal(entry, entry with { Info = changed }));
    }

    [Fact]
    public void SourceFooterSurvivesTruncationAndAgreementIncludesItsLogicalMapping()
    {
        var source = new SourceDocument("physical.kimi", "/// Kept.\n/// \n/// " + new string('*', 40_000));
        var documentation = new HoverDocumentation(source, new(0, source.SourceText.Length), 0, "Project", "src/A.kimi", null, 0, default, []);
        var declaration = new HoverDeclaration("Type", "Project", "struct A", [new("Project", "physical.kimi", default, "src/A.kimi")], [documentation]);
        var info = new HoverInfo([declaration]);
        var rendered = HoverRenderer.Render(info, true);
        Assert.Contains("Documentation truncated:", rendered.Body);
        Assert.EndsWith("(src\\/A\\.kimi)", rendered.Body);
        Assert.True(rendered.Body!.Length <= HoverLimits.Output);
        var changed = info with { Declarations = [declaration with { Origins = [declaration.Origins[0] with { LogicalName = "src/B.kimi" }] }] };
        Assert.False(new HoverAgreement().Equal(new(default, info), new(default, changed)));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void RepeatedVariableAndOperationRequestsReuseCurrentAndHistoricalAnswers()
    {
        const string source = "func main()\n    let value = 1\n    _ = value@copy\n";
        var first = Project(source);
        var second = Project(source);
        var identity = SourceIdentity.FromPath("main.kimi");
        using var text = new TextDocument(source);
        var state = new HoverState([new(new(identity, UnitKind.Product, "one"), 1, first), new(new(identity, UnitKind.Product, "two"), 2, second)], true);
        var positions = new[] { new SourcePosition(1, 8), new SourcePosition(2, 8), new SourcePosition(2, 13), new SourcePosition(2, 14) };
        foreach (var position in positions)
        {
            Assert.NotNull(state.Find(text, position).Body);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            foreach (var position in positions)
            {
                state.Find(text, position);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(state.Edited(0, 0, 1));
        Assert.True(text.TryApply(new(0, 0), new(0, 0), " "));
        Assert.StartsWith(HoverRenderer.PreviousNotice, state.Find(text, positions[0]).Body);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            state.Find(text, positions[0]);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static HoverDocument Project(string source, bool accepted = true)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        compilation.CollectHover = true;
        compilation.CollectDocumentation = true;
        compilation.Kotonoha.AddSource(new("main.kimi", source));
        Assert.Empty(TestDiagnostics.Of(compilation));
        var result = compilation.Bind();
        Assert.Equal(accepted, result.IsComplete);
        return compilation.Binding.CreateHoverSnapshot().Documents[SourceIdentity.FromPath("main.kimi")];
    }

    private static HoverEntry At(HoverDocument document, int position)
    {
        var index = document.Find(position);
        Assert.True(index >= 0, $"No Hover at {position}: {document.Source.SourceText[position..]}");
        return document.Entries[index];
    }

    private static string Render(HoverEntry entry, bool markdown)
        => Assert.IsType<string>(HoverRenderer.Render(entry.Info, markdown).Body);
}
