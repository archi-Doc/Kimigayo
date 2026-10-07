// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--constructor-inference")
        {
            ConstructorInferenceMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--hover")
        {
            HoverMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--local-regions")
        {
            CompilerPlanMeasurements.Run(regions: true);
            return;
        }

        if (args.Length > 0 && args[0] == "--view-plans")
        {
            CompilerPlanMeasurements.Run(views: true);
            return;
        }

        if (args.Length > 0 && args[0] is "--object-plans" or "--callable-plans")
        {
            CompilerPlanMeasurements.Run(args[0] == "--callable-plans");
            return;
        }

        if (args.Length > 0 && args[0] == "--pair-cases")
        {
            PairCaseMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--borrow-storage")
        {
            BorrowStorageMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--compiler-pipeline-check")
        {
            CompilerPipelineMeasurements.Check();
            return;
        }

        if (args.Length > 0 && args[0] == "--compiler-pipeline")
        {
            CompilerPipelineMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--verification")
        {
            VerificationMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--lsp")
        {
            LspMeasurements.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--diagnostics")
        {
            DiagnosticMeasurements.Run();
            return;
        }

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
            typeof(CompilerPipelineBenchmark),
            typeof(DocumentationMarkdownBenchmark),
            typeof(ParseBenchmark),
            typeof(BindingBenchmark),
            typeof(KimiLibraryBenchmark),
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

        this.AddJob(Job.MediumRun.WithToolchain(new BenchmarkToolchain()));
    }
}
