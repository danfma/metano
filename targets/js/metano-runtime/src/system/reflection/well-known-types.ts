import type {
  ArrayType as ArrayTypeShape,
  DelegateType as DelegateTypeShape,
  ObjectType as ObjectTypeShape,
  UnknownType as UnknownTypeShape,
} from "./runtime-type.ts";

/** The wildcard descriptor: every value passes. */
export const UnknownType: UnknownTypeShape = {
  kind: "unknown",
  name: "Unknown",
  fullName: "Unknown",
  namespace: "",
  test: (_value: unknown): _value is unknown => true,
};
export type UnknownType = UnknownTypeShape;

/** `System.Object`: any value but `null` and `undefined`. */
export const ObjectType: ObjectTypeShape = {
  kind: "object",
  name: "Object",
  fullName: "System.Object",
  namespace: "System",
  test: (value: unknown): value is object => value != null,
};
export type ObjectType = ObjectTypeShape;

/** `System.Array`: a native array, its element type erased. */
export const ArrayType: ArrayTypeShape = {
  kind: "array",
  name: "Array",
  fullName: "System.Array",
  namespace: "System",
  test: (value: unknown): value is unknown[] => Array.isArray(value),
};
export type ArrayType = ArrayTypeShape;

/** `System.Delegate`: any function. */
export const DelegateType: DelegateTypeShape = {
  kind: "delegate",
  name: "Delegate",
  fullName: "System.Delegate",
  namespace: "System",
  test: (value: unknown): value is (...args: never[]) => unknown => typeof value === "function",
};
export type DelegateType = DelegateTypeShape;
