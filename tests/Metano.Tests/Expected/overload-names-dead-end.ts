export class Generic {
  constructor() { }

  mInt32$1<T>(x: number): number {
    return 1;
  }

  mInt32$2(x: number): number {
    return 2;
  }

  m<T>(x: number): number {
    return this.mInt32$1<T>(x);
  }
}
