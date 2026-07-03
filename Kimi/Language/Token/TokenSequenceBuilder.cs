// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimigayo.Language;

public ref struct TokenSequenceBuilder
{
    public const int DefaultInitialCapacity = 256;

    private SequenceBuilder<Token> builder;

    public TokenSequenceBuilder(int initialCapacity = DefaultInitialCapacity, bool? clearArrayOnReturn = null)
    {
        this.builder = new(initialCapacity, clearArrayOnReturn);
    }

    public long Length => this.builder.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Token token)
        => this.builder.Add(token);

    public ReadOnlySequence<Token> ToReadOnlySequence()
        => this.builder.ToReadOnlySequence();

    public void Dispose()
        => this.builder.Dispose();
}
