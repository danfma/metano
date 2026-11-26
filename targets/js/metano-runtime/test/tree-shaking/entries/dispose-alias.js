// A class implementing IDisposable, as generated code declares its `[Symbol.dispose]()`
// alias (spec FR-111), disposed by a `using` declaration's protocol call.
import { disposeResource } from "./system/disposable.js";

export class Resource {
  dispose() {}

  [Symbol.dispose]() {
    disposeResource(this, this.dispose);
  }
}

export function release() {
  new Resource()[Symbol.dispose]();
}
