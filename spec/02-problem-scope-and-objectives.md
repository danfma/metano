# Problem, Scope, and Objectives

## 1. Primary Problem

In architectures with a .NET backend and a TypeScript frontend, the same domain
knowledge is often maintained twice: once in C# and once in TS. That duplication
creates inconsistency, maintenance cost, delivery friction, and human error.

## 2. Specific Problems Metano Must Solve

- duplication of shared types;
- duplication of enums, wrappers, and serialization contracts;
- duplication of small and medium domain rules;
- divergence in naming and JSON payload shape;
- difficulty sharing code across multiple projects/packages;
- manual adaptation of C# concepts into TypeScript.

## 3. General Objective

Provide a reliable mechanism to convert a deliberate and useful subset of C#
into executable TypeScript while preserving design intent and reducing manual
synchronization work across platforms.

## 4. Specific Objectives

- transform annotated C# types into `.ts` artifacts;
- reflect namespace structure and dependencies as TypeScript imports;
- preserve relevant semantics for records, classes, enums, interfaces,
  nullability, async flows, and supported collections;
- enable explicit customization through attributes;
- generate output suitable for publication and consumption as an npm package;
- integrate with standard .NET build workflows.

## 5. Scope

The following are in scope for Metano:

- discovery of transpileable types through attributes;
- Roslyn-based semantic transformation;
- generation of TypeScript AST and printed code;
- generation of barrels, imports, and `package.json`;
- supported BCL mappings;
- generation of minimal TypeScript runtime support code;
- cross-project and cross-package support;
- generation of serialization artifacts and type guards when applicable.

## 6. Out of Scope

The following are out of primary product scope:

- supporting the full C# language surface without restrictions;
- running IL/.NET in the browser;
- replacing frontend frameworks;
- inferring arbitrary .NET libraries automatically;
- broadly reproducing reflection-heavy or dynamic-dispatch scenarios;
- covering unsafe code or intensive use of unsupported APIs;
- acting as a generic OpenAPI client generator.
- end-to-end source-map style debugging from generated TypeScript back to C#
  source as a core product guarantee.

## 7. Strategic Constraints

The project deliberately assumes that:

- explicitness is preferable to magic;
- a well-supported subset is better than broad coverage with poor output;
- quality of generated TypeScript is more important than absolute fidelity to
  original C# syntax;
- integration with the JS ecosystem should prevail over full simulation of the
  .NET runtime.
