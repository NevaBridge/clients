import {
  intakeTurnRunningId,
  NevaBridgeApiError,
  NevaBridgeClient,
} from "../src/index.js";

const token = process.env.NEVABRIDGE_API_KEY;
if (!token) {
  throw new Error("Set NEVABRIDGE_API_KEY before running this example.");
}

const client = new NevaBridgeClient({
  baseUrl: process.env.NEVABRIDGE_BASE_URL,
  tokenProvider: () => token,
});

async function followTurn(turnId: string): Promise<number> {
  let preview = "";
  const {revision} = await client.waitForIntakeTurn({
    turnId,
    onEvent: (event) => {
      if (event.type === "status") {
        console.log(`[${event.tool ?? event.phase}]`);
      } else if (event.type === "text") {
        preview += event.delta;
      }
    },
  });
  console.log(`Preview: ${preview}`);
  return revision;
}

try {
  const features = await client.getFeatures();
  if (!features.genericIntake) {
    throw new Error("Ask NevaBridge to enable Generic Intake for this tenant.");
  }

  const {conversationId, turnId} = await client.startIntakeConversation({
    productId: "product-2f6a1c58-51b7-4a0e-9d0e-63a8b0e5c111",
    actorId: "sample-user-4821",
    actorName: "Sample User",
    request: {
      userMessage: {content: "The checkout page is blank after I click Pay."},
    },
  });
  let revision = await followTurn(turnId);

  const conversation = await client.getConversation({conversationId});
  console.log(conversation.messages.at(-1)?.content);

  try {
    const finishing = await client.startIntakeTurn({
      conversationId,
      request: {
        content: "That is everything, thank you.",
        finish: true,
        expectedRevision: revision,
      },
    });
    revision = await followTurn(finishing.turnId);
  } catch (error: unknown) {
    const runningTurnId = intakeTurnRunningId(error);
    if (runningTurnId === undefined) {
      throw error;
    }
    revision = await followTurn(runningTurnId);
  }

  const closed = await client.getConversation({conversationId});
  console.log(closed.status, closed.reports.length, revision);
} catch (error: unknown) {
  if (error instanceof NevaBridgeApiError) {
    console.error(error.status, error.detail);
  }
  throw error;
}
