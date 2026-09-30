import type {
  ConversationDetail,
  ConversationStatusValue,
  EdgeError,
  ModelInvocationError,
  ReportStatusValue,
  StartConversationOptions,
} from "@nevabridge/sdk";

const futureConversationStatus: ConversationStatusValue = "future_status";
const futureReportStatus: ReportStatusValue = "future_report_status";

const detailStatuses: Readonly<{
  conversation: ConversationDetail["status"];
  report: ConversationDetail["reports"][number]["status"];
}> = {
  conversation: futureConversationStatus,
  report: futureReportStatus,
};

void detailStatuses;

// A role the contract gains after this SDK is published must still be sendable.
// The API ignores unknown roles rather than rejecting them, so a closed union
// here would make an additive contract change source-incompatible.
const futureAudience: StartConversationOptions = {
  productId: "product-00000000-0000-4000-8000-000000000000",
  actorId: "dana@example.com",
  actorRoles: ["anonymous", "future_audience"],
  request: {userMessage: {content: "The checkout page is blank."}},
};

void futureAudience;

// A provider or pipeline step the service adds later must not break a consumer
// that reads model failure details.
const futureModelFailure: Pick<ModelInvocationError, "provider" | "role"> = {
  provider: "future_provider",
  role: "future_role",
};

void futureModelFailure;

// 502 and 504 bodies come from the API gateway, not from NevaBridge. Consumers
// need the type to tell them apart from NevaBridge errors, which carry `error`.
const gatewayTimeout: EdgeError = {message: "Endpoint request timed out"};

void gatewayTimeout;
