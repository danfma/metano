# Namespace-first Imports Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** alinhar o transpiler TypeScript ao modelo de namespaces do C#, fazendo com que imports usem barrels de namespace por padrão e reservem imports por arquivo apenas para fallback técnico.

**Architecture:** a mudança precisa acontecer no ponto onde o compilador resolve caminhos lógicos de import, não apenas no writer do `package.json`. O plano é introduzir uma camada explícita de "import target resolution" em `PathNaming`/`ImportCollector`, mudar a representação cross-package para apontar para o namespace barrel e então ajustar barrels, exports e testes para o novo contrato.

**Tech Stack:** C#, Roslyn, transpiler próprio do Metano, geração de `package.json`, testes NUnit, packages TS em `js/`.

---

## Contexto do problema

Hoje o comportamento do compilador é file-first:

- imports locais usam `#/namespace-path/type-name`
- imports cross-package usam `{package}/{namespace-path}/{type-name}` ou `{package}/{emit-in-file-name}`
- barrels (`index.ts`) são gerados, mas funcionam mais como API pública opcional do package do que como origem preferencial de import

Isso está explícito em:

- `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`
- `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs`
- `src/Metano.Compiler.TypeScript/Transformation/BarrelFileGenerator.cs`
- `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs`
- `tests/Metano.Tests/CrossPackageImportTests.cs`

E também aparece no output gerado, por exemplo:

- `js/sample-issue-tracker/src/issues/application/issue-service.ts`
- `js/sample-issue-tracker/src/issues/domain/issue.ts`

## Regra alvo

### Regra 1: assembly vira package

- O package continua sendo determinado por `[assembly: EmitPackage(...)]`.
- O root barrel do package deve ser um ponto de entrada real e estável para o namespace raiz.

### Regra 2: import vem do namespace, não do arquivo

- Import local desejado:
  - `import { Issue, IssueStatus } from "#/issues/domain"`
- Import cross-package desejado:
  - `import { Money } from "@scope/lib/domain"`
- Quando o namespace do tipo coincide com o root namespace do assembly:
  - `import { Widget } from "@scope/lib"`

### Regra 3: fallback file-first só quando necessário

- Casos candidatos:
  - ciclos entre barrels
  - colisão com `index.ts`
  - impossibilidade de um barrel re-exportar algo sem quebrar o grafo

---

## Task 1: Formalizar a estratégia de resolução de imports

**Files:**
- Modify: `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`
- Modify: `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs`
- Modify: `src/Metano.Compiler.TypeScript/Transformation/CyclicReferenceDetector.cs`
- Test: `tests/Metano.Tests/NamespaceTranspileTests.cs`

**Step 1: Introduzir um conceito explícito de import target**

Criar uma API clara para diferenciar:

- import por arquivo
- import por barrel de namespace
- import por root package

Evitar continuar usando apenas strings prontas.

**Step 2: Definir como detectar "namespace raiz"**

Reusar `RootNamespace` / `assemblyRootNamespace` para saber quando o path deve virar:

- `#/` internamente, ou equivalente decidido
- `@scope/pkg` externamente

**Step 3: Decidir o contrato do alias local `#/`**

Escolher uma destas abordagens e fixar em teste:

1. `#/` representa o root barrel do package
2. `#/foo/bar` representa sempre barrel de namespace

**Step 4: Escrever testes vermelhos para resolução namespace-first**

Cobrir:

- mesmo namespace
- subnamespace
- namespace raiz
- grouped file via `[EmitInFile]`

**Step 5: Implementar o mínimo para fazer os testes passarem**

---

## Task 2: Migrar imports locais do output para barrels de namespace

**Files:**
- Modify: `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`
- Modify: `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs`
- Test: `tests/Metano.Tests/NamespaceTranspileTests.cs`
- Test: `tests/Metano.Tests/EmitInFileTests.cs`

**Step 1: Ajustar `ComputeRelativeImportPath()`**

Hoje ele sempre acrescenta o nome do arquivo no final. A nova regra deve preferir o
barrel do namespace do símbolo referenciado.

**Step 2: Ajustar imports locais agrupados por `[EmitInFile]`**

Mesmo quando vários tipos moram no mesmo arquivo, o import desejado deve vir do barrel
do namespace, não do arquivo emitido, desde que o barrel exponha os nomes corretamente.

**Step 3: Preservar elisão de self-import**

Continuar evitando import quando o símbolo está no mesmo arquivo lógico.

**Step 4: Validar tipos type-only vs value**

Garantir que a mudança de path não quebre a lógica de `TypeOnly` e `TypeOnlyNames`.

---

## Task 3: Migrar imports cross-package para namespace barrel

**Files:**
- Modify: `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`
- Modify: `src/Metano.Compiler.TypeScript/Transformation/TypeMapper.cs`
- Modify: `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs`
- Test: `tests/Metano.Tests/CrossPackageImportTests.cs`
- Test: `tests/Metano.Tests/EndToEndOutputTests.cs`

