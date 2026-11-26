import { HashSet, List } from "metano-runtime/system/collections";

export class Letters {
  of(text: string): List<string> {
    return List.of<string>([...text.split("")]);
  }

  framed(text: string, suffix: string | null): string[] {
    return ["<", ...text.split(""), ...(suffix ?? "").split(""), ...((text ?? "") + "!").split(""), ">"];
  }

  distinct(text: string): HashSet<string> {
    return HashSet.of<string>([...text.split("")]);
  }

  words(words: string[]): List<string> {
    return List.of<string>([...words]);
  }

  dashed(words: string[]): string {
    return words.join("-");
  }
}
