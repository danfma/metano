/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  Button as ButtonWidget,
  Column as ColumnWidget,
  Heading as HeadingWidget,
  Row as RowWidget,
  Text as TextWidget,
} from "#/mvu/widgets";
import type { Widget } from "./widget";

export function Column(args: { gap: number; children: Widget[] }): ColumnWidget {
  const { gap, children } = args;

  return new ColumnWidget(gap, children);
}

export function Row(args: { gap: number; children: Widget[] }): RowWidget {
  const { gap, children } = args;

  return new RowWidget(gap, children);
}

export function Text(args: { content: string }): TextWidget {
  const { content } = args;

  return new TextWidget(content);
}

export function Heading(args: { content: string; level?: number }): HeadingWidget {
  const { content, level = 1 } = args;

  return new HeadingWidget(content, level);
}

export function Button(args: { label: string; onPressed: () => void }): ButtonWidget {
  const { label, onPressed } = args;

  return new ButtonWidget(label, onPressed);
}
