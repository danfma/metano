import { IDisposable$runtimeType, type ClassType, type IDisposable } from "metano-runtime";

let Handle$rt: ClassType<Handle> | undefined;

export class Handle implements IDisposable {
  count: number = 0;

  constructor() { }

  static get $runtimeType(): ClassType<Handle> {
    return (Handle$rt ??= {
      kind: "class",
      name: "Handle",
      fullName: "Handle",
      namespace: "",
      ctor: Handle,
      interfaces: [
        () => IDisposable$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Handle(),
      test: (v: unknown): v is Handle => v instanceof Handle,
    });
  }

  dispose(): void {
    this.count = (this.count + 1) | 0;
  }
}
