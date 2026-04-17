# sample_counter (Flutter)

Flutter consumer of the SampleCounter C# project. The `Counter`, `ICounterView`
and `CounterPresenter` classes in `lib/sample_counter/` are regenerated from
the shared C# source by the Metano Dart target.

## Regenerate the Dart sources

From the repo root:

```sh
dotnet run --project src/Metano.Compiler.Dart/ -- \
  -p samples/SampleCounter/SampleCounter.csproj \
  -o targets/flutter/sample_counter/lib/sample_counter \
  --clean
```

## Run the app

```sh
cd targets/flutter/sample_counter
flutter pub get
flutter run
```

## Status

This is a **prototype** for the Dart/Flutter target — the first second-target
exercise of the Metano IR architecture. Declarations flow through the IR, so
the generated files already have the right shape (fields, methods, interfaces,
constructors, null-safety). Bodies still route through the legacy path, which
hasn't been ported to Dart yet, so the generated methods throw
`UnimplementedError()` by default.

While Phase 5 (body extraction) is in progress, `lib/main.dart` supplies manual
implementations for `Counter` members via an extension. Once bodies flow
end-to-end, those workarounds go away and the Flutter app will consume fully
functional generated code just like the TypeScript samples do today.
