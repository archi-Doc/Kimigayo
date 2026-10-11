// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Text;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace XunitTest;

internal static class CodeRulesTestHelper
{
    internal const string SourcePath = "src/Kimi/Compiler/Binding/Test.cs";
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(x => MetadataReference.CreateFromFile(x)).ToArray();

    internal static CSharpCompilation Compilation(params string[] sources)
        => Compilation(sources.Select((source, index) => (source, index == 0 ? SourcePath : SourcePath.Replace("Test", "Partial" + index, StringComparison.Ordinal))).ToArray());

    internal static CSharpCompilation Compilation(params (string Source, string Path)[] sources)
        => CSharpCompilation.Create(
            "CodeRulesFixture",
            sources.Select(x => CSharpSyntaxTree.ParseText(x.Source, path: Path.Combine(Repository.Root, x.Path))),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    internal static AnalyzerOptions Options(string mode = "Enforce", string baseline = "", Action<Dictionary<string, string>>? configure = null)
    {
        var config = File.ReadAllText(Path.Combine(Repository.Root, "src/Kimi/CodeRules.globalconfig"));
        var path = Path.Combine(Repository.Root, "src/Kimi/CodeRules.globalconfig");
        var parsed = AnalyzerConfigSet.Create(new[] { AnalyzerConfig.Parse(config, path) }).GlobalConfigOptions.AnalyzerOptions;
        var values = new Dictionary<string, string>(parsed, StringComparer.Ordinal)
        {
            ["build_property.KimiRepositoryRoot"] = Repository.Root,
            ["build_property.KimiCodeRulesMode"] = mode,
        };
        configure?.Invoke(values);
        config = "is_global = true\n" + string.Join('\n', values.Where(x => x.Key.StartsWith(CodeRulesConfiguration.Prefix, StringComparison.Ordinal)).Select(x => x.Key + " = " + x.Value));
        parsed = AnalyzerConfigSet.Create(new[] { AnalyzerConfig.Parse(config, path) }).GlobalConfigOptions.AnalyzerOptions;
        var effective = new Dictionary<string, string>(parsed, StringComparer.Ordinal)
        {
            ["build_property.KimiRepositoryRoot"] = values["build_property.KimiRepositoryRoot"],
            ["build_property.KimiCodeRulesMode"] = values["build_property.KimiCodeRulesMode"],
        };
        return new AnalyzerOptions(
            [new TextFile("CodeRules.globalconfig", config), new TextFile("CodeRules.baseline.tsv", baseline)],
            new OptionsProvider(effective));
    }

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(string source, AnalyzerOptions? options = null)
        => await Analyze(Compilation(source), options);

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(CSharpCompilation compilation, AnalyzerOptions? options = null)
    {
        var analyzerOptions = new CompilationWithAnalyzersOptions(
            options ?? Options(),
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: true);
        return await compilation.WithAnalyzers([new CodeRulesAnalyzer()], analyzerOptions).GetAnalyzerDiagnosticsAsync();
    }

    private sealed class TextFile(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(text, Encoding.UTF8, SourceHashAlgorithm.Sha256);
    }

    private sealed class OptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new GlobalOptions(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => this.GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => this.GlobalOptions;
    }

    private sealed class GlobalOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override IEnumerable<string> Keys => values.Keys;

        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
