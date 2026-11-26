import { Engine } from "./engine.js";
import { Log } from "./log.js";

export class Caller {
  get(): Engine {
    return new Engine();
  }

  ordered(): number {
    let $recv, $arg1, $arg0;

    return ($recv = this.get(), $arg1 = Log.g(), $arg0 = Log.f(), $recv.applyFD($arg0, $arg1));
  }

  plain(): number {
    return this.get().applyFD(1, 2);
  }
}
