// The other half of the dispose-cycle-a.js import cycle.
import { disposeResource } from "./system/disposable.js";
import { First } from "./dispose-cycle-a.js";

export class Second {
  close() {}

  partner() {
    return new First();
  }

  [Symbol.dispose]() {
    disposeResource(this, this.close);
  }
}
