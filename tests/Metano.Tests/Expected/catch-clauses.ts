export class AppException extends Error {
  constructor(message: string, readonly code: number) {
    super(message);
    this.code = code;
  }
}

export class NotFoundException extends AppException {
  constructor(message: string) {
    super(message, 404);
  }
}

export class Handlers {
  private readonly _log: string[] = [] as Array<string>;

  verbose: boolean = false;

  constructor() { }

  work(kind: string): void {
    if (kind === "missing") {
      throw new NotFoundException(kind);
    }

    if (kind === "app") {
      throw new AppException(kind, 1);
    }

    if (kind === "invalid") {
      throw new Error(kind);
    }
  }

  typed(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof NotFoundException) {
        return "missing";
      } else if (e$ instanceof AppException) {
        const e = e$;

        return "app:" + e.code;
      } else {
        throw e$;
      }
    }
  }

  singleTyped(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof NotFoundException) {
        const e = e$;

        return "missing:" + e.code;
      } else {
        throw e$;
      }
    }
  }

  generalLast(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof AppException) {
        const e = e$;

        return "app:" + e.code;
      } else {
        return "general";
      }
    }
  }

  exceptionLast(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof NotFoundException) {
        return "missing";
      } else {
        const e = e$;

        return e instanceof AppException ? "app" : "any";
      }
    }
  }

  filtered(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof AppException && ((e) => e.code === 404)(e$)) {
        return "missing";
      } else if (e$ instanceof AppException && this.verbose) {
        const e = e$;

        return "verbose:" + e.code;
      } else if (kind === "quiet") {
        return "quiet";
      } else if (e$ instanceof AppException) {
        return "app";
      } else {
        throw e$;
      }
    }
  }

  rethrown(kind: string): void {
    try {
      this.work(kind);
    } catch (e$) {
      if (e$ instanceof NotFoundException) {
        this._log.push("missing");
        throw e$;
      } else if (e$ instanceof AppException) {
        let e = e$;
        e = new AppException("replaced", e.code);
        this._log.push(e.message);
        throw e$;
      } else {
        throw e$;
      }
    } finally {
      this._log.push("done");
    }
  }

  nested(kind: string): void {
    try {
      this.work(kind);
    } catch (e$) {
      if (e$ instanceof AppException) {
        const e = e$;
        try {
          if (e.code === 404) {
            throw e$;
          }

          this.work("retry");
        } catch (e$) {
          if (e$ instanceof NotFoundException) {
            const inner = e$;
            this._log.push("inner:" + inner.code);
          } else {
            throw e$;
          }
        }
      } else {
        throw e$;
      }
    }
  }

  deferred(kinds: string[]): (() => number)[] {
    const codes = [] as Array<() => number>;
    for (const kind of kinds) {
      try {
        this.work(kind);
      } catch (e$) {
        if (e$ instanceof NotFoundException) {
          continue;
        } else if (e$ instanceof AppException && ((e) => e.code < 0)(e$)) {
          break;
        } else if (e$ instanceof AppException) {
          const e = e$;
          codes.push(() => e.code);
        } else {
          throw e$;
        }
      }
    }

    return codes;
  }

  async loadAsync(kind: string): Promise<string> {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof NotFoundException) {
        const e = e$;

        return await this.recoverAsync(e.code);
      } else if (e$ instanceof AppException) {
        await this.recoverAsync(0);
        throw e$;
      } else {
        throw e$;
      }
    }
  }

  bclLast(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof AppException) {
        return "app";
      } else if (e$ instanceof Error) {
        const e = e$;

        return "invalid:" + e.message;
      } else {
        throw e$;
      }
    }
  }

  exceptionThenGeneral(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      if (e$ instanceof NotFoundException) {
        return "missing";
      } else {
        return "ex";
      }
    }
  }

  typeParameterFirst<TFailure extends Error>(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      return "failure";
    }
  }

  logged(kind: string): void {
    try {
      this.work(kind);
    } catch (_err) {
      this._log.push("failed");
      throw _err;
    }
  }

  wrapped(kind: string): void {
    try {
      this.work(kind);
    } catch (e) {
      this._log.push(e instanceof AppException ? "app" : "other");
      throw e;
    }
  }

  private recoverAsync(code: number): Promise<string> {
    return Promise.resolve("recovered:" + code);
  }
}
