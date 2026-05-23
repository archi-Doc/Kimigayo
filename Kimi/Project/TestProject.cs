// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Kimigayo;

public partial class Project
{
    public static Project NewTestProject()
    {
        var project = new Project();

        var file = new ProjectFile();
        file.Targets = ["x86_64-pc-windows-msvc"];
        file.Use = ["Kimi.Base",];

        project.ProjectFile = file;
        return project;
    }
}
