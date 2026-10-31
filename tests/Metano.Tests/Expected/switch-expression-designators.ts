import type { Clip } from "./clip";
import { ImageClip } from "./image-clip";
import { SilenceClip } from "./silence-clip";

export class ClipDescription {
  constructor() { }

  static of(clip: Clip): string {
    return (($s) => {
      if ($s instanceof ImageClip) {
        const image = $s;

        return `image ${image.path}`;
      }

      if ($s instanceof SilenceClip) {
        const silence = $s;

        return `silence ${silence.seconds}`;
      }

      throw new Error("Specified argument was out of the range of valid values. (Parameter 'clip')");
    })(clip);
  }
}
