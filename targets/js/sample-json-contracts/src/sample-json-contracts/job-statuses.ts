/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { formatDouble } from "metano-runtime";
import { SwitchExpressionException } from "metano-runtime/system/exceptions/switch-expression-exception";
import { JobCompleted } from "./job-completed.js";
import { JobError } from "./job-error.js";
import { JobFinished } from "./job-finished.js";
import { JobQueued } from "./job-queued.js";
import { JobRunning } from "./job-running.js";
import type { JobStatus } from "./job-status.js";

export class JobStatuses {
  private constructor() {}

  static asFinished(value: Object): JobFinished | null {
    return value instanceof JobFinished ? value : null;
  }

  static describe(status: JobStatus): string {
    {
      const $s = status;

      if ($s instanceof JobQueued) {
        return "queued";
      }

      if ($s instanceof JobRunning) {
        const running = $s;

        return `running ${running.percent}`;
      }

      if ($s instanceof JobCompleted) {
        const completed = $s;

        return `done in ${formatDouble(completed.seconds)}`;
      }

      if ($s instanceof JobError) {
        const failed = $s;

        return `failed: ${failed.error}`;
      }

      throw new SwitchExpressionException($s);
    }
  }
}
