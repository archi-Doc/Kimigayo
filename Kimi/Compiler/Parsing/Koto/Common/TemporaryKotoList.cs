// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Collects child nodes on the stack before they are stored in a syntax-tree array.
/// </summary>
/// <remarks>
/// Up to four nodes are stored inline without a heap allocation. The element type is fixed,
/// so array stores need no covariance check and the array is sized exactly.
/// </remarks>
internal ref struct TemporaryKotoList
{
    private const int InlineCount = 4;

    private int count;
    private Koto? item0;
    private Koto? item1;
    private Koto? item2;
    private Koto? item3;
    private List<Koto>? overflow;

    /// <summary>Gets the number of nodes in the list.</summary>
    public readonly int Count => this.count;

    /// <summary>Adds a node to the end of the list.</summary>
    /// <param name="item">The node to add.</param>
    public void Add(Koto item)
    {
        switch (this.count)
        {
            case 0:
                this.item0 = item;
                break;
            case 1:
                this.item1 = item;
                break;
            case 2:
                this.item2 = item;
                break;
            case 3:
                this.item3 = item;
                break;
            default:
                (this.overflow ??= new(InlineCount * 2)).Add(item);
                break;
        }

        this.count++;
    }

    /// <summary>Copies the nodes to a new array in insertion order.</summary>
    /// <returns>An exactly sized array, or an empty array when the list is empty.</returns>
    public readonly Koto[] ToArray()
    {
        var count = this.count;
        if (count == 0)
        {
            return [];
        }

        var array = new Koto[count];
        array[0] = this.item0!;
        if (count > 1)
        {
            array[1] = this.item1!;
            if (count > 2)
            {
                array[2] = this.item2!;
                if (count > 3)
                {
                    array[3] = this.item3!;
                    if (count > InlineCount)
                    {
                        this.overflow!.CopyTo(array, InlineCount);
                    }
                }
            }
        }

        return array;
    }
}
