import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { ScriptWord } from "./script-word.js";
import { Script } from "./script.js";

export class JsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new JsonContext() as JsonContext;
  private _script?: TypeSpec<Script>;
  private _scriptWord?: TypeSpec<ScriptWord>;

  static get default(): JsonContext {
    return this._default;
  }

  get script(): TypeSpec<Script> {
    return this._script ??= this.createSpec({
      type: Script,
      runtimeType: () => Script.$runtimeType,
      factory: (p: Record<string, unknown>) => new Script(p.words as ScriptWord[]),
      properties: [
        {
          ts: "words",
          json: "words",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.scriptWord,
            },
          },
        },
      ],
    });
  }

  get scriptWord(): TypeSpec<ScriptWord> {
    return this._scriptWord ??= this.createSpec({
      type: ScriptWord,
      runtimeType: () => ScriptWord.$runtimeType,
      factory: (p: Record<string, unknown>) => new ScriptWord(p.text as string, p.start as number),
      properties: [
        {
          ts: "text",
          json: "text",
          type: { kind: "primitive" },
        },
        {
          ts: "start",
          json: "start",
          type: { kind: "primitive", numberType: "System.Double" },
        },
      ],
    });
  }
}
