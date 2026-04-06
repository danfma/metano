# MetaSharp — Plano: Value Wrappers

## Contexto

Hoje structs como `UserId`, `IssueId` e similares geram classes completas em TypeScript, o que introduz overhead de runtime e ruído de API para tipos cujo objetivo principal e apenas diferenciar semanticamente um primitivo.

## Decisao de Naming

- Nome recomendado do atributo: `[InlineWrapper]`
- Motivo:
  - Comunica claramente que e um wrapper sem sugerir inlining de compilador.
  - Evita ambiguidade de `Inline`.
  - Mantem a semantica de dominio focada em wrappers de valor.

## Objetivo de Saida (TS)

Para wrappers elegiveis, gerar:

```ts
export type UserId = string & { readonly __brand: "UserId" };

export namespace UserId {
  export function create(value: string): UserId { return value as UserId; }
  export function newId(): UserId { return crypto.randomUUID().replace(/-/g, "") as UserId; }
  export function system(): UserId { return "system" as UserId; }
}
```

## Regras de Elegibilidade

Um tipo e tratado como value wrapper quando:

1. E `struct` com `[Transpile]`.
2. Possui `[InlineWrapper]`.
3. Possui exatamente um valor de dados (campo/propriedade) mapeavel para primitivo TS:
   - `string`, `number`, `boolean`, `bigint` (e aliases BCL ja mapeados para esses tipos).
4. Nao depende de estado adicional obrigatorio para invariantes.

Se falhar alguma regra, emitir diagnostic explicito e seguir fluxo normal (classe/struct atual).

## Algoritmo de Transformacao

## 1) Descoberta do wrapper

- Durante `TypeTransformer`, detectar atributo e validar elegibilidade.
- Extrair:
  - `WrapperName` (ex: `UserId`)
  - `InnerType` (ex: `string`)
  - `BrandLiteral` (ex: `"UserId"`)

## 2) Emissao do tipo branded

- Gerar `TsTypeAlias`:
  - `type WrapperName = InnerType & { readonly __brand: "WrapperName" }`

## 3) Emissao do namespace companion

- Gerar namespace `WrapperName` contendo:
  - `create(value: InnerType): WrapperName`
  - Metodos estaticos do C# convertidos para funcoes no namespace:
    - retorno `WrapperName` quando aplicavel
    - corpo transpile normal, com cast final para `WrapperName` se necessario

## 4) Constructor mapping

- `new WrapperName(v)` no C# deve virar `WrapperName.create(v)` no TS.
- Se houver factories estaticas (`New`, `System`, etc.), manter como funcoes nominais no namespace.

## 5) Uso de tipos no restante do transpile

- Parametros, propriedades e retornos que referenciam o wrapper usam o alias branded.
- `instanceof WrapperName` nao se aplica (wrapper nao e classe):
  - quando detectado, emitir diagnostic e sugerir guard dedicado.

## 6) Runtime helpers

- Evitar runtime helper obrigatorio para manter zero overhead.
- Guard opcional por wrapper pode ser adicionado no futuro (`isUserId`) se habilitado por flag.

## Diagnosticos Propostos

- `MSW001`: `[InlineWrapper]` em tipo nao-struct.
- `MSW002`: wrapper sem exatamente um campo/propriedade de valor.
- `MSW003`: tipo interno nao suportado para branded primitive.
- `MSW004`: uso de `instanceof` com value wrapper.
- `MSW005`: uso de atributo legado para wrappers (sugerir `[InlineWrapper]`).

## Plano de Implementacao (fases)

1. **Annotations**
   - adicionar `InlineWrapperAttribute`

2. **Detection + diagnostics**
   - detector central de elegibilidade
   - emissao de diagnostics `MSWxxx`

3. **AST emission**
   - type alias branded
   - namespace companion com `create` + estaticos

4. **Call-site rewriting**
   - `new Wrapper(x)` -> `Wrapper.create(x)`
   - ajustes em conversoes/casts necessarias

5. **Tests**
   - caso feliz com `[InlineWrapper]`
   - casos invalidos (diagnostics)
   - scenario em `SampleIssueTracker` (IDs)

## Progresso de Implementacao (checkpoints)

- [x] Checkpoint 1: `InlineWrapperAttribute` adicionado em `MetaSharp.Annotations`.
- [x] Checkpoint 2: `SymbolHelper.HasInlineWrapper()` adicionado.
- [x] Checkpoint 3: branch de transformacao dedicada no `TypeTransformer` para wrappers elegiveis.
- [x] Checkpoint 4: emissao inicial de branded alias + companion `const` com `create` e metodos estaticos.
- [x] Checkpoint 5: rewrite de `new Wrapper(...)` para `Wrapper.create(...)` no `ExpressionTransformer`.
- [x] Checkpoint 6: coleta de imports atualizada para `TsConstObject` e `TsIntersectionType`.
- [x] Checkpoint 7: testes automatizados de InlineWrapper.
- [x] Checkpoint 8: validacao completa (`dotnet build` + `dotnet run --project MetaSharp.Tests/`).

### Nota de implementacao atual

- O companion foi emitido como `export const WrapperName = { ... } as const` (usando `TsConstObject`) para manter o AST atual simples e reaproveitar infraestrutura existente.
- Isso preserva o objetivo funcional (`WrapperName.create(...)`, metodos estaticos e branded type).

## Criterios de Aceitacao

- Wrapper elegivel nao gera classe TS.
- API gerada preserva ergonomia de factories estaticas.
- `UserId` e `IssueId` permanecem incompativeis em nivel de tipo.
- Nao ha regressao na suite existente.
- Diagnostics claros em casos invalidos.

## Riscos e Mitigacoes

- **Risco:** naming gerar confusao com semantica de runtime.
  - **Mitigacao:** documentar explicitamente que o resultado e branded type + namespace companion.
- **Risco:** rewriting de `new` perder casos de borda.
  - **Mitigacao:** testes com constructors simples, overloads e static factories.
- **Risco:** complexidade crescer no transformer.
  - **Mitigacao:** extrair helper dedicado `InlineWrapperTransformer`.
