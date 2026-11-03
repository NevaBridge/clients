import type {IntakeTurnDoneEvent} from "./generated/models/IntakeTurnDoneEvent.js";
import type {IntakeTurnFailure} from "./generated/models/IntakeTurnFailure.js";
import type {IntakeTurnModelFailure} from "./generated/models/IntakeTurnModelFailure.js";
import type {IntakeTurnStatusEvent} from "./generated/models/IntakeTurnStatusEvent.js";
import type {IntakeTurnTextEvent} from "./generated/models/IntakeTurnTextEvent.js";

/**
 * The generator merges the members of the contract's event `anyOf` into one
 * interface with every field required, so the public union is composed here
 * from the generated member types.
 */
export interface IntakeTurnErrorEvent {
  readonly type: "error";
  readonly error: IntakeTurnFailure | IntakeTurnModelFailure;
}

/**
 * An event of a type this SDK version does not know. The API adds event types
 * without a new version, and clients are asked to ignore the ones they do not
 * know. The wire type is moved to `eventType`, because a `type` of any string
 * would stop TypeScript from narrowing the union on `type`.
 */
export interface IntakeTurnUnknownEvent {
  readonly type: "unknown";
  readonly eventType: string;
  /** The event as the API sent it, `type` included. */
  readonly payload: Readonly<Record<string, unknown>>;
}

export type IntakeTurnEvent =
  | IntakeTurnStatusEvent
  | IntakeTurnTextEvent
  | IntakeTurnDoneEvent
  | IntakeTurnErrorEvent
  | IntakeTurnUnknownEvent;

export interface IntakeTurnEventPage {
  readonly events: IntakeTurnEvent[];
  readonly cursor: string;
  readonly finished: boolean;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function malformed(description: string): Error {
  return new Error(
    `NevaBridge returned a malformed Generic Intake turn ${description}.`,
  );
}

function turnFailure(
  value: unknown,
): IntakeTurnFailure | IntakeTurnModelFailure {
  if (
    !isRecord(value) ||
    typeof value.error !== "string" ||
    typeof value.message !== "string" ||
    typeof value.statusCode !== "number"
  ) {
    throw malformed("error event");
  }
  if (value.error !== "ModelInvocationFailed") {
    return {
      error: value.error,
      message: value.message,
      statusCode: value.statusCode,
    };
  }
  if (
    typeof value.category !== "string" ||
    typeof value.provider !== "string" ||
    typeof value.role !== "string" ||
    typeof value.catalogKey !== "string"
  ) {
    throw malformed("model failure");
  }
  return {
    error: value.error,
    message: value.message,
    category: value.category,
    provider: value.provider,
    role: value.role,
    catalogKey: value.catalogKey,
    statusCode: value.statusCode,
  };
}

function intakeTurnEvent(value: unknown): IntakeTurnEvent {
  if (!isRecord(value) || typeof value.type !== "string") {
    throw malformed("event");
  }
  switch (value.type) {
    case "status": {
      if (
        typeof value.phase !== "string" ||
        (value.tool !== undefined && typeof value.tool !== "string")
      ) {
        throw malformed("status event");
      }
      return value.tool === undefined
        ? {type: "status", phase: value.phase}
        : {type: "status", phase: value.phase, tool: value.tool};
    }
    case "text": {
      if (typeof value.delta !== "string") {
        throw malformed("text event");
      }
      return {type: "text", delta: value.delta};
    }
    case "done": {
      if (typeof value.revision !== "number") {
        throw malformed("done event");
      }
      return {type: "done", revision: value.revision};
    }
    case "error":
      return {type: "error", error: turnFailure(value.error)};
    default:
      return {type: "unknown", eventType: value.type, payload: value};
  }
}

export function intakeTurnEventPage(value: unknown): IntakeTurnEventPage {
  if (
    !isRecord(value) ||
    !Array.isArray(value.events) ||
    typeof value.cursor !== "string" ||
    typeof value.finished !== "boolean"
  ) {
    throw malformed("event page");
  }
  return {
    events: value.events.map(intakeTurnEvent),
    cursor: value.cursor,
    finished: value.finished,
  };
}
