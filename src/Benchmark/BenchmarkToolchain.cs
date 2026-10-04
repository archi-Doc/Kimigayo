// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Characteristics;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Validators;

namespace Benchmark;

/// <summary>Uses this project's source path without recursively searching the solution's artifacts.</summary>
internal sealed class BenchmarkToolchain : Toolchain
{
    private static readonly IToolchain DefaultToolchain = CsProjCoreToolchain.NetCoreApp10_0;

    public BenchmarkToolchain()
        : base("Benchmark.csproj", new ProjectGenerator(), DefaultToolchain.Builder, DefaultToolchain.Executor)
    {
    }

    public override IEnumerable<ValidationError> Validate(BenchmarkCase benchmarkCase, IResolver resolver)
        => DefaultToolchain.Validate(benchmarkCase, resolver);

    private sealed class ProjectGenerator : CsProjGenerator
    {
        public ProjectGenerator()
            : base("net10.0", null!, null!, null!)
        {
        }

        protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger) => GetProjectFile();

        private static FileInfo GetProjectFile([CallerFilePath] string sourcePath = "")
            => new(Path.Combine(Path.GetDirectoryName(sourcePath)!, "Benchmark.csproj"));
    }
}
