import { Engine } from "./engine.js";
import { Log } from "./log.js";

export class Caller {
  constructor() { }

  get(): Engine {
    return new Engine();
  }

  ordered(): number {
    return (($recv, $arg1, $arg0) => $recv.applyFD($arg0, $arg1))(this.get(), Log.g(), Log.f());
  }

  plain(): number {
    return this.get().applyFD(1, 2);
  }
}
