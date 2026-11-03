export class PrimaryCollisions {
  f: (arg: number) => number;

  readonly g: (arg: number) => number;

  constructor(total: number) {
    this.f = (total$1: number) => total$1 + total;
    this.g = (total$2: number) => total$2 * total;
  }
}
