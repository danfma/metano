/**
 * The text .NET's `Type.ToString()` prints for a collection
 * (``System.Collections.Generic.List`1[System.Int32]``), used by every
 * collection's `toString()`. Internal.
 */
import type { RuntimeType } from "../reflection/runtime-type.ts";

/**
 * ``<name>[<arguments>]`` (`name` carries .NET's arity suffix, ``List`1``): each argument from `typeArguments` when the
 * instance carries them, else (and for `UnknownType`) the C# type-parameter name.
 */
export function collectionTypeName(
  name: string,
  parameterNames: readonly string[],
  typeArguments: readonly RuntimeType[] | undefined,
): string {
  const names = parameterNames.map((parameter, position) =>
    argumentName(typeArguments?.[position], parameter),
  );

  return `${name}[${names.join(",")}]`;
}

function argumentName(argument: RuntimeType | undefined, parameter: string): string {
  if (argument === undefined || argument.kind === "unknown") {
    return parameter;
  }

  if (argument.kind === "collection" && argument.typeArguments.length > 0) {
    const openName = argument.fullName.slice(0, argument.fullName.indexOf("<"));
    const names = argument.typeArguments.map((nested) => argumentName(nested, "T"));

    return `${openName}\`${names.length}[${names.join(",")}]`;
  }

  return argument.fullName;
}
