import { HashCode, copyRecordWith } from "metano-runtime";
import { Clip } from "./clip";

export class ImageClip extends Clip {
  constructor(readonly path: string, seconds: number) {
    super(seconds);
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
