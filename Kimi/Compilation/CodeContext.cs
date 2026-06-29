using System;
using System.Collections.Generic;
using System.Text;

namespace Kimigayo.Language;

public class CodeContext
{
    public Compilation Compilation { get; }

    internal CodeContext(Compilation compilation)
    {
        this.Compilation = compilation;
    }
}
