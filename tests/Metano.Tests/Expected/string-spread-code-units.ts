import { HashSet, List } from "metano-runtime/system/collections";

export class Letters {
  constructor() { }

  of(text: string): List<string> {
    return List.of([...text.split("")]);
  }

  framed(text: string, suffix: string | null): string[] {
    return ["<", ...text.split(""), ...(suffix ?? "").split(""), ...((text ?? "") + "!").split(""), ">"];
  }

  distinct(text: string): HashSet<string> {
    return HashSet.of([...text.split("")]);
  }

  words(words: string[]): List<string> {
    return List.of([...words]);
  }

  dashed(words: string[]): string {
    return words.join("-");
  }
}
