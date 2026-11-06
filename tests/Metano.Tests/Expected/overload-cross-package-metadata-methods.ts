import { Scale, twice } from "lib/lib";

export class Till extends Scale {
  constructor() {
    super();
  }

  ring(): number {
    return this.apply(2);
  }

  split(): number {
    return this.apply(8, 4);
  }

  words(): number {
    return this.apply("ring");
  }

  twice(): number {
    return twice(21);
  }
}
