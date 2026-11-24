// Node ESM check of spec 011 FR-029: import the built base module first, unbundled,
// then its guard module, and call the strict guard on plain objects. The generated
// relative `.js` specifiers load under plain Node ESM (spec 004 FR-012).

const dist = new URL("../../dist/sample-runtime-types/unions/", import.meta.url);

await import(new URL("token.js", dist).href);

const { isToken } = await import(new URL("token.guard.js", dist).href);

console.log(isToken({ kind: "TextToken", text: "a" }), isToken({ kind: "TextToken", text: 1 }));
