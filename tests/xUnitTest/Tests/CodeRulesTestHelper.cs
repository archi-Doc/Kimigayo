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
        => CSharpCompilation.Create(
            "CodeRulesFixture",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(
                source, path: Path.Combine(Repository.Root, index == 0 ? SourcePath : SourcePath.Replace("Test", "Partial" + index, StringComparison.Ordinal)))),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    internal static AnalyzerOptions Options(string mode = "Enforce", string baseline = "", Action<Dictionary<string, string>>? configure = null)
    {
        var config = File.ReadAllText(Path.Combine(Repository.Root, "src/Kimi/CodeRules.globalconfig"));
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.KimiRepositoryRoot"] = Repository.Root,
            ["build_property.KimiCodeRulesMode"] = mode,
        };
        foreach (var line in config.Split('\n'))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].StartsWith(CodeRulesConfiguration.Prefix, StringComparison.Ordinal))
            {
                values.Add(parts[0], parts[1]);
            }
        }

        configure?.Invoke(values);
        return new AnalyzerOptions(
            [new TextFile("CodeRules.globalconfig", config), new TextFile("CodeRules.baseline.tsv", baseline)],
            new OptionsProvider(values));
    }

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(string source, AnalyzerOptions? options = null)
    {
        var analyzerOptions = new CompilationWithAnalyzersOptions(
            options ?? Options(),
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: true);
        return await Compilation(source).WithAnalyzers([new CodeRulesAnalyzer()], analyzerOptions).GetAnalyzerDiagnosticsAsync();
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
