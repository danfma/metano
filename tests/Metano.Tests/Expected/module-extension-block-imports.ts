import { plainInt32, plainString, size$get, twice } from "./block-host";

export class UseBlock {
  constructor() { }

  go(): number {
    return twice("x") + size$get("x") + plainInt32(1) + plainString("y");
  }
}
