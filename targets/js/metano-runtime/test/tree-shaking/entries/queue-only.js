// `Queue` alone (spec 011 NFR-008).
import { Queue } from "./system/collections/queue.js";

export const create = () => new Queue([1, 2]);
