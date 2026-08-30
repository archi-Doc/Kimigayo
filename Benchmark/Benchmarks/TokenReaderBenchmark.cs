// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class TokenReaderBenchmark
{
    private const string SourceText = """
        public struct Point
            let x: int = 10
            let y: int = 20
        """;

    private readonly CodeContext codeContext;
    private readonly SourceDocument sourceDocument;

    public TokenReaderBenchmark()
    {
        var compilation = Compilation.CreateForTest();
        this.codeContext = compilation.Kotonoha.CreateCodeContext();
        this.sourceDocument = new SourceDocument("TokenReaderBenchmark.kimi", SourceText);
    }

    [Benchmark]
    public int ReadAllTokens()
    {
        var tokenizer = new Tokenizer(this.codeContext.DiagnosticCollection, this.sourceDocument);

        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(this.codeContext, ref tokenizer);
            var checksum = 0;

            while (reader.TryRead(out var token, addDiagnostic: false))
            {
                checksum = unchecked((checksum * 31) + (int)token.Kind);
            }

            return checksum;
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    [Benchmark]
    public int ReadAllTokens_Obs()
    {
        var tokenizer = new Tokenizer(this.codeContext.DiagnosticCollection, this.sourceDocument);

        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReaderObsolete(this.codeContext, ref tokenizer);
            var checksum = 0;

            while (reader.TryRead(out var token, addDiagnostic: false))
            {
                checksum = unchecked((checksum * 31) + (int)token.Kind);
            }

            return checksum;
        }
        finally
        {
            tokenizer.Dispose();
        }
    }
}
