/**
 * Array-backed provider that materializes a `linq()` chain by reading
 * each operator's `queryable` metadata (the IR expression tree) instead
 * of opening the original closure. Each predicate / selector lambda is
 * compiled into a JS function via the runtime's
 * {@link compileLambdaBody} helper, then applied over the in-memory
 * source array.
 *
 * Triggered via `runArrayProvider(source, stages)` where `stages` is
 * the descriptor list the compiler emitted. Stages without queryable
 * meta fall back to the runtime closure (`apply`), so a mixed chain
 * still works — only stages we can introspect get the provider
 * treatment.
 *
 * Scope: predicate evaluation for `where` / `select` over the MVP
 * tree subset. Real providers extend the same {@link ExprTreeVisitor}
 * surface (or {@link evaluateExprTree}) to translate the tree into
 * SQL, GraphQL, or HTTP query strings.
 */
import { type AnyOperator, compileLambdaBody, type SelectOp, type WhereOp } from "metano-runtime";

/**
 * Result of running the provider over an array source.
 *
 * `materialized` carries the rows after applying every stage that
 * exposed queryable meta (so they bypass their closures). `fellBack`
 * tracks every stage that lacked meta — those still ran but via their
 * native `apply`, so the provider can warn the caller about coverage
 * gaps.
 */
export interface ProviderResult<T> {
  materialized: T[];
  fellBack: AnyOperator["kind"][];
}

/** Outcome of materialising one stage — the new rows + whether the
 * provider had to fall back to the runtime closure (no queryable meta
 * available). Returned as data rather than mutated through a side
 * channel so each handler stays a pure function. */
interface StageOutcome {
  items: unknown[];
  fellBack: boolean;
}

export function runArrayProvider<T>(
  source: Iterable<T>,
  stages: AnyOperator[],
): ProviderResult<unknown> {
  let current: unknown[] = Array.from(source);
  const fellBack: AnyOperator["kind"][] = [];

  for (const stage of stages) {
    const outcome = applyStage(stage, current);
    current = outcome.items;
    if (outcome.fellBack) fellBack.push(stage.kind);
  }

  return { materialized: current, fellBack };
}

function applyStage(stage: AnyOperator, source: unknown[]): StageOutcome {
  switch (stage.kind) {
    case "where":
      return handleWhere(stage as WhereOp<unknown>, source);
    case "select":
      return handleSelect(stage as SelectOp<unknown, unknown>, source);
    default:
      // Stages without a queryable meta path: drive via the runtime
      // closure (`apply`) and record the fallback.
      return { items: Array.from(stage.apply(source)), fellBack: true };
  }
}

function handleWhere(op: WhereOp<unknown>, source: unknown[]): StageOutcome {
  const meta = op.queryable;
  if (!meta) {
    return {
      items: source.filter((item, index) => op.predicate(item, index)),
      fellBack: true,
    };
  }
  const predicate = compileLambdaBody(meta);
  return { items: source.filter((item) => Boolean(predicate(item))), fellBack: false };
}

function handleSelect(op: SelectOp<unknown, unknown>, source: unknown[]): StageOutcome {
  const meta = op.queryable;
  if (!meta) {
    return {
      items: source.map((item, index) => op.selector(item, index)),
      fellBack: true,
    };
  }
  const selector = compileLambdaBody(meta);
  return { items: source.map((item) => selector(item)), fellBack: false };
}
