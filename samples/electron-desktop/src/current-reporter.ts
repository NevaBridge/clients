import {userInfo} from "node:os";
import type {Reporter} from "@nevabridge/electron";

/**
 * Step 2 in your application: tell NevaBridge who is reporting. Map your own signed-in user here.
 *
 * The id becomes the report's reporterId, so keep it stable. Roles (anonymous, customer, tenant)
 * select which knowledge-base documents the assistant may use; this example sends none.
 */
export function currentReporter(): Reporter {
  const {username} = userInfo();
  return {id: username, displayName: username};
}
