/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export function getOrCreateElementById(document: Document, id: string): HTMLElement {
  return document.getElementById(id) ?? document.createElement("div");
}
