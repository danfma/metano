export abstract class Widget {
  abstract build(): string;
}

import { Widget } from "./widget.js";

export abstract class StatefulWidget extends Widget {
  constructor() {
    super();
  }

  protected abstract override build(context: number): Widget;

  override build(): string {
    return "ok";
  }
}
