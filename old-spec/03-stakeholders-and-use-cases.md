# Stakeholders and Use Cases

## 1. Primary Stakeholders

### 1.1 .NET backend developer

Wants to keep shared domain models and contracts in C# without rewriting them
manually for the frontend.

### 1.2 TypeScript frontend developer

Wants to consume generated code that is typed, readable, predictable, and
compatible with the JS/TS ecosystem without depending on a heavy runtime.

### 1.3 Transpiler maintainer

Wants to evolve the product through a clear architecture, explicit support
boundaries, and consistent diagnostics.

### 1.4 AI agents and automation tools

Want a stable knowledge base that explains project intent, scope boundaries,
terms, and quality expectations.

## 2. Stakeholder Needs

- clarity about what is supported and what is not;
- predictability in output structure and conventions;
- explicit means to customize names, imports, and mappings;
- traceability from C# input to TS artifact and diagnostics;
- enough documentation for adoption and contribution.

## 3. Primary Use Cases

### UC-01 Transpile a domain assembly

A developer marks an assembly or selected types as transpileable and obtains
generated TypeScript files in a target package.

### UC-02 Share types and behavior across backend and frontend

A team defines records, classes, enums, and small domain rules in C# and
consumes them in the frontend as executable TypeScript.

### UC-03 Publish transpiled packages with inter-project dependencies

One C# project depends on another transpileable C# project, and Metano
generates the corresponding npm imports and package dependencies automatically.

### UC-04 Customize emission through attributes

A user changes output behavior using attributes for renaming, string enums,
branded types, type guards, modules, and external mappings.

### UC-05 Generate serialization artifacts

A user defines a `JsonSerializerContext` in C# and obtains a corresponding
TypeScript context with compile-time-resolved property names.

## 4. Success Criteria by Stakeholder

- backend: less duplicated code and less drift across platforms;
- frontend: natural consumption of output without “foreign code” feel;
- maintainer: easy evolution of features without architectural collapse;
- AI/tooling: ability to answer “what is Metano supposed to do?” without
  relying on informal inference.
