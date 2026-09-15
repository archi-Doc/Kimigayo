// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private void IndexLayoutAttribute(AttributeKoto attribute)
    {
        attribute.BindingFailure = BindingFailure.None;
        attribute.BindingState = BindingState.Unvisited;
        attribute.BoundType = null;
        this.nodes.Add(attribute);
        var target = attribute.Parent;
        while (target is AttributeKoto)
        {
            target = target.Parent;
        }

        if (target is not StructKoto || attribute.LayoutMode is null)
        {
            Fail(attribute, BindingFailure.InvalidLayoutAttribute);
        }
        else
        {
            Complete(attribute, BoundType.Unit);
        }
    }

    private void ValidateLayoutFragments()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not StructKoto container)
            {
                continue;
            }

            string? mode = null;
            AttributeKoto? latestSpecification = null;
            var previousFragment = -1;
            for (var attribute = container.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (attribute.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "Layout" })
                {
                    continue;
                }

                if (previousFragment == attribute.FragmentOrdinal)
                {
                    Fail(attribute, BindingFailure.InvalidLayoutAttribute);
                }

                previousFragment = attribute.FragmentOrdinal;
                if (attribute.LayoutMode is { } explicitMode)
                {
                    if (mode is not null && mode != explicitMode)
                    {
                        Fail(latestSpecification!, BindingFailure.ConflictingLayout);
                    }
                    else if (mode is null)
                    {
                        mode = explicitMode;
                        latestSpecification = attribute;
                    }
                }
            }

            if (mode != "C")
            {
                continue;
            }

            var storageFragment = -1;
            for (var m = 0; m < container.Members.Count; m++)
            {
                if (container.Members[m] is PropertyKoto field && IsStoredVariable(field))
                {
                    if (storageFragment >= 0 && storageFragment != field.FragmentOrdinal)
                    {
                        Fail(field, BindingFailure.SplitCLayoutStorage);
                    }

                    storageFragment = field.FragmentOrdinal;
                }
            }
        }
    }
}
