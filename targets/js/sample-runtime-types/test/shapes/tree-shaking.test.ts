import { describe, expect, test } from "bun:test";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const source = resolve(import.meta.dir, "../../src/sample-runtime-types");

/** The minified module code a bundle of `entry` keeps, with `metano-runtime` external. */
async function bundle(entry: string): Promise<string> {
  const directory = mkdtempSync(join(tmpdir(), "sample-runtime-types-"));

  try {
    const file = join(directory, "entry.ts");
    writeFileSync(file, entry);

    const result = await Bun.build({
      entrypoints: [file],
      external: ["metano-runtime"],
      minify: { whitespace: true, syntax: true },
    });

    expect(result.success).toBe(true);

    return (await Promise.all(result.outputs.map((output) => output.text()))).join("\n");
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

describe("tree-shaking (D29)", () => {
  test("a program that never asks about Blob bundles none of it", async () => {
    const code = await bundle(
      `import { ShapeQuestions } from "${source}/shapes/shape-questions";\n` +
        `console.log(ShapeQuestions.nameOf("circle"));\n`,
    );

    expect(code).toContain("Circle");
    expect(code).not.toContain("Blob");
  });

  test("the strict union base bundled without its guard module keeps no variant guard", async () => {
    const code = await bundle(
      `import { Token } from "${source}/unions/token";\n` + `console.log(Token.name);\n`,
    );

    expect(code).not.toContain("isNumberToken");
    expect(code).not.toContain("isTextToken");
  });
});
