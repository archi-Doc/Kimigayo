// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;

namespace Kimi.Compiler;

// LayerList (PLAN G949; DECISIONS BU-220): a per-pass list whose library entries are read later, as one list.
// - Responsibility: reading a list's program part and its library part as one list, the program's part first (R1.1's composition
//   order, owned by Binding's pass driver); Count, the indexer and an allocation-free foreach.
// - Inputs: the two parts, each its store's own list (ImplTable: the overlay's and LibraryLayer's base's).
// - Outputs: the composed reads; Program, the program part alone.
// - Invariants: an index below the program part's Count reads the program part, any other the library part at the index less it;
//   foreach reads the program part, then the library part, and fails as a List's does when a part changes during it.
// - Precondition (the reader's): no entry joins either part while an index loop over the composed list runs, since a program entry
//   added then shifts every library index (the loop reads one library entry twice and never the new one); ImplTable's registrations
//   end with RegisterInheritedConformances, before every index loop over Paths (Binding's pass order).
// - Prohibitions: no copy of either part and no write: each part belongs to its store.
internal readonly struct LayerList<T> : IReadOnlyList<T>
{
    private readonly List<T> program;
    private readonly List<T> library;

    internal LayerList(List<T> program, List<T> library)
    {
        this.program = program;
        this.library = library;
    }

    /// <summary>Gets the entries of both parts.</summary>
    public int Count => this.program.Count + this.library.Count;

    /// <summary>Gets the program part.</summary>
    internal List<T> Program => this.program;

    /// <summary>Gets an entry: the program part's, then the library part's.</summary>
    /// <param name="index">The index in the composed list.</param>
    /// <returns>The entry.</returns>
    public T this[int index] => index < this.program.Count ? this.program[index] : this.library[index - this.program.Count];

    /// <summary>Enumerates the program part, then the library part, without allocating.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this.program, this.library);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => this.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>The enumerator over the program part, then the library part.</summary>
    internal struct Enumerator : IEnumerator<T>
    {
        private readonly List<T> library;
        private List<T>.Enumerator part;
        private bool inLibrary;

        internal Enumerator(List<T> program, List<T> library)
        {
            this.library = library;
            this.part = program.GetEnumerator();
        }

        /// <inheritdoc/>
        public T Current => this.part.Current;

        object? IEnumerator.Current => this.Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (this.part.MoveNext())
            {
                return true;
            }

            if (this.inLibrary)
            {
                return false;
            }

            this.inLibrary = true;
            this.part = this.library.GetEnumerator();
            return this.part.MoveNext();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
