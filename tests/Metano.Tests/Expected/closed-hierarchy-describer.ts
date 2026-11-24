import { formatDouble, type RuntimeType } from "metano-runtime";
import { SwitchExpressionException } from "metano-runtime/system/exceptions/switch-expression-exception";
import { Branch } from "./branch";
import { Circle } from "./circle";
import { Completed } from "./completed";
import { Failed } from "./failed";
import { Finished } from "./finished";
import type { JobStatus } from "./job-status";
import { Leaf } from "./leaf";
import { Queued } from "./queued";
import { Running } from "./running";
import type { Shape } from "./shape";
import { ShotSource } from "./shot-source";
import { Square } from "./square";
import type { Tree } from "./tree";

export class Describer {
  constructor() { }

  static describe(status: JobStatus): string {
    {
      let $s = status;

      if ($s instanceof Queued) {
        return "queued";
      }

      if ($s instanceof Running) {
        const percent = $s.percent;

        return `running ${percent}`;
      }

      if ($s instanceof Completed) {
        const done = $s;

        return `done in ${formatDouble(done.seconds)}`;
      }

      if ($s instanceof Failed) {
        const error = $s.error;

        return `failed: ${error ?? ""}`;
      }

      throw new SwitchExpressionException($s);
    }
  }

  static stage(status: JobStatus): string {
    {
      let $s = status;

      if ($s instanceof Queued) {
        return "waiting";
      }

      if ($s instanceof Running) {
        return "busy";
      }

      if ($s instanceof Finished) {
        return "over";
      }

      throw new SwitchExpressionException($s);
    }
  }

  static kind(shape: Shape): string {
    {
      let $s = shape;

      if ($s instanceof Circle) {
        return "circle";
      }

      if ($s instanceof Square) {
        return "square";
      }

      throw new SwitchExpressionException($s);
    }
  }

  static count<T>(tree: Tree<T>, T$type: RuntimeType<T>): number {
    {
      let $s = tree;

      if ($s instanceof Leaf) {
        return 1;
      }

      if ($s instanceof Branch) {
        const left = $s.left;
        const right = $s.right;

        return (Describer.count(left, T$type) + Describer.count(right, T$type)) | 0;
      }

      throw new SwitchExpressionException($s);
    }
  }

  static source(source: ShotSource): string {
    {
      let $s = source;

      if ($s instanceof ShotSource.Fresh) {
        return "fresh";
      }

      if ($s instanceof ShotSource.Reused) {
        const reused = $s;

        return `reused ${reused.from}`;
      }

      throw new SwitchExpressionException($s);
    }
  }

  static generic<TStatus extends JobStatus>(status: TStatus): string {
    {
      let $s = status;

      if ($s instanceof Queued) {
        return "queued";
      }

      if ($s instanceof Running) {
        return "running";
      }

      if ($s instanceof Finished) {
        return "finished";
      }

      throw new SwitchExpressionException($s);
    }
  }

  static isFresh(value: Object): boolean {
    return value instanceof ShotSource.Fresh;
  }

  static isOver(status: JobStatus): boolean {
    return status instanceof Finished;
  }

  static statement(shape: Shape): string {
    switch (true) {
      case shape instanceof Circle:
        return "round";
      case shape instanceof Square && ((square) => square.area > 4)(shape):
        {
          const square = shape;

          return "big square";
        }
      default:
        return "other";
    }
  }
}
