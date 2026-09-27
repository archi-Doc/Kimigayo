// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static BoundType? AssociatedFormationType(Koto declaration)
        => declaration is IsKoto clause ? AssociatedIdentityType(clause.BoundConstraint) : null;

    private static BoundType? AssociatedIdentityType(BoundConstraint? constraint)
        => constraint?.Kind switch
        {
            ConstraintKind.TypeIdentity => constraint.RequiredType,
            ConstraintKind.And => AssociatedIdentityType(constraint.Left) ?? AssociatedIdentityType(constraint.Right),
            _ => null,
        };

    private bool CheckAssociatedFormation(BoundType type, Koto use)
    {
        if (IsBorrow(type.Semantics) && type.Origin is { } outer)
        {
            if ((outer.Kind == OriginKind.Static && IsExclusive(type.Semantics)) ||
                (type.Components.Count != 0 && !this.AssociatedOriginsOutlive(type.Components[0], outer, use)))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!this.CheckAssociatedFormation(type.Components[i], use))
            {
                return false;
            }
        }

        return true;
    }

    private bool AssociatedOriginsOutlive(BoundType type, BoundOrigin outer, Koto use)
    {
        if (type.Origin is { } origin && !this.ProvesOriginOutlives(origin, outer, use))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!this.ProvesOriginOutlives(type.OriginArguments[i], outer, use))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!this.AssociatedOriginsOutlive(type.Components[i], outer, use))
            {
                return false;
            }
        }

        return true;
    }
}
