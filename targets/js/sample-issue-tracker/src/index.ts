export { JsonContext } from "./json-context";
import * as $Issues_Application from "./issues/application";
import * as $Issues_Domain from "./issues/domain";
import * as $Planning_Domain from "./planning/domain";
import * as $SharedKernel from "./shared-kernel";

export namespace Issues {
  export import Application = $Issues_Application;

  export import Domain = $Issues_Domain;
}

export namespace Planning {
  export import Domain = $Planning_Domain;
}

export import SharedKernel = $SharedKernel;
