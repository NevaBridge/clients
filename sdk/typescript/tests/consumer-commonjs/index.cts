import {ConversationStatus, NevaBridgeClient} from "@nevabridge/sdk";

const client = new NevaBridgeClient({tokenProvider: () => "token"});

void client;
void ConversationStatus.InProgress;
