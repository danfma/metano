import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Account } from "./account";
import { Blob } from "./blob";
import { BX } from "./bx";
import { DX } from "./dx";
import { Init } from "./init";
import { Noted } from "./noted";
import { OrdBase } from "./ord-base";
import { OrdDer } from "./ord-der";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _blob?: TypeSpec<Blob>;

  private _account?: TypeSpec<Account>;

  private _ordDer?: TypeSpec<OrdDer>;

  private _byteArray?: ValueSpec<number[] | null>;

  private _init?: TypeSpec<Init>;

  private _noted?: TypeSpec<Noted>;

  private _bX?: TypeSpec<BX>;

  private _dX?: TypeSpec<DX>;

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
            element: { kind: "primitive", numberType: "System.Byte" },
          },
        },
      ],
    });
  }

  get account(): TypeSpec<Account> {
    return this._account ??= this.createSpec({
      type: Account,
      factory: () => new Account(),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
        {
          ts: "version",
          json: "Version",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "kept",
          json: "Kept",
          type: { kind: "primitive", numberType: "System.Int32" },
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
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      assignIfPresent: ["name", "version", "kept", "id", "hits"],
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
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "der2",
          json: "Der2",
          type: { kind: "primitive", numberType: "System.Int32" },
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

  get init(): TypeSpec<Init> {
    return this._init ??= this.createSpec({
      type: Init,
      factory: () => new Init(),
      properties: [
        {
          ts: "note",
          json: "Note",
          type: {
            kind: "nullable",
            inner: { kind: "primitive" },
          },
          optional: true,
        },
        {
          ts: "count",
          json: "Count",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "items",
          json: "Items",
          type: {
            kind: "nullable",
            inner: {
              kind: "array",
              element: { kind: "primitive", numberType: "System.Int32" },
            },
          },
          optional: true,
        },
      ],
      assignIfPresent: ["note", "count", "items"],
    });
  }

  get noted(): TypeSpec<Noted> {
    return this._noted ??= this.createSpec({
      type: Noted,
      factory: (p: Record<string, unknown>) => Object.assign(new Noted(p.a as number), { note: p.note as string | null }),
      properties: [
        {
          ts: "a",
          json: "A",
          type: { kind: "primitive", numberType: "System.Int32" },
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

  get bX(): TypeSpec<BX> {
    return this._bX ??= this.createSpec({
      type: BX,
      factory: (p: Record<string, unknown>) => new BX(p.x as number),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    });
  }

  get dX(): TypeSpec<DX> {
    return this._dX ??= this.createSpec({
      type: DX,
      base: this.bX,
      factory: () => new DX(),
      properties: [
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      assignIfPresent: ["x", "y"],
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
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "base2",
          json: "Base2",
          type: { kind: "primitive", numberType: "System.Int32" },
          order: 5,
        },
      ],
    });
  }
}
