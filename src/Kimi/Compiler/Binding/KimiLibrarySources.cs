// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // Only immutable text and tokens are shared across freezes. Each freeze makes its own SourceDocuments and syntax/binding graph, which
    // every compilation reading that library shares (PLAN G949 Stage 2 U3), their line maps formed by the freeze (PLAN G1072). No resource
    // IO on rebind.
    private static class Sources
    {
        internal static readonly Source[] All =
        [
            Read("Core.kimi"),
            Read("Windows.kimi"),
            Read("Time.kimi"),
            Read("Comparison.kimi"),
            Read("Arithmetic.kimi"),
            Read("Iterator.kimi"),
            Read("LendingIterator.kimi"),
            Read("Iterable.kimi"),
            Read("UniqIterable.kimi"),
            Read("IntoIterable.kimi"),
            Read("Iteration.kimi"),
            Read("Indexable.kimi"),
            Read("Slice.kimi"),
            Read("UniqSlice.kimi"),
            Read("Array.kimi"),
            Read("Dictionary.kimi"),
            Read("DictionaryStorage.kimi"),
            Read("Storage.kimi"),
            Read("Raw.kimi"),
            Read("Intrinsics.kimi"),
            Read("Formatting.kimi"),
            Read("Test.kimi"),
        ];

        private static Source Read(string name)
        {
            using var stream = typeof(KimiLibrary).Assembly.GetManifestResourceStream("Kimi.Library." + name)
                ?? throw new InvalidOperationException("Missing embedded Kimi source: " + name);
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true));
            return new("compiler://Kimi/" + Compilation.CurrentLanguageVersion + "/" + name, reader.ReadToEnd());
        }
    }

    private sealed class Source(string path, string text)
    {
        private Token[]? tokens;
        private DocumentationSource.Candidate[]? documentation;

        internal string Path { get; } = path;

        internal string Text { get; } = text;

        internal ReadOnlySpan<Token> GetTokens(CodeContext context)
        {
            var collect = context.Compilation.CollectDocumentation;
            var optionalOwner = context.Compilation.CollectHover ? context.Compilation : null;
            var cached = Volatile.Read(ref this.tokens);
            var candidates = collect ? Volatile.Read(ref this.documentation) : null;
            if (cached is not null && (!collect || candidates is not null))
            {
                if (collect)
                {
                    context.Documentation = DocumentationSource.FromCandidates(context.SourceDocument!, candidates, optionalOwner);
                }

                return cached;
            }

            var tokenizer = new Tokenizer(context.DiagnosticCollection, context.SourceDocument!) { CollectDocumentation = collect, OptionalHoverOwner = optionalOwner };
            try
            {
                tokenizer.ReadAll();
                var parsed = cached ?? tokenizer.Tokens.ToArray();
                // Tokens contain only immutable kinds and source offsets, never ASTs,
                // source documents or compilation state. Invalid lexing is not cached:
                // each compilation must receive its own source diagnostics.
                var valid = !context.Compilation.Diagnostics.HasSyntaxErrors(context.SourceDocument!);
                if (collect)
                {
                    context.Documentation = tokenizer.Documentation;
                    if (valid)
                    {
                        // Empty is a completed collection too. Publish only ranges; parsing below mutates its own comments.
                        var collected = tokenizer.Documentation?.CaptureCandidates() ?? [];
                        if (optionalOwner?.HasDocumentationFailure(context.SourceDocument!) != true)
                        {
                            Interlocked.CompareExchange(ref this.documentation, collected, null);
                        }
                    }
                }

                return valid ? Interlocked.CompareExchange(ref this.tokens, parsed, null) ?? parsed : parsed;
            }
            finally
            {
                tokenizer.Dispose();
            }
        }
    }

    // Each source is an ordinary Kimigayo source of the library's root (SPEC 22.1); a declaration the compiler implements omits its
    // Body by its implementation Attribute (FunctionKoto.OmitsBody).
    private void LoadSources()
    {
        foreach (var source in Sources.All)
        {
            var document = new SourceDocument(source.Path, source.Text);
            _ = this.Kotonoha.LogicalName(document); // D-C1 (PLAN G949 Stage 2 U2b): the name memo is formed here, never by a later reader.
            var context = new CodeContext(this.Kotonoha, sourceDocument: document);
            var reader = new TokenReader(context, document.AsSpan(), source.GetTokens(context));
            this.Kotonoha.RootKoto.Parse(ref reader);
            if (context.Documentation is { } documentation)
            {
                documentation.SetLocation(this.Kotonoha.LogicalName(document));
                documentation.Finish(context.Compilation.Diagnostics.HasSyntaxErrors(document));
                this.Kotonoha.RecordDocumentation(documentation);
            }
        }
    }
}
