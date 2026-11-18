/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Stats } from "./stats";

let SnakeCaseJsonContext$rt: ClassType<SnakeCaseJsonContext> | undefined;

export class SnakeCaseJsonContext extends SerializerContext {
  private static readonly _default =
    /* @__PURE__ */ new SnakeCaseJsonContext() as SnakeCaseJsonContext;
  private _stats?: TypeSpec<Stats>;

  static get $runtimeType(): ClassType<SnakeCaseJsonContext> {
    return (SnakeCaseJsonContext$rt ??= {
      kind: "class",
      name: "SnakeCaseJsonContext",
      fullName: "SampleJsonContracts.SnakeCaseJsonContext",
      namespace: "SampleJsonContracts",
      ctor: SnakeCaseJsonContext,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new SnakeCaseJsonContext(),
      test: (v: unknown): v is SnakeCaseJsonContext => v instanceof SnakeCaseJsonContext,
    });
  }

  static get default(): SnakeCaseJsonContext {
    return SnakeCaseJsonContext._default;
  }

  get stats(): TypeSpec<Stats> {
    return (this._stats ??= this.createSpec({
      type: Stats,
      factory: (p: Record<string, unknown>) =>
        new Stats(p.count as number, p.ratio as number, p.userName as string),
      properties: [
        {
          ts: "count",
          json: "count",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "ratio",
          json: "ratio",
          type: { kind: "primitive", numberType: "System.Double" },
        },
        {
          ts: "userName",
          json: "user_name",
          type: { kind: "primitive" },
        },
      ],
    }));
  }
}
