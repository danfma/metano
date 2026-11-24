/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { IgnAbsDer } from "./ign-abs-der.js";
import { IgnAbs } from "./ign-abs.js";
import { IgnBase } from "./ign-base.js";
import { IgnDer } from "./ign-der.js";

let IgnoreJsonContext$rt: ClassType<IgnoreJsonContext> | undefined;

export class IgnoreJsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new IgnoreJsonContext() as IgnoreJsonContext;
  private _ignBase?: TypeSpec<IgnBase>;
  private _ignAbs?: TypeSpec<IgnAbs>;
  private _ignDer?: TypeSpec<IgnDer>;
  private _ignAbsDer?: TypeSpec<IgnAbsDer>;

  static get $runtimeType(): ClassType<IgnoreJsonContext> {
    return (IgnoreJsonContext$rt ??= {
      kind: "class",
      name: "IgnoreJsonContext",
      fullName: "SampleJsonContracts.IgnoreJsonContext",
      namespace: "SampleJsonContracts",
      ctor: IgnoreJsonContext,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new IgnoreJsonContext(),
      test: (v: unknown): v is IgnoreJsonContext => v instanceof IgnoreJsonContext,
    });
  }

  static get default(): IgnoreJsonContext {
    return IgnoreJsonContext._default;
  }

  get ignBase(): TypeSpec<IgnBase> {
    return (this._ignBase ??= this.createSpec({
      type: IgnBase,
      runtimeType: () => IgnBase.$runtimeType,
      factory: (p: Record<string, unknown>) => new IgnBase(p.v as number),
      properties: [
        {
          ts: "v",
          json: "V",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "k",
        derived: [
          {
            tag: "d",
            spec: () => this.ignDer,
          },
        ],
        ignoreUnrecognizedTags: true,
      },
    }));
  }

  get ignAbs(): TypeSpec<IgnAbs> {
    return (this._ignAbs ??= this.createSpec({
      type: IgnAbs,
      runtimeType: () => IgnAbs.$runtimeType,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'IgnAbs' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [
        {
          ts: "v",
          json: "V",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      polymorphism: {
        discriminator: "k",
        abstract: true,
        derived: [
          {
            tag: "d",
            spec: () => this.ignAbsDer,
          },
        ],
        ignoreUnrecognizedTags: true,
      },
    }));
  }

  get ignDer(): TypeSpec<IgnDer> {
    return (this._ignDer ??= this.createSpec({
      type: IgnDer,
      runtimeType: () => IgnDer.$runtimeType,
      base: this.ignBase,
      factory: (p: Record<string, unknown>) => new IgnDer(p.v as number, p.w as number),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }

  get ignAbsDer(): TypeSpec<IgnAbsDer> {
    return (this._ignAbsDer ??= this.createSpec({
      type: IgnAbsDer,
      runtimeType: () => IgnAbsDer.$runtimeType,
      base: this.ignAbs,
      factory: (p: Record<string, unknown>) => new IgnAbsDer(p.v as number, p.w as number),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    }));
  }
}
