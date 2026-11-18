export class Effects {
  constructor() { }

  log(message: string): void {
    console.log(message);
    const a = 1; console.log(a);
    if ((globalThis as any).x) { console.log(1) };
    console.log(2) // a trailing comment
    ;
    void 0;
  }

  arrow(): void {
    console.log('arrow');
  }

  callback(): () => void {
    return () => {
      console.log('callback');
    };
  }
}
