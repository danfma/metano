import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { EpisodeSummary } from "./episode-summary";
import { Feed } from "./feed";
import { Page } from "./page";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _feed?: TypeSpec<Feed>;

  private _pageEpisodeSummary?: TypeSpec<Page<EpisodeSummary>>;

  private _episodeSummary?: TypeSpec<EpisodeSummary>;

  static get default(): JsonContext {
    return this._default;
  }

  get feed(): TypeSpec<Feed> {
    return this._feed ??= this.createSpec({
      type: Feed,
      factory: (p: Record<string, unknown>) => new Feed(p.latest as Page<EpisodeSummary>),
      properties: [
        {
          ts: "latest",
          json: "Latest",
          type: {
            kind: "ref",
            spec: () => this.pageEpisodeSummary,
          },
        },
      ],
    });
  }

  get pageEpisodeSummary(): TypeSpec<Page<EpisodeSummary>> {
    return this._pageEpisodeSummary ??= this.createSpec<Page<EpisodeSummary>>({
      type: Page,
      factory: (p: Record<string, unknown>) => new Page(p.items as EpisodeSummary[], p.total as number),
      properties: [
        {
          ts: "items",
          json: "Items",
          type: {
            kind: "array",
            element: {
              kind: "ref",
              spec: () => this.episodeSummary,
            },
          },
        },
        {
          ts: "total",
          json: "Total",
          type: { kind: "primitive" },
        },
      ],
    });
  }

  get episodeSummary(): TypeSpec<EpisodeSummary> {
    return this._episodeSummary ??= this.createSpec({
      type: EpisodeSummary,
      factory: (p: Record<string, unknown>) => new EpisodeSummary(p.title as string, p.number as number),
      properties: [
        {
          ts: "title",
          json: "Title",
          type: { kind: "primitive" },
        },
        {
          ts: "number",
          json: "Number",
          type: { kind: "primitive" },
        },
      ],
    });
  }
}
