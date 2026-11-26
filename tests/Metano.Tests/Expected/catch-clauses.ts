import { List, throwModified } from "metano-runtime/system/collections";
import { toException, tryFilter, type ClassType } from "metano-runtime";
import { Exception } from "metano-runtime/system/exceptions/exception";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

let AppException$rt: ClassType<AppException> | undefined;

export class AppException extends Exception {
  override readonly name: string = "AppException";
  declare readonly code: number;

  constructor(message: string, code: number) {
    super(message ?? "Exception of type 'AppException' was thrown.");
    this.code = code;
    this.code = code;
  }

  static override get $runtimeType(): ClassType<AppException> {
    return (AppException$rt ??= {
      kind: "class",
      name: "AppException",
      fullName: "AppException",
      namespace: "",
      ctor: AppException,
      base: () => Exception.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is AppException => v instanceof AppException,
    });
  }

  protected override get $typeName(): string {
    return "AppException";
  }

  protected override get $typeNamespace(): string {
    return "";
  }
}

let NotFoundException$rt: ClassType<NotFoundException> | undefined;

export class NotFoundException extends AppException {
  override readonly name: string = "NotFoundException";

  constructor(message: string) {
    super(message, 404);
  }

  static override get $runtimeType(): ClassType<NotFoundException> {
    return (NotFoundException$rt ??= {
      kind: "class",
      name: "NotFoundException",
      fullName: "NotFoundException",
      namespace: "",
      ctor: NotFoundException,
      base: () => AppException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is NotFoundException => v instanceof NotFoundException,
    });
  }

  protected override get $typeName(): string {
    return "NotFoundException";
  }

  protected override get $typeNamespace(): string {
    return "";
  }
}

export class Handlers {
  declare private readonly _log: List<string>;
  declare verbose: boolean;

  constructor() {
    this._log = new List<string>();
    this.verbose = false;
  }

  work(kind: string): void {
    if (kind === "missing") {
      throw new NotFoundException(kind);
    }

    if (kind === "app") {
      throw new AppException(kind, 1);
    }

    if (kind === "invalid") {
      throw new InvalidOperationException(kind);
    }
  }

  typed(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      const e0 = toException(e$);
      if (e0 instanceof NotFoundException) {
        return "missing";
      } else if (e0 instanceof AppException) {
        const e = e0;

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
      const e0 = toException(e$);
      if (e0 instanceof NotFoundException) {
        const e = e0;

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
      const e0 = toException(e$);
      if (e0 instanceof AppException) {
        const e = e0;

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
      const e0 = toException(e$);
      if (e0 instanceof NotFoundException) {
        return "missing";
      } else {
        const e = e0;

        return e instanceof AppException ? "app" : "any";
      }
    }
  }

  filtered(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      const e0 = toException(e$);
      if (e0 instanceof AppException && tryFilter((e) => e.code === 404, e0)) {
        return "missing";
      } else if (e0 instanceof AppException && tryFilter(() => this.verbose)) {
        const e = e0;

        return "verbose:" + e.code;
      } else if (tryFilter(() => kind === "quiet")) {
        return "quiet";
      } else if (e0 instanceof AppException) {
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
      const e0 = toException(e$);
      if (e0 instanceof NotFoundException) {
        this._log.add("missing");
        throw e$;
      } else if (e0 instanceof AppException) {
        let e = e0;
        e = new AppException("replaced", e.code);
        this._log.add(e.message);
        throw e$;
      } else {
        throw e$;
      }
    } finally {
      this._log.add("done");
    }
  }

  nested(kind: string): void {
    try {
      this.work(kind);
    } catch (e$) {
      const e0 = toException(e$);
      if (e0 instanceof AppException) {
        const e = e0;
        try {
          if (e.code === 404) {
            throw e$;
          }

          this.work("retry");
        } catch (e$) {
          const e0 = toException(e$);
          if (e0 instanceof NotFoundException) {
            const inner = e0;
            this._log.add("inner:" + inner.code);
          } else {
            throw e$;
          }
        }
      } else {
        throw e$;
      }
    }
  }

  deferred(kinds: List<string>): List<() => number> {
    const codes = new List<() => number>();
    {
      const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
      const $l = kinds;
      const $a = $l._items;
      const $n = $a.length;
      const $v = $c ? $l._version : 0;
      for (let $i = 0; ; $i++) {
        if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
        if ($i === $n) break;
        const kind = $a[$i]!;
        try {
          this.work(kind);
        } catch (e$) {
          const e0 = toException(e$);
          if (e0 instanceof NotFoundException) {
            continue;
          } else if (e0 instanceof AppException && tryFilter((e) => e.code < 0, e0)) {
            break;
          } else if (e0 instanceof AppException) {
            const e = e0;
            codes.add(() => e.code);
          } else {
            throw e$;
          }
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
      const e0 = toException(e$);
      if (e0 instanceof NotFoundException) {
        const e = e0;

        return await this.recoverAsync(e.code);
      } else if (e0 instanceof AppException) {
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
      const e0 = toException(e$);
      if (e0 instanceof AppException) {
        return "app";
      } else if (e0 instanceof InvalidOperationException) {
        const e = e0;

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
      const e0 = toException(e$);
      if (e0 instanceof NotFoundException) {
        return "missing";
      } else {
        return "ex";
      }
    }
  }

  typeParameterFirst<TFailure extends Exception>(kind: string): string {
    try {
      this.work(kind);

      return "ok";
    } catch (e$) {
      const e0 = toException(e$);

      return "failure";
    }
  }

  logged(kind: string): void {
    try {
      this.work(kind);
    } catch (_err) {
      this._log.add("failed");
      throw _err;
    }
  }

  wrapped(kind: string): void {
    try {
      this.work(kind);
    } catch (e$) {
      const e = toException(e$);
      this._log.add(e instanceof AppException ? "app" : "other");
      throw e$;
    }
  }

  private recoverAsync(code: number): Promise<string> {
    return Promise.resolve("recovered:" + code);
  }
}
