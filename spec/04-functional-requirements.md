# Functional Requirements

## 1. Convention

The requirements below describe expected Metano system behavior. Each
requirement uses a stable identifier in the format `FR-XXX`.

## 2. Discovery and Selection Requirements

- **FR-001** The system shall identify transpileable types based on explicit
  type-level and assembly-level attributes.
- **FR-002** The system shall support transpiling an entire assembly through an
  assembly-level opt-in mechanism.
- **FR-003** The system shall allow specific types to be excluded even when an
  assembly is configured to transpile all public types.
- **FR-004** The system shall distinguish between “type known for resolution”
  and “type that actually emits a file,” enabling declaration-only reference and
  import scenarios.

## 3. Semantic Transformation Requirements

- **FR-005** The system shall transform C# enums into TypeScript
  representations compatible with the configured strategy, including numeric and
  string enum forms.
- **FR-006** The system shall transform records into TypeScript artifacts that
  preserve structural identity and relevant output behavior.
- **FR-007** The system shall transform supported classes and inheritance
  constructs into equivalent TypeScript constructs.
- **FR-008** The system shall transform C# interfaces into TypeScript
  interfaces, including supported generic scenarios.
- **FR-009** The system shall map C# nullability into equivalent TypeScript
  representations.
- **FR-010** The system shall map `Task` and `ValueTask` into equivalent
  asynchronous abstractions in TypeScript.
- **FR-011** The system shall transform supported exception constructs into
  forms compatible with the JavaScript/TypeScript error model.
- **FR-012** The system shall transform supported operators and overloads in a
  semantically consistent way, including supported user-defined unary and
  binary operators, built-in increment/decrement forms, and supported
  comparison and type-test lowerings.
- **FR-013** The system shall transform supported pattern matching and switch
  constructs into functionally equivalent TypeScript.
- **FR-014** The system shall transform supported expressions, invocations,
  member access, object creation, lambdas, and string interpolation constructs.

## 4. Types and Collections Requirements

- **FR-015** The system shall map supported primitive and BCL types to
  TypeScript targets defined by the product.
- **FR-016** The system shall support supported collection categories,
  including lists, dictionaries, sets, queues, stacks, and immutable
  collection mappings defined by the product.
- **FR-017** The system shall support a LINQ layer for the subset of operations
  defined by the product.
- **FR-018** The system shall support inline wrappers as branded or opaque
  TypeScript-compatible types with minimal or zero runtime cost when configured
  to do so.

## 5. Attribute-Driven Customization Requirements

- **FR-019** The system shall allow types and members to be renamed in
  TypeScript output.
- **FR-020** The system shall allow specific members to be omitted from output.
- **FR-021** The system shall allow types to be emitted as plain objects when
  DTO-like shape output is desired.
- **FR-022** The system shall allow static classes to be emitted as TypeScript
  modules when explicitly configured.
- **FR-023** The system shall allow generation of type guards when requested,
  emitting both a narrowing predicate (`isT`) and a throwing assertion
  companion (`assertT(value, message?)`) for every guardable type.
- **FR-024** The system shall allow multiple types to be grouped into a single
  TypeScript file when explicitly configured.
- **FR-025** The system shall allow declaration of external imports and
  mappings to JavaScript/npm libraries.
- **FR-026** The system shall allow declarative templates and mappings for
  lowering supported APIs.

## 6. Output Requirements

- **FR-027** The system shall generate `.ts` files from transpiled types.
- **FR-028** The system shall generate imports consistent with symbol origin.
- **FR-029** The system shall generate barrels/index files consistent with
  namespaces and product output conventions.
- **FR-030** The system shall generate or update the target package's
  `package.json` with correct `imports`, `exports`, and `dependencies` fields.
  When the transpiler output directory is a subdirectory of the package's
  TypeScript source root (e.g., `src/domain/` inside a package whose build
  tool compiles from `src/`), the generated `imports` and `exports` paths
  shall include the correct prefix so that dist paths mirror the source tree
  structure. The system shall accept an explicit source-root parameter
  (`--src-root` / `MetanoSrcRoot`, defaulting to the first path segment of
  the output directory relative to the package root) to resolve ambiguity
  when the relationship between source and dist directories cannot be
  inferred.
- **FR-031** The system shall reflect cross-package dependencies between
  transpiled assemblies through correct npm-based imports.

## 7. Serialization and Validation Requirements

- **FR-032** The system shall transform `System.Text.Json`-based serialization
  contexts into equivalent contexts consumable in the TypeScript runtime.
- **FR-033** The system shall resolve JSON property names and naming policies at
  transpile time.
- **FR-034** The system shall generate enough metadata to support
  serialization/deserialization of supported types.

## 8. Integration Requirements

- **FR-035** The system shall integrate with standard .NET build workflows.
- **FR-036** The system shall allow execution through a dedicated CLI/tooling
  entry point.
- **FR-037** The system shall operate on real C# projects, using compilation and
  semantic analysis as the basis for transformation.
- **FR-046** The product shall support consumption of `Metano` and
  `Metano.Build` as build-only dependencies in .NET projects, so adopting the
  transpiler does not unnecessarily contribute runtime or transitive package
  surface to downstream application outputs.

## 9. Diagnostic Requirements

- **FR-038** The system shall produce diagnostics when it detects invalid,
  ambiguous, or unsupported scenarios.
- **FR-039** The system shall provide stable diagnostic identifiers to support
  troubleshooting and automation, including the stable `MS0001` through
  `MS0008` catalog.
- **FR-040** The system shall fail explicitly when it cannot generate correct
  output within the product's supported rules.

## 10. Advanced Emission and Runtime Semantics Requirements

- **FR-041** The system shall support delegate and event lowering, including
  event subscription and unsubscription transformations and the required
  runtime helper imports.
- **FR-042** The system shall support method and constructor overload dispatch
  for supported overload groups through generated runtime dispatchers and
  specialized fast-path implementations.
- **FR-043** The system shall support module entry points and C# top-level
  statements by lowering them into module-level TypeScript statements, subject
  to explicit validation rules.
- **FR-044** The system shall support C# collection expressions within the
  supported language surface.
- **FR-045** The system shall detect cyclic local-package import chains between
  generated TypeScript files and report them through diagnostics.
- **FR-047** The system shall derive cross-package TypeScript import and export
  subpaths from C# namespace structure and declared package identity, not from
  incidental file placement details in the generated output tree.
