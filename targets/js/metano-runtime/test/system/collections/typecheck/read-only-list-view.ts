import {
  asJsArray,
  asList,
  type ImmutableArray,
  type ImmutableArrayBuilder,
  type ImmutableList,
  type ImmutableListBuilder,
  type IReadOnlyList,
  type List,
  type ReadOnlyList,
  type SortedListView,
} from "#/system/collections";
import { type Equal, expectType } from "../../../typecheck/type-assertions.ts";

expectType<Equal<IReadOnlyList<number>["length"], number>>();
expectType<Equal<ReturnType<IReadOnlyList<number>["toArray"]>, number[]>>();
expectType<Equal<ReturnType<IReadOnlyList<number>["asJsArray"]>, readonly number[]>>();

declare const list: List<number>;
declare const ro: IReadOnlyList<number>;
declare const readOnlyList: ReadOnlyList<number>;
declare const immutableList: ImmutableList<number>;
declare const immutableArray: ImmutableArray<number>;
declare const sortedView: SortedListView<number>;
declare const listBuilder: ImmutableListBuilder<number>;
declare const arrayBuilder: ImmutableArrayBuilder<number>;
declare const nativeReadOnly: readonly number[];
declare const raw: unknown;

const asInterfaceFromList: IReadOnlyList<number> = list;
const asInterfaceFromReadOnly: IReadOnlyList<number> = readOnlyList;
const asInterfaceFromImmutableList: IReadOnlyList<number> = immutableList;
const asInterfaceFromImmutableArray: IReadOnlyList<number> = immutableArray;
const asInterfaceFromSortedView: IReadOnlyList<number> = sortedView;
const asInterfaceFromListBuilder: IReadOnlyList<number> = listBuilder;
const asInterfaceFromArrayBuilder: IReadOnlyList<number> = arrayBuilder;

void asInterfaceFromList;
void asInterfaceFromReadOnly;
void asInterfaceFromImmutableList;
void asInterfaceFromImmutableArray;
void asInterfaceFromSortedView;
void asInterfaceFromListBuilder;
void asInterfaceFromArrayBuilder;

const mutableView = asJsArray(list);
const roViewFromHelper = asJsArray(ro);
const roViewFromNative = asJsArray(nativeReadOnly);

expectType<Equal<typeof mutableView, number[]>>();
expectType<Equal<typeof roViewFromHelper, readonly number[]>>();
expectType<Equal<typeof roViewFromNative, readonly number[]>>();

const castReadOnly =
  asList<number>(raw, "System.Collections.Generic.IReadOnlyList`1[System.Int32]") as IReadOnlyList<number>;

void castReadOnly;

// @ts-expect-error — consumer view from IReadOnlyList is read-only
ro.asJsArray().push(1);

// @ts-expect-error — free helper over IReadOnlyList returns readonly T[]
asJsArray(ro).push(1);

// @ts-expect-error — length mirrors count and is read-only
ro.length = 0;
