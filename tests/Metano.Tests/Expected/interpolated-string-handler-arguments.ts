import { LogHandler } from "./log-handler.js";
import { Logger } from "./logger.js";

export class Sink {
  declare private readonly _logger: Logger;

  constructor() {
    this._logger = new Logger();
  }

  write(level: number, n: number): void {
    let handler$temp$0, handler$temp$1;
    this._logger.log(level, (handler$temp$0 = new LogHandler(2, 2, this._logger, level), handler$temp$0.appendLiteral("a") && handler$temp$0.put(n, 0, null) && handler$temp$0.appendLiteral("b") && handler$temp$0.put(n, 3, "X"), handler$temp$0));
    const logger = this._logger;
    logger.log(2, (handler$temp$1 = new LogHandler(1, 1, logger, 2), handler$temp$1.appendLiteral("x") && handler$temp$1.put(n, 0, null), handler$temp$1));
  }
}
