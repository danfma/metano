// Preload for downlevel-check.mjs: makes the engine look like one without explicit resource
// management (stable Safari). `Symbol.dispose` is a non-configurable property of the real
// `Symbol`, so the global binding is replaced by a stand-in function that calls the real one
// and carries every static of it but `dispose` / `asyncDispose`, until something assigns
// them (the metano-runtime polyfill). `SuppressedError` and the disposable stacks are
// removed, so TypeScript's downlevel helpers use their stand-ins.
const realSymbol = globalThis.Symbol;
const hidden = new Set(["dispose", "asyncDispose", "length", "name", "prototype"]);

function SymbolWithoutDispose(description) {
  return realSymbol(description);
}

for (const key of Reflect.ownKeys(realSymbol)) {
  if (!hidden.has(key)) {
    Object.defineProperty(SymbolWithoutDispose, key, {
      value: realSymbol[key],
      writable: true,
      configurable: true,
    });
  }
}

SymbolWithoutDispose.prototype = realSymbol.prototype;
globalThis.Symbol = SymbolWithoutDispose;

delete globalThis.SuppressedError;
delete globalThis.DisposableStack;
delete globalThis.AsyncDisposableStack;
