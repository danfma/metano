import type { JsonReadOptions, PropertySpec } from "./types.ts";
import { ordinalIgnoreCaseKeyOf } from "./ordinal-ignore-case.ts";

/** The JSON value a contract property reads, `undefined` when no key supplies it. */
export type PropertyLookup = (property: PropertySpec) => unknown;

/**
 * How the properties of a contract find their JSON values in `raw`: by
 * their exact JSON name, or, under `propertyNameCaseInsensitive`, ignoring
 * case (FR-048). `metadataKey` is the discriminator a polymorphic read
 * consumed: it is metadata and never fills a property (measured; an exact
 * clash is a contract error in System.Text.Json, `MS0032` here).
 */
export function propertyLookupOf(
  raw: Record<string, unknown>,
  properties: readonly PropertySpec[],
  readOptions: JsonReadOptions | undefined,
  metadataKey: string | undefined,
): PropertyLookup {
  return readOptions?.propertyNameCaseInsensitive
    ? caseInsensitiveLookup(raw, properties, metadataKey)
    : exactLookup(raw, metadataKey);
}

function exactLookup(
  raw: Record<string, unknown>,
  metadataKey: string | undefined,
): PropertyLookup {
  return (property) => (property.json === metadataKey ? undefined : raw[property.json]);
}

/**
 * Every key that matches a property like `OrdinalIgnoreCase`, applied in
 * document order, so that the last match wins (measured).
 */
function caseInsensitiveLookup(
  raw: Record<string, unknown>,
  properties: readonly PropertySpec[],
  metadataKey: string | undefined,
): PropertyLookup {
  const propertiesByKey = new Map(
    properties.map((property) => [ordinalIgnoreCaseKeyOf(property.json), property]),
  );

  const values = new Map<PropertySpec, unknown>();
  const dataEntries = Object.entries(raw).filter(([key]) => key !== metadataKey);

  for (const [key, value] of dataEntries) {
    const property = propertiesByKey.get(ordinalIgnoreCaseKeyOf(key));

    if (property) {
      values.set(property, value);
    }
  }

  return (property) => values.get(property);
}
