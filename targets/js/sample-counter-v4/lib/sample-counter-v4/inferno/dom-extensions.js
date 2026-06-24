/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export function getOrCreateElementById(document, id) {
    return document.getElementById(id) ?? document.createElement("div");
}
