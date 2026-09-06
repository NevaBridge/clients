import {NevaBridgeApiError, NevaBridgeClient} from "../src/index.js";

const token = process.env.NEVABRIDGE_API_KEY;
if (!token) {
  throw new Error("Set NEVABRIDGE_API_KEY before running this example.");
}

const client = new NevaBridgeClient({
  baseUrl: process.env.NEVABRIDGE_BASE_URL,
  tokenProvider: () => token,
});

try {
  const turn = await client.startConversation({
    productId: "product-2f6a1c58-51b7-4a0e-9d0e-63a8b0e5c111",
    actorId: "sample-user-4821",
    actorName: "Sample User",
    actorRoles: ["anonymous"],
    request: {
      userMessage: {content: "The checkout page is blank after I click Pay."},
      applicationContext: {environment: "sandbox"},
    },
  });

  for (const currentReport of turn.reports) {
    if (currentReport.status === "reporting_in_progress") {
      await client.submitReport({
        conversationId: currentReport.conversationId,
        reportId: currentReport.id,
      });
    }
  }
} catch (error: unknown) {
  if (error instanceof NevaBridgeApiError) {
    console.error(error.status, error.detail);
  }
  throw error;
}
