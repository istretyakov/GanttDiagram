using System.Runtime.CompilerServices;
using EmptyFiles;

namespace GanttDiagram.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        FileExtensions.AddTextExtension("puml");
        FileExtensions.AddTextExtension("mmd");
    }
}
