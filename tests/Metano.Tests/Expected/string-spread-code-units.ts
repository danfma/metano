import { HashSet } from "metano-runtime";

export class Letters {
  constructor() { }

  of(text: string): string[] {
    return [...text.split("")];
  }

  framed(text: string, suffix: string | null): string[] {
    return ["<", ...text.split(""), ...(suffix ?? "").split(""), ...(text + "!").split(""), ">"];
  }

  distinct(text: string): HashSet<string> {
    return new HashSet<string>([...text.split("")]);
  }

  words(words: string[]): string[] {
    return [...words];
  }

  dashed(words: string[]): string {
    return words.join("-");
  }
}
