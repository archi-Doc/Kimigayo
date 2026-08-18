// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Arc.Crypto;
using BenchmarkDotNet.Attributes;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class HashedStringBenchmark
{
    private string name = "TopLevelKeywordAfterCode";
    private string baseName = "Kimi.TopLevelKeywordAfterCode";

    public HashedStringBenchmark()
    {
    }

    [Benchmark]
    public ulong XxHash3()
    {
        var hash = XxHash3Slim.Hash64($"Kimi.{this.name}.Message");
        hash ^= XxHash3Slim.Hash64($"Kimi.{this.name}.Severity");
        hash ^= XxHash3Slim.Hash64($"Kimi.{this.name}.Label");
        hash ^= XxHash3Slim.Hash64($"Kimi.{this.name}.Fix");
        hash ^= XxHash3Slim.Hash64($"Kimi.{this.name}.Note");
        return hash;
    }

    [Benchmark]
    public ulong StringInterpolation()
    {
        var hash = FarmHash.Hash64($"Kimi.{this.name}.Message");
        hash ^= FarmHash.Hash64($"Kimi.{this.name}.Severity");
        hash ^= FarmHash.Hash64($"Kimi.{this.name}.Label");
        hash ^= FarmHash.Hash64($"Kimi.{this.name}.Fix");
        hash ^= FarmHash.Hash64($"Kimi.{this.name}.Note");
        return hash;
    }

    [Benchmark]
    public ulong Append()
    {
        var farm = default(FarmHash);
        farm.Append(this.baseName);
        farm.Append(".Message");
        var hash = farm.Finalize();

        farm.Initialize();
        farm.Append(this.baseName);
        farm.Append(".Severity");
        hash ^= farm.Finalize();

        farm.Initialize();
        farm.Append(this.baseName);
        farm.Append(".Label");
        hash ^= farm.Finalize();

        farm.Initialize();
        farm.Append(this.baseName);
        farm.Append(".Fix");
        hash ^= farm.Finalize();

        farm.Initialize();
        farm.Append(this.baseName);
        farm.Append(".Note");
        hash ^= farm.Finalize();

        return hash;
    }
}
