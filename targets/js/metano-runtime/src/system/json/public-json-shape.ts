/**
 * The own-property name of a per-class backing field the transpiler emits for an
 * overridable auto-property: `<property>$<Class>_<hash>` (`p$Shape_k3x9ab`), where the
 * hash is six base-36 characters. `$` never appears in a C# identifier, so no
 * property of the source type can be mistaken for one.
 */
const BACKING_FIELD_NAME = /^([^$]+)\$[^$]+_[0-9a-z]{6}$/;

/**
 * The shape `JSON.stringify` produced for a transpiled instance while its overridable
 * auto-properties were plain fields: one key per property, in declaration order, base
 * classes first.
 *
 * Generated classes that keep such a property in a private backing field call this from
 * `toJSON()`, because `JSON.stringify` would otherwise serialize the mangled field
 * names. Every own property of the instance is visited; a backing field contributes its
 * property name, read through the accessor so the most derived override answers, and the
 * property is listed once however many classes of the chain declare a backing field for
 * it. Any other own property passes through unchanged.
 */
export function publicJsonShape(instance: object): Record<string, unknown> {
  const source = instance as Record<string, unknown>;
  const shape: Record<string, unknown> = {};

  for (const key of Object.keys(instance)) {
    const name = publicNameOf(key);

    if (!(name in shape)) {
      shape[name] = source[name];
    }
  }

  return shape;
}

function publicNameOf(ownKey: string): string {
  return BACKING_FIELD_NAME.exec(ownKey)?.[1] ?? ownKey;
}
