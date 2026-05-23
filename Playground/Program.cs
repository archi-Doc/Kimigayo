namespace Playground;

using Arc.Unit;
using Kimigayo;
using SimplePrompt;
using Tinyhand;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var builder = new Solution();
        builder.TryReadFile("aaa");
        builder.Build();
        // var tree = CodeTree.Parse("");

        var project = Project.NewTestProject();
        project.AddSource("""
            #Namespace(Test.Program) // Comment
            method void Main()
              return;
            """);
        var result = await project.Build();

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
