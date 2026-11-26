import { IDisposable$runtimeType, disposeResource, type ClassType, type IDisposable } from "metano-runtime";

let Res$rt: ClassType<Res> | undefined;

export class Res implements IDisposable {
  declare count: number;

  constructor() {
    this.count = 0;
  }

  static get $runtimeType(): ClassType<Res> {
    return (Res$rt ??= {
      kind: "class",
      name: "Res",
      fullName: "Res",
      namespace: "",
      ctor: Res,
      interfaces: [
        () => IDisposable$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Res(),
      test: (v: unknown): v is Res => v instanceof Res,
    });
  }

  dispose(): void {
    this.count = (this.count + 1) | 0;
  }

  [Symbol.dispose](): void {
    disposeResource(this);
  }
}
