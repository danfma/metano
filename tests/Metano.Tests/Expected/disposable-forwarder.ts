import { IDisposable$runtimeType, type ClassType, type IDisposable } from "metano-runtime";
import { OwnerNode } from "./owner-node.js";

let EffectNode$rt: ClassType<EffectNode> | undefined;

export class EffectNode extends OwnerNode implements IDisposable {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<EffectNode> {
    return (EffectNode$rt ??= {
      kind: "class",
      name: "EffectNode",
      fullName: "EffectNode",
      namespace: "",
      ctor: EffectNode,
      base: () => OwnerNode.$runtimeType,
      interfaces: [
        () => IDisposable$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new EffectNode(),
      test: (v: unknown): v is EffectNode => v instanceof EffectNode,
    });
  }
}
