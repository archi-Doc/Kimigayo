// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimigayo.Language;

/*[TinyhandObject]
public partial class AttributeKoto : Koto
{// #Attribute(KotoList)
    [Key(1)]
    public Koto Attribute { get; private set; }

    [Key(2)]
    public List<Koto> Arguments { get; private set; }

    public AttributeKoto(ref TokenReader reader, Koto attribute, List<Koto> arguments)
        : base(ref reader, default)
    {
        this.Attribute = attribute;
        this.Arguments = arguments;

        attribute.Parent = this;
        foreach (var x in arguments)
        {
            x.Parent = this;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append(Constants.SharpChar);
        sb.Append(this.Attribute.ToString());
        sb.Append(Constants.OpenParenthesisChar);
        for (var i = 0; i < this.Arguments.Count; i++)
        {
            sb.Append(this.Arguments[i].ToString());
            if (i < (this.Arguments.Count - 1))
            {
                sb.Append(Constants.CommaChar);
                sb.Append(Constants.SpaceChar);
            }
        }

        return sb.ToString();
    }
}*/
