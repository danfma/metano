import type { JsonReadOptions, PropertySpec } from "./types.ts";
import { ordinalIgnoreCaseKeyOf } from "./ordinal-ignore-case.ts";

/** The JSON value a contract property reads, `undefined` when no key supplies it. */
export type PropertyLookup = (property: PropertySpec) => unknown;

/**
 * The lookup of one JSON object read by a contract, built from the work
 * shared by every object it reads (compiled once per contract).
 * `metadataKey` is the discriminator a polymorphic read consumed: it is
 * metadata and never fills a property (measured; an exact clash is a
 * contract error in System.Text.Json, `MS0032` here).
 */
export type PropertyLookupPlan = (
  raw: Record<string, unknown>,
  metadataKey: string | undefined,
) => PropertyLookup;

/**
 * How the properties of a contract find their JSON values: by their exact
 * JSON name, or, under `propertyNameCaseInsensitive`, ignoring case
 * (FR-048).
 */
export function propertyLookupPlanOf(
  properties: readonly PropertySpec[],
  readOptions: JsonReadOptions | undefined,
): PropertyLookupPlan {
  return readOptions?.propertyNameCaseInsensitive
    ? caseInsensitiveLookupPlan(properties)
    : exactLookup;
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
function caseInsensitiveLookupPlan(properties: readonly PropertySpec[]): PropertyLookupPlan {
  const propertiesByKey = new Map(
    properties.map((property) => [ordinalIgnoreCaseKeyOf(property.json), property]),
  );

  return (raw, metadataKey) => {
    const values = new Map<PropertySpec, unknown>();
    const dataEntries = Object.entries(raw).filter(([key]) => key !== metadataKey);

    for (const [key, value] of dataEntries) {
      const property = propertiesByKey.get(ordinalIgnoreCaseKeyOf(key));

      if (property) {
        values.set(property, value);
      }
    }

    return (property) => values.get(property);
  };
}
