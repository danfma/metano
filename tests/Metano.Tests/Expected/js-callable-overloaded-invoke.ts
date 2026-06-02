export class SignalConsumer {
  constructor() { }

  go(setCount: ((value: number) => void) & ((updater: (arg: number) => number) => void)): void {
    setCount(5);
    setCount((c: number) => c + 1);
  }
}
