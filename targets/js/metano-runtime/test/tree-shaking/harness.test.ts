import { expect, test } from "bun:test";
import { readdirSync } from "node:fs";

import {
  BUNDLERS,
  bundle,
  bundledModules,
  createBundleDirectory,
} from "./harness.ts";

test.each(BUNDLERS)(
  "concurrent entries remain independent and are removed (%s)",
  async (bundler) => {
    const directory = createBundleDirectory();
    const originalFiles = readdirSync(directory.root).sort();

    try {
      const [first, second] = await Promise.all([
        bundle(bundler, directory, 'console.log("first-entry");'),
        bundle(bundler, directory, 'console.log("second-entry");'),
      ]);

      expect(first).toContain("first-entry");
      expect(first).not.toContain("second-entry");
      expect(second).toContain("second-entry");
      expect(second).not.toContain("first-entry");
      expect(readdirSync(directory.root).sort()).toEqual(originalFiles);
    } finally {
      directory.dispose();
    }
  },
  60_000,
);

test.each(BUNDLERS)(
  "failed entries are removed (%s)",
  async (bundler) => {
    const directory = createBundleDirectory();
    const originalFiles = readdirSync(directory.root).sort();

    try {
      await expect(bundle(bundler, directory, 'import "./missing-entry.js";')).rejects.toThrow();
      expect(readdirSync(directory.root).sort()).toEqual(originalFiles);
    } finally {
      directory.dispose();
    }
  },
  60_000,
);

test("module discovery and bundling can share a directory without leaking entries", async () => {
  const directory = createBundleDirectory();
  const originalFiles = readdirSync(directory.root).sort();

  try {
    const [modules, code] = await Promise.all([
      bundledModules(directory, 'import "./system/uuid.js";'),
      bundle("esbuild", directory, 'console.log("parallel-entry");'),
    ]);

    expect(modules).toContain("system/uuid.js");
    expect(modules.some((module) => module.startsWith("__entry_"))).toBe(false);
    expect(code).toContain("parallel-entry");
    expect(readdirSync(directory.root).sort()).toEqual(originalFiles);
  } finally {
    directory.dispose();
  }
}, 60_000);
