// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler;

/// <summary>
/// Provides a stack-only builder for creating a contiguous <see cref="ReadOnlySequence{T}"/> of <see cref="Token"/> values.
/// </summary>
/// <remarks>
/// This type wraps <see cref="SequenceBuilder{T}"/> for token-specific usage and must be disposed
/// when no longer needed to return pooled buffers.
/// </remarks>
public ref struct TokenSequenceBuilder
{
    /// <summary>
    /// The default number of token slots allocated when no initial capacity is provided.
    /// </summary>
    public const int DefaultInitialCapacity = 1024 * 4;

    private SequenceBuilder<Token> builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenSequenceBuilder"/> struct.
    /// </summary>
    /// <param name="initialCapacity">
    /// The initial token capacity to allocate. Defaults to <see cref="DefaultInitialCapacity"/>.
    /// </param>
    /// <param name="clearArrayOnReturn">
    /// Whether rented arrays should be cleared when returned to the pool.
    /// If <see langword="null"/>, the underlying builder default behavior is used.
    /// </param>
    public TokenSequenceBuilder(int initialCapacity, bool? clearArrayOnReturn = null)
    {
        this.builder = new(initialCapacity, clearArrayOnReturn);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenSequenceBuilder"/> struct
    /// with the default initial token capacity.
    /// </summary>
    public TokenSequenceBuilder()
        : this(DefaultInitialCapacity)
    {
    }

    /// <summary>
    /// Gets the current number of tokens written to the builder.
    /// </summary>
    public long Length => this.builder.Length;

    /// <summary>
    /// Appends a token to the end of the sequence being built.
    /// </summary>
    /// <param name="token">The token to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Token token)
        => this.builder.Add(token);

    /// <summary>
    /// Materializes the current contents as a read-only token sequence.
    /// </summary>
    /// <returns>
    /// A <see cref="ReadOnlySequence{T}"/> containing all tokens added so far.
    /// </returns>
    public ReadOnlySequence<Token> ToReadOnlySequence()
        => this.builder.ToReadOnlySequence();

    /// <summary>
    /// Releases resources used by the underlying builder, including pooled buffers.
    /// </summary>
    public void Dispose()
        => this.builder.Dispose();
}
