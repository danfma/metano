/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { castTo, getRuntimeType, isType, type RuntimeType } from "metano-runtime";
import { AreaDto$runtimeType, type AreaDto } from "./area-dto.js";
import { Circle } from "./circle.js";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area.js";
import { ShapeFactory } from "./shape-factory.js";

export class ShapeQuestions {
  private constructor() {}

  static nameOf(kind: string): string {
    return getRuntimeType(ShapeFactory.create(kind)).name;
  }

  static isCircle(kind: string): boolean {
    return getRuntimeType(ShapeFactory.create(kind)) === Circle.$runtimeType;
  }

  static hasArea(value: Object): boolean {
    return isType(value, IHasArea$runtimeType);
  }

  static areaOf(value: Object): number {
    let area: IHasArea;

    return isType(value, IHasArea$runtimeType) && ((area = value), true) ? area.area : -1;
  }

  static toAreaDto(value: Object): AreaDto {
    return castTo<AreaDto>(value, AreaDto$runtimeType);
  }

  private static is<T>(value: Object, T$type: RuntimeType<T>): boolean {
    return isType(value, T$type);
  }

  static isCircleViaGeneric(value: Object): boolean {
    return ShapeQuestions.is<Circle>(value, Circle.$runtimeType);
  }
}
