/**
 * The `metano-runtime` functions the conformance rows call, by the name a row uses. Relative
 * imports of the built output (`bun run build` first), because `UUID` is a TypeScript
 * namespace, which Node's type stripping cannot load from source; the same registry then runs
 * under Bun and under Node. A function whose result is a `Temporal` or `UUID` value gets a
 * wrapper that reads it the way the row records it (ticks, UTC ticks and offset, text).
 */

import * as stringMembers from "../../dist/system/string-members.js";
import type { RowRegistry } from "./conformance-rows.ts";

export const stringRegistry: RowRegistry = { ...stringMembers };
