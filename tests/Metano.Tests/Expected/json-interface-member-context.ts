import { List } from "metano-runtime/system/collections";
import { NotSupportedException, SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Hub } from "./hub.js";
import type { ISensor } from "./i-sensor.js";

export class JsonContext extends SerializerContext {
  private static readonly _default = /* @__PURE__ */ new JsonContext() as JsonContext;
  private _hub?: TypeSpec<Hub>;
  private _iSensor?: TypeSpec<ISensor>;

  static get default(): JsonContext {
    return this._default;
  }

  get hub(): TypeSpec<Hub> {
    return this._hub ??= this.createSpec({
      type: Hub,
      runtimeType: () => Hub.$runtimeType,
      factory: (p: Record<string, unknown>) => new Hub(p.title as string, p.sensor as ISensor | null, p.sensors as List<ISensor>),
      properties: [
        {
          ts: "title",
          json: "Title",
          type: { kind: "primitive" },
        },
        {
          ts: "sensor",
          json: "Sensor",
          type: {
            kind: "nullable",
            inner: {
              kind: "ref",
              spec: () => this.iSensor,
            },
          },
          optional: true,
        },
        {
          ts: "sensors",
          json: "Sensors",
          type: {
            kind: "list",
            element: {
              kind: "ref",
              spec: () => this.iSensor,
            },
            create: (items) => List.of(items),
          },
        },
      ],
    });
  }

  get iSensor(): TypeSpec<ISensor> {
    return this._iSensor ??= this.createSpec({
      factory: () => {
        throw new NotSupportedException("Deserialization of interface or abstract types is not supported. Type 'TestApp.ISensor'.");
      },
      properties: [
        {
          ts: "level",
          json: "Level",
          type: { kind: "primitive", numberType: "System.Int32" },
        },
      ],
    });
  }
}
