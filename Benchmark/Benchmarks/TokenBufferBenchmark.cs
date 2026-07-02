// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using BenchmarkDotNet.Attributes;
using Kimigayo.Language;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class TokenBufferBenchmark
{
    public const int N = 100 * 1024;

    public TokenBufferBenchmark()
    {
    }

    [GlobalSetup]
    public void Setup()
    {
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    /*[Benchmark]
    public long List()
    {
        var list = new List<Token>();
        for (var i = 0; i < N; i++)
        {
            var token = new Token((TokenKind)(i & 7), default, default);
            list.Add(token);
        }

        long sum = 0;
        foreach (var x in list)
        {
            sum += (long)x.Kind;
        }

        return sum;
    }

    [Benchmark]
    public long List2()
    {
        var list = new List<Token>(N);
        for (var i = 0; i < N; i++)
        {
            var token = new Token((TokenKind)(i & 7), default, default);
            list.Add(token);
        }

        long sum = 0;
        foreach (var x in list)
        {
            sum += (long)x.Kind;
        }

        return sum;
    }*/

    [Benchmark]
    public long Builder()
    {
        var list = default(TokenSequenceBuilder);
        for (var i = 0; i < N; i++)
        {
            var token = new Token((TokenKind)(i & 7), default, default);
            list.Add(token);
        }

        long sum = 0;
        foreach (var y in list.ToReadOnlySequence())
        {
            foreach (var x in y.Span)
            {
                sum += (long)x.Kind;
            }
        }

        list.Dispose();

        return sum;
    }

    [Benchmark]
    public long BuilderSeq()
    {
        var list = default(TokenSequenceBuilder);
        for (var i = 0; i < N; i++)
        {
            var token = new Token((TokenKind)(i & 7), default, default);
            list.Add(token);
        }

        var sr = new SequenceReader<Token>(list.ToReadOnlySequence());
        long sum = 0;
        while (sr.TryRead(out var x))
        {
            sum += (long)x.Kind;
        }

        list.Dispose();

        return sum;
    }

    /*[Benchmark]
    public long Builder2()
    {
        var list = default(TokenSequenceBuilder2);
        for (var i = 0; i < N; i++)
        {
            var token = new Token((TokenKind)(i & 7), default, default);
            list.AddToken(token);
        }

        long sum = 0;
        foreach (var y in list.Build())
        {
            foreach (var x in y.Span)
            {
                sum += (long)x.Kind;
            }
        }

        list.Dispose();

        return sum;
    }*/
}
