import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { Episode } from "./episode";
import { Plain } from "./plain";
import { Slug } from "./slug";
import { Title } from "./title";
import { Video } from "./video";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _video?: TypeSpec<Video>;

  private _title?: ValueSpec<Title>;

  private _listSlug?: ValueSpec<Slug[] | null>;

  private _code?: ValueSpec<Episode.Code>;

  static get default(): JsonContext {
    return this._default;
  }

  get video(): TypeSpec<Video> {
    return this._video ??= this.createSpec({
      type: Video,
      factory: (p: Record<string, unknown>) => new Video(p.title as Title, p.slug as Slug, p.note as Plain, p.subtitle as Title | null, p.aliases as Title[], p.codes as Map<Title, Episode.Code>),
      properties: [
        {
          ts: "title",
          json: "title",
          type: { kind: "branded", create: Title.from, typeName: "TestApp.Title" },
        },
        {
          ts: "slug",
          json: "slug",
          type: { kind: "branded", create: Slug.parse, typeName: "TestApp.Slug" },
        },
        {
          ts: "note",
          json: "note",
          type: { kind: "branded", create: Plain.create, typeName: "TestApp.Plain" },
        },
        {
          ts: "subtitle",
          json: "subtitle",
          type: {
            kind: "nullable",
            inner: { kind: "branded", create: Title.from, typeName: "TestApp.Title" },
          },
          optional: true,
        },
        {
          ts: "aliases",
          json: "aliases",
          type: {
            kind: "array",
            element: { kind: "branded", create: Title.from, typeName: "TestApp.Title" },
          },
        },
        {
          ts: "codes",
          json: "codes",
          type: {
            kind: "map",
            key: { kind: "branded", create: Title.from, typeName: "TestApp.Title" },
            value: { kind: "branded", create: Episode.Code.from, typeName: "TestApp.Episode+Code" },
          },
        },
      ],
    });
  }

  get title(): ValueSpec<Title> {
    return this._title ??= this.createValueSpec({
      descriptor: { kind: "branded", create: Title.from, typeName: "TestApp.Title" },
      valueType: true,
      typeName: "TestApp.Title",
    });
  }

  get listSlug(): ValueSpec<Slug[] | null> {
    return this._listSlug ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "branded", create: Slug.parse, typeName: "TestApp.Slug" },
      },
    });
  }

  get code(): ValueSpec<Episode.Code> {
    return this._code ??= this.createValueSpec({
      descriptor: { kind: "branded", create: Episode.Code.from, typeName: "TestApp.Episode+Code" },
      valueType: true,
      typeName: "TestApp.Episode+Code",
    });
  }
}
