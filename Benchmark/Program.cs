// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Running;

namespace Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--named-arguments")
        {
            NamedArgumentMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--documentation-markdown")
        {
            DocumentationMarkdownMeasurements.Run(args[1..]);
            return;
        }

        var switcher = new BenchmarkSwitcher(new[]
        {
            typeof(DocumentationMarkdownBenchmark),
            typeof(ParseBenchmark),
            typeof(BindingBenchmark),
            typeof(PatternBindingBenchmark),
            typeof(StartupBindingBenchmark),
            typeof(OwnershipAnalysisBenchmark),
            typeof(MatchOwnershipBenchmark),
            typeof(FrontEndBenchmark),
            typeof(DirectiveBenchmark),
            typeof(TokenReaderBenchmark),
            typeof(SourceDocumentBenchmark),
            typeof(HashedStringBenchmark),
            typeof(TargetTripleBenchmark),
            typeof(HexToIntBenchmark),
            typeof(NumberLiteralBenchmark),
        });
        switcher.Run(args);
    }
}

public class BenchmarkConfig : BenchmarkDotNet.Configs.ManualConfig
{
    public BenchmarkConfig()
    {
        this.AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub);
        this.AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);

        this.AddJob(BenchmarkDotNet.Jobs.Job.MediumRun);
    }
}
