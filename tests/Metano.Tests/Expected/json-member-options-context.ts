import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Account } from "./account";
import { Blob } from "./blob";
import { OrdBase } from "./ord-base";
import { OrdDer } from "./ord-der";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _blob?: TypeSpec<Blob>;

  private _account?: TypeSpec<Account>;

  private _ordDer?: TypeSpec<OrdDer>;

  private _byteArray?: ValueSpec<number[] | null>;

  private _ordBase?: TypeSpec<OrdBase>;

  static get default(): JsonContext {
    return this._default;
  }

  get blob(): TypeSpec<Blob> {
    return this._blob ??= this.createSpec({
      type: Blob,
      factory: (p: Record<string, unknown>) => new Blob(p.data as number[], p.maybe as number[] | null, p.numbers as number[]),
      properties: [
        {
          ts: "data",
          json: "Data",
          type: { kind: "bytes" },
        },
        {
          ts: "maybe",
          json: "Maybe",
          type: {
            kind: "nullable",
            inner: { kind: "bytes" },
          },
          optional: true,
        },
        {
          ts: "numbers",
          json: "Numbers",
          type: {
            kind: "array",
            element: { kind: "primitive" },
          },
        },
      ],
    });
  }

  get account(): TypeSpec<Account> {
    return this._account ??= this.createSpec({
      type: Account,
      factory: (p: Record<string, unknown>) => Object.assign(new Account(), {
        name: p.name as string,
        version: p.version as number,
        kept: p.kept as number,
        id: p.id as string,
        hits: p.hits as number,
      }),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
        {
          ts: "version",
          json: "Version",
          type: { kind: "primitive" },
        },
        {
          ts: "kept",
          json: "Kept",
          type: { kind: "primitive" },
        },
        {
          ts: "id",
          json: "Id",
          type: { kind: "primitive" },
          order: -1,
        },
        {
          ts: "hits",
          json: "Hits",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get ordDer(): TypeSpec<OrdDer> {
    return this._ordDer ??= this.createSpec({
      type: OrdDer,
      base: this.ordBase,
      factory: (p: Record<string, unknown>) => Object.assign(new OrdDer(p.base1 as number, p.der1 as number), { base2: p.base2 as number, der2: p.der2 as number }),
      properties: [
        {
          ts: "der1",
          json: "Der1",
          type: { kind: "primitive" },
        },
        {
          ts: "der2",
          json: "Der2",
          type: { kind: "primitive" },
          order: -5,
        },
      ],
    });
  }

  get byteArray(): ValueSpec<number[] | null> {
    return this._byteArray ??= this.createValueSpec({
      descriptor: { kind: "bytes" },
    });
  }

  get ordBase(): TypeSpec<OrdBase> {
    return this._ordBase ??= this.createSpec({
      type: OrdBase,
      factory: (p: Record<string, unknown>) => Object.assign(new OrdBase(p.base1 as number), { base2: p.base2 as number }),
      properties: [
        {
          ts: "base1",
          json: "Base1",
          type: { kind: "primitive" },
        },
        {
          ts: "base2",
          json: "Base2",
          type: { kind: "primitive" },
          order: 5,
        },
      ],
    });
  }
}
