import { Base } from "lib/lib";

export class Derived extends Base {
  constructor() {
    super();
  }

  override addX(x: number): number {
    return x * 2;
  }

  override addS$(s: string): number {
    return 3;
  }
}
