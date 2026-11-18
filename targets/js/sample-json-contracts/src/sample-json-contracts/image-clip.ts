/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { Clip } from "./clip";

let ImageClip$rt: ClassType<ImageClip> | undefined;

export class ImageClip extends Clip {
  constructor(
    readonly path: string,
    seconds: number,
  ) {
    super(seconds);
  }

  static override get $runtimeType(): ClassType<ImageClip> {
    return (ImageClip$rt ??= {
      kind: "class",
      name: "ImageClip",
      fullName: "SampleJsonContracts.ImageClip",
      namespace: "SampleJsonContracts",
      ctor: ImageClip,
      base: () => Clip.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is ImageClip => v instanceof ImageClip,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.path === other.path;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.path);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<ImageClip>): ImageClip {
    return copyRecordWith<ImageClip>(this, overrides);
  }
}
