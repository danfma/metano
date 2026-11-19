import { NullReferenceException } from "../exceptions/null-reference-exception.ts";
import { hasPlainPrototype, ownRuntimeTypeOf } from "./constructor-descriptor.ts";
import { isBoxed } from "./is-boxed.ts";
import { getNumberType } from "./get-number-type.ts";
import {
  BigIntegerType,
  BooleanType,
  DecimalType,
  Int64Type,
  StringType,
  UInt64Type,
} from "./primitive-types.ts";
import type { ClassType, RuntimeType } from "./runtime-type.ts";
import { toStringTag } from "./to-string-tag.ts";
import {
  DateOnlyType,
  DateTimeOffsetType,
  DateTimeType,
  TimeOnlyType,
  TimeSpanType,
} from "./temporal-types.ts";
import { ArrayType, DelegateType, ObjectType } from "./well-known-types.ts";
import { isDecimal, isInt64, isUInt64 } from "../../type-checking/index.ts";

type Constructor = abstract new (...args: never[]) => unknown;

let anonymousTypes: WeakMap<Constructor, ClassType<unknown>> | undefined;

/** C# `value.GetType()`: the runtime descriptor of a value; `null` and `undefined` throw. */
export function getRuntimeType(value: unknown): RuntimeType {
  if (value === null || value === undefined) {
    throw new NullReferenceException();
  }

  if (isBoxed(value)) {
    return value.type;
  }

  switch (typeof value) {
    case "number":
      return getNumberType(value);
    case "bigint":
      return getBigIntType(value);
    case "string":
      return StringType;
    case "boolean":
      return BooleanType;
    case "function":
      return DelegateType;
    default:
      return getObjectType(value);
  }
}

function getBigIntType(value: bigint): RuntimeType {
  if (isInt64(value)) {
    return Int64Type;
  }

  return isUInt64(value) ? UInt64Type : BigIntegerType;
}

function getObjectType(value: object): RuntimeType {
  return (
    getDecimalType(value) ??
    getTaggedType(toStringTag(value)) ??
    (Array.isArray(value) ? ArrayType : undefined) ??
    ownRuntimeTypeOf(value) ??
    (hasPlainPrototype(value) ? ObjectType : getAnonymousType(value))
  );
}

function getDecimalType(value: object): RuntimeType | undefined {
  return isDecimal(value) ? DecimalType : undefined;
}

/** Temporal values, recognized by their `Symbol.toStringTag`. */
function getTaggedType(tag: string): RuntimeType | undefined {
  switch (tag) {
    case "[object Temporal.PlainDateTime]":
      return DateTimeType;
    case "[object Temporal.ZonedDateTime]":
      return DateTimeOffsetType;
    case "[object Temporal.PlainDate]":
      return DateOnlyType;
    case "[object Temporal.PlainTime]":
      return TimeOnlyType;
    case "[object Temporal.Duration]":
      return TimeSpanType;
    default:
      return undefined;
  }
}

/** A class the compiler never described: a descriptor named by its constructor, one per constructor. */
function getAnonymousType(value: object): RuntimeType {
  const found = (value as { readonly constructor?: unknown }).constructor;

  if (typeof found !== "function") {
    return ObjectType;
  }

  anonymousTypes ??= new WeakMap();

  const ctor = found as Constructor;
  const known = anonymousTypes.get(ctor);

  if (known !== undefined) {
    return known;
  }

  const created: ClassType<unknown> = {
    kind: "class",
    name: ctor.name,
    fullName: ctor.name,
    namespace: "",
    ctor,
    interfaces: [],
    typeParameters: [],
    isAbstract: false,
    isValueType: false,
    test: (candidate: unknown): candidate is unknown => candidate instanceof ctor,
  };

  anonymousTypes.set(ctor, created);

  return created;
}
