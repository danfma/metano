export abstract class Widget {
  constructor() { }

  abstract build(): string;
}

import { Widget } from "./widget.js";

export abstract class StatefulWidget extends Widget {
  constructor() {
    super();
  }

  protected abstract build(context: number): Widget;

  build(): string {
    return "ok";
  }
}
