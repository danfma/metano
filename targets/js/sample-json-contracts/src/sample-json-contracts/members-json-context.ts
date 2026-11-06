/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Account } from "./account";
import { Blob } from "./blob";
import { InitRec } from "./init-rec";
import { OrdBase } from "./ord-base";
import { SetClass } from "./set-class";

export class MembersJsonContext extends SerializerContext {
  private static readonly _default: MembersJsonContext = new MembersJsonContext();

  private _blob?: TypeSpec<Blob>;

  private _account?: TypeSpec<Account>;

  private _ordBase?: TypeSpec<OrdBase>;

  private _initRec?: TypeSpec<InitRec>;

  private _setClass?: TypeSpec<SetClass>;

  static get default(): MembersJsonContext {
    return MembersJsonContext._default;
  }

  get blob(): TypeSpec<Blob> {
    return (this._blob ??= this.createSpec({
      type: Blob,
      factory: (p: Record<string, unknown>) =>
        new Blob(p.data as number[], p.maybe as number[] | null, p.numbers as number[]),
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
    }));
  }

  get account(): TypeSpec<Account> {
    return (this._account ??= this.createSpec({
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
      assignIfPresent: ["name", "version", "id", "hits"],
    }));
  }

  get ordBase(): TypeSpec<OrdBase> {
    return (this._ordBase ??= this.createSpec({
      type: OrdBase,
      factory: (p: Record<string, unknown>) =>
        new OrdBase(p.second as number, p.first as number, p.third as number),
      properties: [
        {
          ts: "second",
          json: "Second",
          type: { kind: "primitive", numberType: "System.Int32" },
          order: 5,
        },
        {
          ts: "first",
          json: "First",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "third",
          json: "Third",
          type: { kind: "primitive", numberType: "System.Int32" },
          order: 3,
        },
      ],
    }));
  }

  get initRec(): TypeSpec<InitRec> {
    return (this._initRec ??= this.createSpec({
      type: InitRec,
      factory: (p: Record<string, unknown>) =>
        Object.assign(new InitRec(p.a as number), { note: p.note as string | null }),
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
    }));
  }

  get setClass(): TypeSpec<SetClass> {
    return (this._setClass ??= this.createSpec({
      type: SetClass,
      factory: (p: Record<string, unknown>) => Object.assign(new SetClass(), { n: p.n as number }),
      properties: [
        {
          ts: "n",
          json: "N",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
        {
          ts: "count",
          json: "Count",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
      assignIfPresent: ["count"],
    }));
  }
}
