import { formatDouble } from "metano-runtime";
import { ArgumentOutOfRangeException } from "metano-runtime/system/exceptions/argument-out-of-range-exception";
import type { Clip } from "./clip";
import { ImageClip } from "./image-clip";
import { SilenceClip } from "./silence-clip";

export class ClipDescription {
  constructor() { }

  static of(clip: Clip): string {
    return (($s) => {
      if ($s instanceof ImageClip) {
        const image = $s;

        return `image ${image.path ?? ""}`;
      }

      if ($s instanceof SilenceClip) {
        const silence = $s;

        return `silence ${formatDouble(silence.seconds)}`;
      }

      throw new ArgumentOutOfRangeException("clip");
    })(clip);
  }
}
