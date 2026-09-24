// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // Only immutable text and tokens are shared. SourceDocument's lazy line map and the mutable
    // syntax/binding graph belong to each compilation. No resource IO on rebind.
    private static class Sources
    {
        internal static readonly Source[] All =
        [
            Read("Core.kimi"),
            Read("Comparison.kimi"),
            Read("Iterator.kimi"),
            Read("Iterable.kimi"),
            Read("Slice.kimi"),
            Read("Array.kimi"),
            Read("Dictionary.kimi"),
            Read("DictionaryStorage.kimi"),
            Read("Formatting.kimi"),
            Read("Text.kimi", KimiLibraryContainer.Text, signatures: true),
            Read("FixedBuffer.kimi", KimiLibraryContainer.FixedBuffer, signatures: true),
            Read("HeapBuffer.kimi", KimiLibraryContainer.HeapBuffer, signatures: true),
            Read("WriteWindow.kimi", KimiLibraryContainer.WriteWindow, signatures: true),
            Read("Utf8Writer.kimi", KimiLibraryContainer.Utf8Writer, signatures: true),
            Read("ArrayOperations.kimi", KimiLibraryContainer.Array, signatures: true),
            Read("DictionaryOperations.kimi", KimiLibraryContainer.Dictionary, signatures: true),
            Read("Intrinsics.kimi", KimiLibraryContainer.Intrinsics, signatures: true),
            Read("Console.kimi", KimiLibraryContainer.Console, signatures: true),
            Read("Test.kimi", KimiLibraryContainer.Test, signatures: true),
        ];

        private static Source Read(string name, KimiLibraryContainer container = KimiLibraryContainer.Root, bool signatures = false)
        {
            using var stream = typeof(KimiLibrary).Assembly.GetManifestResourceStream("Kimi.Library." + name)
                ?? throw new InvalidOperationException("Missing embedded Kimi source: " + name);
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true));
            return new("compiler://Kimi/" + Compilation.CurrentLanguageVersion + "/" + name, reader.ReadToEnd(), container, signatures);
        }
    }

    private sealed class Source(string path, string text, KimiLibraryContainer container, bool signatures)
    {
        private Token[]? tokens;

        internal string Path { get; } = path;

        internal string Text { get; } = text;

        internal KimiLibraryContainer Container { get; } = container;

        internal bool Signatures { get; } = signatures;

        internal ReadOnlySpan<Token> GetTokens(CodeContext context)
        {
            if (Volatile.Read(ref this.tokens) is { } cached)
            {
                return cached;
            }

            var diagnostics = context.DiagnosticCollection;
            var errorVersion = diagnostics.ErrorVersion;
            var tokenizer = new Tokenizer(diagnostics, context.SourceDocument!);
            try
            {
                tokenizer.ReadAll();
                var parsed = tokenizer.Tokens.ToArray();
                // Tokens contain only immutable kinds and source offsets, never ASTs,
                // source documents or compilation state. Invalid lexing is not cached:
                // each compilation must receive its own source diagnostics.
                return diagnostics.ErrorVersion != errorVersion ? parsed :
                    Interlocked.CompareExchange(ref this.tokens, parsed, null) ?? parsed;
            }
            finally
            {
                tokenizer.Dispose();
            }
        }
    }

    private DeclarationContainerKoto? FormattingContainer(KimiLibraryContainer kind)
        => kind == KimiLibraryContainer.Text ? this.Text :
            kind is KimiLibraryContainer.FixedBuffer or KimiLibraryContainer.HeapBuffer
                ? FindDeclaration(this.Text, kind.ToString(), false) as DeclarationContainerKoto :
            kind is KimiLibraryContainer.WriteWindow or KimiLibraryContainer.Utf8Writer
                ? FindDeclaration(this.Kotonoha.RootKoto, kind.ToString(), false) as DeclarationContainerKoto : null;

    private void LoadSources()
    {
        foreach (var source in Sources.All)
        {
            var container = source.Container switch
            {
                KimiLibraryContainer.Console => this.Console,
                KimiLibraryContainer.Intrinsics => this.Intrinsics,
                KimiLibraryContainer.Test => this.Test,
                KimiLibraryContainer.Array => FindDeclaration(this.Kotonoha.RootKoto, "Array", false) as DeclarationContainerKoto, // Array.kimi is read first.
                KimiLibraryContainer.Dictionary => FindDeclaration(this.Kotonoha.RootKoto, "Dictionary", false) as DeclarationContainerKoto,
                KimiLibraryContainer.Root => this.Kotonoha.RootKoto,
                _ => this.FormattingContainer(source.Container),
            };
            if (container is null)
            {
                continue; // The missing Array struct is reported by validation (SourceExpected).
            }

            this.ParseSource(source, container);
        }
    }

    private void ParseSource(Source source, DeclarationContainerKoto container)
    {
        var document = new SourceDocument(source.Path, source.Text);
        var context = new CodeContext(this.Kotonoha, sourceDocument: document);
        context.DiagnosticCollection.SetSourceDocument(document);
        var reader = new TokenReader(context, document.AsSpan(), source.GetTokens(context));
        if (!source.Signatures)
        {
            container.Parse(ref reader);
            return;
        }

        // This mode is private to embedded, compiler-implemented signatures.
        // Each loaded member must subsequently match a registered intrinsic.
        while (reader.CanRead)
        {
            Parser.ConsumeAttributeAndModifier(ref reader, out var end);
            if (end)
            {
                break;
            }

            if (reader.CurrentTokenKind != TokenKind.Func)
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, reader.CurrentTokenKind.ToString());
                break;
            }

            reader.Advance();
            var declaration = Parser.ParseFuncDeclaration(ref reader);
            if (declaration is null)
            {
                break;
            }

            Parser.ParseSignatureClauses(ref reader, declaration);
            if (container is StructKoto)
            {
                Parser.ValidateReceiverParameters(declaration);
            }

            container.AddLast(declaration);
        }
    }
}
