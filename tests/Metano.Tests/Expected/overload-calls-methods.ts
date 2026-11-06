import { Scale } from "./scale";

export class Caller {
  constructor() { }

  timesTwo(): number {
    return new Scale().applyF(2);
  }

  divided(): number {
    return Scale.identity().applyFD(8, 4);
  }

  ofWord(): number {
    return new Scale().applyMode("word");
  }

  typed(s: Scale): Scale {
    return s.applyF(1) === 2 ? s : Scale.identity();
  }
}
