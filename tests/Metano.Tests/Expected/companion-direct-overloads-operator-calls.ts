import { Ticks } from "./ticks";
import type { UId } from "./u-id";

export class OperatorCalls {
  constructor() { }

  run(ticks: Ticks, id: UId): Ticks {
    return Ticks.__addTicksUId(Ticks.__addTicksInt64(ticks, 2n), id);
  }
}
