import type { Ticks } from "./ticks";
import type { UId } from "./u-id";

export class OperatorCalls {
  constructor() { }

  run(ticks: Ticks, id: UId): Ticks {
    return ticks + 2n + id;
  }
}
