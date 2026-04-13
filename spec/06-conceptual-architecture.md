# Conceptual Architecture

## 1. Overview

Conceptually, Metano is a semantic transpilation pipeline. The system takes a
C# project as input, discovers the relevant symbols, transforms them into a
TypeScript AST, and materializes output artifacts consumable as an npm package.

## 2. Macro Components

### 2.1 Annotations and Contracts Layer

Responsible for defining the product's declarative control language, namely the
attributes and mappings used by C# code to guide transpilation.

### 2.2 Compilation Core

Responsible for:

- loading and compiling C# projects;
- inspecting symbols and attributes;
- coordinating the transpilation process;
- exposing target-agnostic abstractions.

### 2.3 TypeScript Target

Responsible for:

- mapping C# constructs into TypeScript constructs;
- producing TypeScript AST;
- collecting imports and dependencies;
- printing final source files.

### 2.4 Build Integration

Responsible for connecting the transpiler to the consumer's build workflow.

### 2.5 TypeScript Runtime Support

Responsible only for the minimum required support needed for semantics and
structures that do not exist directly in native JavaScript.

## 3. Conceptual Flow

1. The user defines C# types and marks what should be transpiled.
2. The system loads the project through .NET/Roslyn compilation infrastructure.
3. The system discovers the eligible type set.
4. The system transforms types, members, expressions, and mappings into TS AST.
5. The system resolves imports, barrels, and package dependencies.
6. The system writes TypeScript output into the target directory.

## 4. System Boundaries

### Inside the system

- semantic analysis of the C# project;
- support decisions and transformation logic;
- generation of TS code;
- generation of package/layout support files.

### Outside the system

- execution of generated code in production;
- frontend framework selection;
- bundling and publication of the TS package;
- arbitrary unsupported C# scenarios outside product scope.

## 5. Architectural Principles

- **target-agnostic core**: the core must not depend on a single output target.
- **specialized target**: TypeScript-specific rules belong in the TS target.
- **transformation by responsibility**: lowering logic should be decomposed into
  focused transformers/handlers.
- **output as a first-class artifact**: AST, imports, and printing are core
  product concerns, not secondary details.
- **declarative extensibility**: attributes and mappings are the primary
  customization mechanism.

## 6. Desired Architectural Consequences

- easier addition of newly supported constructs;
- easier diagnosis of where a transformation failed;
- future ability to support new targets without rewriting the core;
- clear separation between “what the product promises” and “how a specific
  emission is implemented.”

## 7. ADR Crosswalk

The following ADR families are directly relevant to this specification:

- **ADR-0001**: target-agnostic core and per-target split.
- **ADR-0002**: handler decomposition for transformation logic.
- **ADR-0003**: declarative BCL mappings.
- **ADR-0004**: cross-project references via Roslyn.
- **ADR-0005**: inline-wrapper branded type strategy.
- **ADR-0006**: namespace-first barrel imports.
- **ADR-0007**: output conventions and file layout decisions.
- **ADR-0008**: overload dispatch strategy.
- **ADR-0009**: generated type guards.
- **ADR-0010**: diagnostic model and stable codes.
- **ADR-0011**: `[EmitPackage]` as package identity source of truth.
- **ADR-0012**: LINQ runtime strategy.

See [`11-adr-cross-reference.md`](./11-adr-cross-reference.md) for the
requirement-level mapping.
