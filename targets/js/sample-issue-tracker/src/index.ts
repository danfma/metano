/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import * as $SampleIssueTracker from "./sample-issue-tracker";
import * as $SampleIssueTracker_Issues_Application from "./sample-issue-tracker/issues/application";
import * as $SampleIssueTracker_Issues_Domain from "./sample-issue-tracker/issues/domain";
import * as $SampleIssueTracker_Planning_Domain from "./sample-issue-tracker/planning/domain";
import * as $SampleIssueTracker_SharedKernel from "./sample-issue-tracker/shared-kernel";

export namespace SampleIssueTracker {
  export import _ = $SampleIssueTracker;

  export namespace Issues {
    export import Application = $SampleIssueTracker_Issues_Application;

    export import Domain = $SampleIssueTracker_Issues_Domain;
  }

  export namespace Planning {
    export import Domain = $SampleIssueTracker_Planning_Domain;
  }

  export import SharedKernel = $SampleIssueTracker_SharedKernel;
}
