# Metano Formal Specification

This directory contains the formal requirements specification for **Metano**,
with primary focus on the product as a **C# to TypeScript transpiler**.

The goal of this knowledge base is to serve as a source of truth for:

- product development and prioritization;
- high-level architectural understanding;
- alignment between documentation, implementation, and tests;
- use by AI agents as stable, normative context.

## Principles

- **Transpiler-centric**: the system of interest is the core that transforms
  annotated C# code into executable, idiomatic TypeScript.
- **Complementary**: this directory does not replace `docs/` or ADRs; it
  consolidates what the product is expected to be, why it exists, and which
  requirements it must satisfy.
- **Traceable**: functional and non-functional requirements use stable IDs to
  support references from issues, tests, commits, and prompts.
- **Product-focused**: implementation details are mentioned only when they help
  define expected system behavior.

## Structure

- [`01-product-vision.md`](./01-product-vision.md): product definition, value
  proposition, and vision.
- [`02-problem-scope-and-objectives.md`](./02-problem-scope-and-objectives.md):
  problem statement, goals, scope, and non-scope.
- [`03-stakeholders-and-use-cases.md`](./03-stakeholders-and-use-cases.md):
  user profiles, needs, and primary usage flows.
- [`04-functional-requirements.md`](./04-functional-requirements.md):
  normative functional requirements for the transpiler.
- [`05-non-functional-requirements.md`](./05-non-functional-requirements.md):
  expected quality attributes of the system.
- [`06-conceptual-architecture.md`](./06-conceptual-architecture.md):
  conceptual view of the solution and its subsystems.
- [`07-glossary.md`](./07-glossary.md): canonical terms and definitions.
- [`08-feature-support-matrix.md`](./08-feature-support-matrix.md): support
  matrix for language constructs and major product capabilities.
- [`09-attribute-catalog.md`](./09-attribute-catalog.md): catalog of currently
  available Metano attributes and related annotations.
- [`10-diagnostic-catalog.md`](./10-diagnostic-catalog.md): formal catalog of
  stable diagnostic codes exposed by the transpiler.
- [`11-adr-cross-reference.md`](./11-adr-cross-reference.md): mapping between
  this specification and architectural decisions recorded in `docs/adr/`.

## Relationship to Other Directories

- `docs/`: explanatory documentation and user/contributor guides.
- `docs/adr/`: architectural decisions recorded over time.

## Editorial Rule

When updating this directory, prioritize:

- normative and unambiguous language;
- clear separation between requirement, decision, and implementation;
- consistency with Metano's core vision as a C# -> TS transpiler;
- terminology aligned with the glossary.
