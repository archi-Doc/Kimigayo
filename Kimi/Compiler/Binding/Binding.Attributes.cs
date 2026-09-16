// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static Koto? AttributeTarget(AttributeKoto attribute)
    {
        var target = attribute.Parent;
        while (target is AttributeKoto)
        {
            target = target.Parent;
        }

        return target;
    }

    private void IndexLayoutAttribute(AttributeKoto attribute)
    {
        attribute.BindingFailure = BindingFailure.None;
        attribute.BindingState = BindingState.Unvisited;
        attribute.BoundType = null;
        this.nodes.Add(attribute);
        var target = AttributeTarget(attribute);

        if (target is not StructKoto || attribute.LayoutMode is null)
        {
            Fail(attribute, BindingFailure.InvalidLayoutAttribute);
            if (target is not null)
            {
                Fail(target, BindingFailure.InvalidTypeFormation);
            }
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
            var valid = true;
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

                valid &= attribute.BindingState != BindingState.Invalid;

                previousFragment = attribute.FragmentOrdinal;
                if (attribute.LayoutMode is { } explicitMode)
                {
                    if (mode is not null && mode != explicitMode)
                    {
                        Fail(latestSpecification!, BindingFailure.ConflictingLayout);
                        valid = false;
                    }
                    else if (mode is null)
                    {
                        mode = explicitMode;
                        latestSpecification = attribute;
                    }
                }
            }

            var storageFragment = -1;
            for (var m = 0; mode == "C" && m < container.Members.Count; m++)
            {
                if (container.Members[m] is PropertyKoto field && IsStoredVariable(field))
                {
                    if (storageFragment >= 0 && storageFragment != field.FragmentOrdinal)
                    {
                        Fail(field, BindingFailure.SplitCLayoutStorage);
                        valid = false;
                    }

                    storageFragment = field.FragmentOrdinal;
                }
            }

            if (!valid)
            {
                Fail(container, BindingFailure.InvalidTypeFormation);
            }
        }
    }
}
