import { formatDouble } from "metano-runtime";
import { ArgumentOutOfRangeException } from "metano-runtime/system/exceptions/argument-out-of-range-exception";
import type { Clip } from "./clip.js";
import { ImageClip } from "./image-clip.js";
import { SilenceClip } from "./silence-clip.js";

export class ClipDescription {
  private constructor() { }

  static of(clip: Clip): string {
    {
      let $s = clip;

      if ($s instanceof ImageClip) {
        const image = $s;

        return `image ${image.path ?? ""}`;
      }

      if ($s instanceof SilenceClip) {
        const silence = $s;

        return `silence ${formatDouble(silence.seconds)}`;
      }

      throw new ArgumentOutOfRangeException("clip");
    }
  }
}
