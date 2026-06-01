import { Counter } from "./counter";
import { Route } from "solid-router";

export type AppRouterProps = {};

export function AppRouter(props: AppRouterProps) {
  return <Route path="/"><Counter count={1} /></Route>;
}
