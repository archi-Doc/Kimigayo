// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public abstract class Koto
{
    public Koto()
    {
    }

    public virtual void Read(ref TokenReader reader)
    {
    }
}
