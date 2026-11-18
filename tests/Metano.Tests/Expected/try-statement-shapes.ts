import { toException, tryFilter } from "metano-runtime";
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export class Runner {
  private readonly _log: string[] = [] as Array<string>;

  constructor() { }

  async runAsync(name: string): Promise<string> {
    try {
      const text = await this.loadAsync(name);
      try {
        this._log.push(text);
      } catch (e$) {
        const e0 = toException(e$);
        if (e0 instanceof InvalidOperationException) {
          this._log.push("inner");
          throw e$;
        } else {
          throw e$;
        }
      }

      return text;
    } catch (e$) {
      const e0 = toException(e$);
      if (e0 instanceof ArgumentException && tryFilter((e) => e.paramName === "name", e0)) {
        const e = e0;

        return `bad\n${e.message}`;
      } else {
        throw e$;
      }
    } finally {
      this._log.push("done");
    }
  }

  guarded(): void {
    let step = 0;
    try {
      step = 1;
    } finally {
      this._log.push("step " + step);
    }
  }

  private loadAsync(name: string): Promise<string> {
    return Promise.resolve(name);
  }
}
