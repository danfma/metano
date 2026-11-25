import type { Holder } from "./holder.js";
import type { UserDto } from "./user-dto.js";

export class Hatches {
  dto(raw: Object): UserDto {
    return raw as unknown as UserDto;
  }

  name(raw: Object): string {
    return (raw as unknown as UserDto).name;
  }

  generic<T>(raw: Object): T {
    return raw as unknown as T;
  }

  chained(holder: Holder | null): string | null {
    return holder == null ? null : (holder.value as unknown as UserDto).name;
  }

  call(f: Object): number {
    return (f as unknown as (arg: number) => number)(2);
  }

  static(raw: Object): UserDto {
    return raw as unknown as UserDto;
  }

  dynamic(holder: Holder): Object {
    return (holder as any);
  }

  local(holder: Holder): Object {
    const any = (holder as any);

    return any;
  }
}
