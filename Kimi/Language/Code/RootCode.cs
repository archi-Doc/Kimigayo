// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

public sealed class RootCode : GroupCode
{
    public Code Current { get; private set; }

    private List<Code> list = new();

    public RootCode(Project project)
    {
        this.Current = this;
    }

    public void Read(List<Token> list, int count)
    {
        foreach (var x in list)
        {
            var code = CodeHelper.FromToken(x);
        }
    }
}
