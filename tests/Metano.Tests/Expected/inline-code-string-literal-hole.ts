export class Greeter {
  single(name: string): string {
    return ('hello name') as string;
  }

  double(name: string): string {
    return ("hello name") as string;
  }

  template(name: string): string {
    return (`hello name`) as string;
  }

  substitution(name: string): string {
    return (`hello ${name}`) as string;
  }

  comment(name: string): string {
    return (/* 'x' */ name) as string;
  }
}
