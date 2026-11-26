import { Key } from "./key.js";
import { UId } from "./u-id.js";

export class KeyCalls {
  declare private _counter: number;

  constructor() {
    this._counter = 0;
  }

  private next(): number {
    return this._counter = (this._counter + 1) | 0;
  }

  private nextId(): UId {
    return UId.create(BigInt(this._counter = (this._counter + 1) | 0));
  }

  run(): string {
    let $arg1, $arg0;

    return (Key.xL$(1n) ?? "") + (Key.xU(UId.create(1n)) ?? "") + (Key.xL("s") ?? "") + (Key.pinViaLong(1n) ?? "") + (Key.pinU(UId.create(1n)) ?? "") + (Key.vU(UId.create(1n)) ?? "") + (Key.dUN(UId.create(1n), 5) ?? "") + (($arg1 = this.next(), $arg0 = this.nextId(), Key.dUN($arg0, $arg1)) ?? "");
  }
}
