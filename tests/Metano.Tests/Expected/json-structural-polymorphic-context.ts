import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { isAnimal, type Animal } from "./animal";
import { Circle } from "./circle";
import { isDog, type Dog } from "./dog";
import { Holder } from "./holder";
import type { IShape } from "./i-shape";
import { MC } from "./mc";
import { Mixed } from "./mixed";
import { isMP, type MP } from "./mp";
import { isPoint, type Point } from "./point";
import { isPoint3, type Point3 } from "./point3";
import { isRect, type Rect } from "./rect";
import { isSAnimal, type SAnimal } from "./s-animal";
import { isSDog, type SDog } from "./s-dog";
import type { Shape } from "./shape";
import { Sq } from "./sq";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _holder?: TypeSpec<Holder>;

  private _shape?: TypeSpec<Shape>;

  private _animal?: TypeSpec<Animal>;

  private _sAnimal?: TypeSpec<SAnimal>;

  private _mixed?: TypeSpec<Mixed>;

  private _iShape?: TypeSpec<IShape>;

  private _point?: TypeSpec<Point>;

  private _point3?: TypeSpec<Point3>;

  private _rect?: TypeSpec<Rect>;

  private _dog?: TypeSpec<Dog>;

  private _sDog?: TypeSpec<SDog>;

  private _mP?: TypeSpec<MP>;

  private _mC?: TypeSpec<MC>;

  private _sq?: TypeSpec<Sq>;

  private _circle?: TypeSpec<Circle>;

  static get default(): JsonContext {
    return this._default;
  }

  get holder(): TypeSpec<Holder> {
    return this._holder ??= this.createSpec({
      type: Holder,
      factory: (p: Record<string, unknown>) => new Holder(p.main as IShape, p.all as IShape[]),
      properties: [
        {
          ts: "main",
          json: "Main",
          type: {
            kind: "ref",
            spec: () => this.iShape,
          },
        },
        {
          ts: "all",
          json: "All",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.iShape,
            },
          },
        },
      ],
    });
  }

  get shape(): TypeSpec<Shape> {
    return this._shape ??= this.createSpec({
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'Shape' directly; deserialize it through a polymorphic spec.");
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        typeName: "Shape",
        derived: [
          {
            tag: "point",
            spec: () => this.point,
            test: isPoint,
          },
          {
            tag: "point3",
            spec: () => this.point3,
            test: isPoint3,
          },
          {
            tag: "rect",
            spec: () => this.rect,
            test: isRect,
          },
        ],
        testOrder: [1, 0, 2],
      },
    });
  }

  get animal(): TypeSpec<Animal> {
    return this._animal ??= this.createSpec({
      factory: (p: Record<string, unknown>) => ({ name: p.name as string }),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        typeName: "Animal",
        derived: [
          {
            tag: "dog",
            spec: () => this.dog,
            test: isDog,
          },
        ],
        baseTest: isAnimal,
      },
    });
  }

  get sAnimal(): TypeSpec<SAnimal> {
    return this._sAnimal ??= this.createSpec({
      factory: (p: Record<string, unknown>) => ({ name: p.name as string }),
      properties: [
        {
          ts: "name",
          json: "Name",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        typeName: "SAnimal",
        derived: [
          {
            tag: "animal",
            spec: () => this.sAnimal,
            test: isSAnimal,
          },
          {
            tag: "dog",
            spec: () => this.sDog,
            test: isSDog,
          },
        ],
        testOrder: [1, 0],
      },
    });
  }

  get mixed(): TypeSpec<Mixed> {
    return this._mixed ??= this.createSpec({
      type: Mixed,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'Mixed' directly; deserialize it through a polymorphic spec.");
      },
      properties: [],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        derived: [
          {
            tag: "p",
            spec: () => this.mP,
            test: isMP,
          },
          {
            tag: "c",
            spec: () => this.mC,
          },
        ],
      },
    });
  }

  get iShape(): TypeSpec<IShape> {
    return this._iShape ??= this.createSpec({
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize interface type 'IShape' directly; deserialize it through a polymorphic spec.");
      },
      properties: [
        {
          ts: "area",
          json: "Area",
          type: { kind: "primitive" },
        },
      ],
      polymorphism: {
        discriminator: "$type",
        abstract: true,
        typeName: "IShape",
        derived: [
          {
            tag: "sq",
            spec: () => this.sq,
          },
          {
            tag: "circle",
            spec: () => this.circle,
          },
        ],
      },
    });
  }

  get point(): TypeSpec<Point> {
    return this._point ??= this.createSpec({
      base: this.shape,
      factory: (p: Record<string, unknown>) => ({ x: p.x as number }),
      properties: [
        {
          ts: "x",
          json: "X",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get point3(): TypeSpec<Point3> {
    return this._point3 ??= this.createSpec({
      base: this.point,
      factory: (p: Record<string, unknown>) => ({ x: p.x as number, y: p.y as number, z: p.z as number }),
      properties: [
        {
          ts: "y",
          json: "Y",
          type: { kind: "primitive" },
        },
        {
          ts: "z",
          json: "Z",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get rect(): TypeSpec<Rect> {
    return this._rect ??= this.createSpec({
      base: this.shape,
      factory: (p: Record<string, unknown>) => ({ w: p.w as number, h: p.h as number }),
      properties: [
        {
          ts: "w",
          json: "W",
          type: { kind: "primitive" },
        },
        {
          ts: "h",
          json: "H",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get dog(): TypeSpec<Dog> {
    return this._dog ??= this.createSpec({
      base: this.animal,
      factory: (p: Record<string, unknown>) => ({ name: p.name as string, bark: p.bark as number }),
      properties: [
        {
          ts: "bark",
          json: "Bark",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get sDog(): TypeSpec<SDog> {
    return this._sDog ??= this.createSpec({
      base: this.sAnimal,
      factory: (p: Record<string, unknown>) => ({ name: p.name as string, bark: p.bark as number }),
      properties: [
        {
          ts: "bark",
          json: "Bark",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get mP(): TypeSpec<MP> {
    return this._mP ??= this.createSpec({
      base: this.mixed,
      factory: (p: Record<string, unknown>) => ({ n: p.n as number }),
      properties: [
        {
          ts: "n",
          json: "N",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get mC(): TypeSpec<MC> {
    return this._mC ??= this.createSpec({
      type: MC,
      base: this.mixed,
      factory: (p: Record<string, unknown>) => new MC(p.s as string),
      properties: [
        {
          ts: "s",
          json: "S",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get sq(): TypeSpec<Sq> {
    return this._sq ??= this.createSpec({
      type: Sq,
      factory: (p: Record<string, unknown>) => new Sq(p.side as number),
      properties: [
        {
          ts: "side",
          json: "Side",
          type: { kind: "primitive" },
        },
        {
          ts: "area",
          json: "Area",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get circle(): TypeSpec<Circle> {
    return this._circle ??= this.createSpec({
      type: Circle,
      factory: (p: Record<string, unknown>) => new Circle(p.radius as number),
      properties: [
        {
          ts: "radius",
          json: "Radius",
          type: { kind: "primitive" },
        },
        {
          ts: "area",
          json: "Area",
          type: { kind: "primitive" },
        },
      ],
    });
  }
}
