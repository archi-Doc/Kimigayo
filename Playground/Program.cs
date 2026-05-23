namespace Playground;

using Arc.Unit;
using Kimigayo;
using Kimigayo.Project;
using Kimigayo.Solution;
using SimplePrompt;
using Tinyhand;

public class KimiParser
{
    public KimiParser()
    {
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var builder = new Solution();
        builder.TryReadFile("aaa");
        builder.Build();
        // var tree = CodeTree.Parse("");

        var project = Project.NewTestProject();
        project.Build();

        var file = new ProjectFile();
        file.Targets = ["Windows", "Linux"];
        file.Use = ["Kimi.Base"];
        var package = new ProjectFile.PackageClass() with
        {
            Name = "tinyhand",
            Version = "1.2",
        };
        file.Packages = [package];
        var st = TinyhandSerializer.SerializeToString(file);

        var file2 = TinyhandSerializer.DeserializeFromString<ProjectFile>("""
            Targets=
              Windows
              Linux
            Packages=
              + Name="tinyhand", Version="1.2"
              + Name="valuelink", Version="1.2"
            Use=
              "Kimi.Base"
            """);

    }
}
