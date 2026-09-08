// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Running;

namespace Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        var switcher = new BenchmarkSwitcher(new[]
        {
            typeof(ParseBenchmark),
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
