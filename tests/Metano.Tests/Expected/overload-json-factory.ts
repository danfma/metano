import { Decimal } from "metano-runtime/system/decimal";
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Money } from "./money.js";
import { Tag } from "./tag.js";

export class ShopJsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new ShopJsonContext() as ShopJsonContext;
  private _money?: TypeSpec<Money>;
  private _tag?: TypeSpec<Tag>;

  static get default(): ShopJsonContext {
    return this._default;
  }

  get money(): TypeSpec<Money> {
    return this._money ??= this.createSpec({
      type: Money,
      runtimeType: () => Money.$runtimeType,
      factory: (p: Record<string, unknown>) => Object.assign(Money.fromAmountCurrency(p.amount as Decimal, p.currency as string), { note: p.note as string | null }),
      properties: [
        {
          ts: "amount",
          json: "Amount",
          type: { kind: "decimal", type: Decimal, typeName: "System.Decimal" },
        },
        {
          ts: "currency",
          json: "Currency",
          type: { kind: "primitive" },
        },
        {
          ts: "note",
          json: "Note",
          type: {
            kind: "nullable",
            inner: { kind: "primitive" },
          },
          optional: true,
        },
      ],
    });
  }

  get tag(): TypeSpec<Tag> {
    return this._tag ??= this.createSpec({
      type: Tag,
      runtimeType: () => Tag.$runtimeType,
      factory: (p: Record<string, unknown>) => Tag.fromName(p.name as string),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
      ],
    });
  }
}

import { isInt32, isString, type ClassType } from "metano-runtime";

let Tag$rt: ClassType<Tag> | undefined;

export class Tag {
  protected static readonly byTagId = /* @__PURE__ */ Symbol("Tag(int id)");
  protected static readonly byTagName = /* @__PURE__ */ Symbol("Tag(string name)");
  declare readonly name: string;

  constructor(overload: typeof Tag.byTagId, id: number);
  /** @internal */
  constructor(overload: typeof Tag.byTagName, name: string);
  constructor(id: number);
  /** @internal */
  constructor(name: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Tag.byTagId) {
      const id = args[1] as number;
      this.name = id.toString();

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Tag.byTagName) {
      const name = args[1] as string;
      this.name = name;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const id = args[0] as number;
      this.name = id.toString();

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const name = args[0] as string;
      this.name = name;

      return;
    }

    throw new Error("No matching constructor");
  }

  static get $runtimeType(): ClassType<Tag> {
    return (Tag$rt ??= {
      kind: "class",
      name: "Tag",
      fullName: "Tag",
      namespace: "",
      ctor: Tag,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Tag => v instanceof Tag,
    });
  }

  static fromId(id: number): Tag {
    return new Tag(Tag.byTagId, id);
  }

  static fromName(name: string): Tag {
    return new Tag(Tag.byTagName, name);
  }
}
