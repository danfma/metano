/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { castTo, getRuntimeType, isType } from "metano-runtime";
import { AreaDto$runtimeType, type AreaDto } from "./area-dto";
import { Circle } from "./circle";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area";
import { ShapeFactory } from "./shape-factory";

export class ShapeQuestions {
  constructor() {}

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
}
