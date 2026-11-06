import { Scale, twice } from "lib/lib";

export class Till extends Scale {
  constructor() {
    super(Scale.byScaleDefault);
  }

  ring(): number {
    return this.applyF(2);
  }

  split(): number {
    return this.applyFD(8, 4);
  }

  words(): number {
    return this.applyMode("ring");
  }

  twice(): number {
    return twice(21);
  }
}
