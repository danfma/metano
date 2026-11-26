// Two generated IDisposable implementers that import each other (FR-111): the runtime's
// polyfill anchor must still run before either class key is evaluated.
import { disposeResource } from "./system/disposable.js";
import { Second } from "./dispose-cycle-b.js";

export class First {
  dispose() {}

  partner() {
    return new Second();
  }

  [Symbol.dispose]() {
    disposeResource(this, this.dispose);
  }
}
