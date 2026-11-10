import { Key } from "./key";
import { UId } from "./u-id";

export class KeyCalls {
  private _counter: number = 0;

  constructor() { }

  private next(): number {
    return ++this._counter;
  }

  private nextId(): UId {
    return UId.create(BigInt(++this._counter));
  }

  run(): string {
    return Key.xL$(1n) + Key.xU(UId.create(1n)) + Key.xL("s") + Key.pinViaLong(1n) + Key.pinU(UId.create(1n)) + Key.vU(UId.create(1n)) + Key.dUN(UId.create(1n), 5) + (($arg1, $arg0) => Key.dUN($arg0, $arg1))(this.next(), this.nextId());
  }
}
