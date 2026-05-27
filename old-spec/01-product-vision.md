# Product Vision

## 1. Identification

**Product name:** Metano

**Category:** source-to-source transpiler and TypeScript artifact generator

**Short definition:** Metano is a Roslyn-powered transpiler that converts
annotated C# code into executable, typed, idiomatic TypeScript, preserving
shared domain types, behavior, and useful semantics with minimal runtime
support.

## 2. Vision

Enable .NET teams to use **C# as the single source of truth** for shared domain
types, DTOs, rules, and selected business logic, while obtaining a TypeScript
package that integrates naturally into the JavaScript ecosystem.

## 3. Value Proposition

Metano exists to remove structural and behavioral duplication between .NET
backends and TypeScript frontends, reducing:

- divergence between equivalent models in different languages;
- manual rework required to keep DTOs, enums, and small rules in sync;
- bugs caused by semantic drift between backend and frontend;
- integration friction across ecosystems.

In exchange, Metano provides:

- generation of **real TypeScript**, not only type declarations;
- output compatible with standard JS/TS bundlers, tests, and tooling;
- preservation of useful C# concepts for shared domain code;
- low runtime coupling, with zero-cost strategies where feasible.

## 4. Core Product Thesis

Metano's central value is not “running .NET in the browser.” Its central value
is **carrying domain knowledge written in C# into TypeScript code that is
usable, readable, and operational**, without forcing the frontend into a
parallel ecosystem.

## 5. Strategic Differentiation

Metano is differentiated from adjacent approaches by the following product
commitments:

- it does not prioritize unrestricted coverage of all C# language features;
- it does not ship a heavy runtime to simulate .NET end-to-end;
- it does not limit itself to static contract generation;
- it does not attempt to replace the native TypeScript ecosystem.

Its positioning is that of a pragmatic transpiler for **shareable domain code**,
with idiomatic output and integration-first design.

## 6. Expected User Outcome

After adopting Metano, a user should be able to:

1. model shared domain code in C#;
2. explicitly mark what should be transpiled;
3. run build/transpile workflows;
4. consume the generated TypeScript package as part of a normal frontend stack;
5. trust that types, names, imports, and essential behavior remain consistent
   with the .NET source.
