# Metano Documentation

Welcome to the Metano documentation. If you're new here, start with the
[main README](../README.md) for the project overview, then come back here for
in-depth guides.

## Guides

### Getting started

- **[Getting Started](getting-started.md)** — Your first Metano project: install the
  CLI, annotate a C# class, run the transpiler, consume the generated TypeScript.

### Reference

- **[Attribute Reference](attributes.md)** — Every Metano attribute, what it does,
  and when to use it.
- **[BCL Type Mappings](bcl-mappings.md)** — How C# standard library types map to
  TypeScript (collections, temporal types, decimal, Guid, etc.).
- **[Cross-Project References](cross-package.md)** — Sharing types between C#
  projects and producing matching npm packages.
- **[JSON Serialization](serialization.md)** — Transpiling `JsonSerializerContext`
  to a TypeScript runtime serializer.

### Advanced

- **[Architecture Overview](architecture.md)** — How the transpiler works
  internally: the pipeline, AST, handlers, and where to extend.

## External docs

- **[Main README](../README.md)** — Project overview, features, installation
- **[CLAUDE.md](../CLAUDE.md)** — Guidance for contributors and AI assistants
- **[Sample projects](../samples/)** — Real examples with their own READMEs
- **[Architecture Decision Records](adr/)** — The "why" behind major design choices
- **[Roadmap / pending work](https://github.com/danfma/metano/issues)** — Feature backlog tracked on the GitHub issue tracker
