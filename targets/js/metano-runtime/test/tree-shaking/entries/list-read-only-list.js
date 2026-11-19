// `List` plus `ReadOnlyList` (`list.AsReadOnly()`), for the marginal cost of
// `ReadOnlyList` over `List` alone (spec 011 NFR-008, stage 011h, W4-42).
import { List } from "./system/collections/list.js";
import { asReadOnly } from "./system/collections/read-only-list.js";

export function build(items) {
  return asReadOnly(List.from(items));
}
