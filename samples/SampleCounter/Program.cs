using Metano.Annotations;

namespace SampleCounter;

// Dart skips emission: the Flutter consumer wires up its own entry point in
// `lib/main.dart`, and we don't have a BCL mapping for `Console.WriteLine` on
// Dart yet — emitting `Console.writeLine(...)` literally would fail
// `dart analyze`.
[Erasable]
[NoEmit(TargetLanguage.Dart)]
public static class Program
{
    [ModuleEntryPoint]
    public static void Main()
    {
        Console.WriteLine("Hello, World!");
    }
}
