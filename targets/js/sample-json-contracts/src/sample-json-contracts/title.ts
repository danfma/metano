/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export type Title = string & { readonly __brand: "Title" };

export namespace Title {
  export function create(value: string): Title {
    return value as Title;
  }

  export function from(value: string): Title {
    const trimmed = value.trim();

    if (trimmed.length === 0) {
      throw new Error("blank");
    }

    return Title.create(trimmed);
  }
}