**Step 1: Trocar `ComputeSubPath()` para namespace path**

O subpath anexado ao `TsTypeOrigin` deve representar o namespace barrel, não o arquivo.

**Step 2: Tratar root namespace como import do package root**

Quando o tipo estiver diretamente no namespace raiz do assembly produtor:

- desejado: `import { TypeA } from "@scope/pkg"`
- não desejado: `import { TypeA } from "@scope/pkg/type-a"`

**Step 3: Atualizar testes que hoje fixam file path**

Exemplos prováveis de atualização:

- `EmitInFile_CrossPackageImportUsesFilePath`
- `PlainObject_CrossPackage_EmitsAsTypeImport`
- `CrossPackageImport_MixedValueAndType_UsesPerNameQualifier`

**Step 4: Adicionar teste novo para root import**

Cobrir explicitamente:

- library namespace raiz = `AcmeLib`
- consumer importa `Widget`
- output esperado = `from "@acme/lib"`

---

## Task 4: Tornar os barrels e exports compatíveis com o novo contrato

**Files:**
- Modify: `src/Metano.Compiler.TypeScript/Transformation/BarrelFileGenerator.cs`
- Modify: `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs`
- Test: `tests/Metano.Tests/NamespaceTranspileTests.cs`
- Test: `tests/Metano.Tests/EmitPackageTests.cs`

**Step 1: Garantir root barrel consistente**

Confirmar que `index.ts` da raiz existe sempre que houver exports elegíveis e que ele
vira `exports["."]` no `package.json`.

**Step 2: Decidir política para exports por arquivo**

Opções:

1. manter exports por arquivo como fallback público
2. manter exports por arquivo só para compatibilidade temporária
3. esconder exports por arquivo no futuro

O plano recomendado é a opção 2, com migração gradual.

**Step 3: Revisar comentário e contrato do `BarrelFileGenerator`**

A documentação hoje assume explicitamente consumo por `package/issues/domain/issue`.
Ela precisa refletir a nova convenção.

**Step 4: Adicionar cobertura para export `"."`**

Validar que o package root fica importável quando o namespace coincide com o root namespace.

---

## Task 5: Implementar fallback controlado para ciclos e colisões

**Files:**
- Modify: `src/Metano.Compiler.TypeScript/Transformation/CyclicReferenceDetector.cs`
- Modify: `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs`
- Test: `tests/Metano.Tests/CyclicReferenceTests.cs`
- Test: `tests/Metano.Tests/NamespaceTranspileTests.cs`

**Step 1: Detectar quando barrel-first introduz um ciclo novo**

Precisamos distinguir:

- ciclo já existente entre tipos
- ciclo introduzido apenas pela mudança para barrel

**Step 2: Definir fallback mínimo**

Quando houver ciclo, cair para o menor escopo necessário:

- primeiro tentar arquivo
- não degradar o package inteiro para file-first

**Step 3: Cobrir colisão com `index.ts`**

Se um namespace não puder ter barrel por colisão, o import precisa continuar resolúvel.

---

## Task 6: Regenerar amostras e validar o output real

**Files:**
- Modify: `js/sample-issue-tracker/src/**/*.ts`
- Modify: `js/sample-issue-tracker/test/**/*.ts`
- Optional: `js/metano-runtime/**`

**Step 1: Regenerar `sample-issue-tracker`**

Rodar a geração após a mudança e conferir se imports como:

- `#/issues/domain/issue`
- `#/shared-kernel/page-request`

viram imports por namespace barrel.

**Step 2: Validar build e testes do sample**

Executar build TS e testes Bun.

**Step 3: Decidir escopo de `metano-runtime`**

Como `js/metano-runtime` parece ser runtime manual e não output puro do transpiler,
decidir se:

1. ele entra agora na mesma convenção
2. ele fica fora do escopo desta mudança

Recomendação: deixar fora do escopo inicial e tratar depois, para não misturar
mudança de contrato do transpiler com refactor manual do runtime.

---

## Riscos e decisões em aberto

- Importar do barrel pode introduzir ciclos onde hoje o arquivo direto não introduz.
- O alias local `#/` pode precisar ganhar semântica mais precisa para root package vs subpath.
- Manter exports por arquivo por compatibilidade pode mascarar regressões se os testes só
  validarem compilação; precisamos fixar asserts exatos de string.
- Se o package root não tiver barrel real, a regra `import { X } from "{package}"` vai
  parecer suportada no contrato, mas falhar no consumidor.

---

## Verificação

Antes de considerar a mudança concluída:

- `dotnet test` deve passar para `tests/Metano.Tests`
- os testes novos devem afirmar explicitamente imports barrel-first
- `js/sample-issue-tracker` deve compilar
- o output gerado deve evitar imports file-first, exceto nos testes que cobrem fallback
