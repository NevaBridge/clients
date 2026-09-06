import type {
  ConversationDetail,
  ConversationStatusValue,
  ReportStatusValue,
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
