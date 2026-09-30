// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Running;

namespace Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--compiler-pipeline-check")
        {
            foreach (var functions in new[] { 1, 32, 128 })
            {
                foreach (var branches in new[] { false, true })
                {
                    var benchmark = new CompilerPipelineBenchmark { Functions = functions, Branches = branches };
                    benchmark.Setup();
                    for (var iteration = 0; iteration < 2; iteration++)
                    {
                        benchmark.FreshParse();
                        benchmark.FreshCompileToIr();
                        benchmark.RebindAndStartup();
                        benchmark.ReanalyzeOwnership();
                        benchmark.ReemitIr();
                    }

                    Console.WriteLine($"PASS compiler pipeline: functions={functions}, branches={branches}");
                }
            }

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
