using ConsoleAppFramework;
using MetaSharp;
using Microsoft.Build.Locator;

// Register MSBuild before any Roslyn workspace types are loaded
MSBuildLocator.RegisterDefaults();

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args);
