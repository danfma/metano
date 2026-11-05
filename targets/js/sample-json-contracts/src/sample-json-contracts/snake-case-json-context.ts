/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Stats } from "./stats";

export class SnakeCaseJsonContext extends SerializerContext {
  private static readonly _default: SnakeCaseJsonContext = new SnakeCaseJsonContext();

  private _stats?: TypeSpec<Stats>;

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
