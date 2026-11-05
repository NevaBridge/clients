# How the Generic Intake chat keeps its state

`IntakeChatViewModel` (in `src/NevaBridge.Chat`) runs one Generic Intake conversation. Its turns run
in the background on NevaBridge and every answer can be lost on the network. This note lists the
rules the view model keeps, so a change, or a copy in your own application, can check itself
against all of them at once.

## Rules

### Turns

1. One message is on its way at a time. Send, Finish and Check again are unavailable while a turn
   is followed (`IsBusy`) or while a reply could not be loaded (`CanCheckAgain`).
2. The first message starts the conversation. A first turn that ends in `error` deletes its
   conversation on NevaBridge, so the chat forgets it and the next message starts a new one.
3. A later message sends the revision the chat last read. A refusal because another turn runs
   follows that turn; a refusal because the revision is stale reads the conversation.
4. A message counts as saved only when the conversation holds more reporter messages than before
   it was sent and the newest has its text. Otherwise it goes back to the composer, ahead of
   anything typed since, so nothing the reporter wrote is lost and nothing is sent twice.
5. A poll that fails with an edge or network failure is repeated with the same cursor, three
   times, a second apart. After that the chat says the reply could not be loaded and offers
   Check again.
6. Check again for a later turn reads the conversation and follows its running turn. For a first
   turn, whose conversation may be gone, it reads on from the saved cursor, with the reply text
   written so far, so a failure still returns the message with its reason.
7. After `done` the saved conversation replaces the live reply; the live text is only a preview.
8. A closed conversation takes no more messages and lists every report it filed.
9. When the answer to the very first start is lost, the chat cannot find that conversation; the
   message returns to the composer and sending again starts another one. This is a limitation of
   the API, not prevented here.

### Attachments

10. Attachments are offered only with a file picker, only once the first turn is saved (files of a
    deleted first conversation would vanish with it), and never after the conversation ended.
11. One attachment operation runs at a time: attach, remove and listing share one lock
    (`_isAttaching`). A conversation read that comes while an upload, removal or listing runs
    queues its listing until that operation has ended, and attach and remove are refused while a
    listing runs, so a list answer never overwrites a later change and no listing is lost.
12. NevaBridge holds the real list. After every conversation read, and after an attach or remove
    whose outcome is uncertain, the chat replaces its list with what NevaBridge lists.
13. When listing fails, the list keeps what it showed, and until a listing succeeds the notice
    says the files could not be loaded, whatever else it says, with Load files again, which lists
    them without another message.
14. A 403 from any attachment call means attachments were switched off: the chat stops offering
    them and says so.

### Closing

15. Disposing the view model, when its window closes, cancels every request. Cancellation caused
    by closing ends any operation quietly; nothing is shown and no exception leaves a command.

## Why attachments are one serialized unit

The first version guarded each attachment case separately: a catch for closing here, a refresh
after a failure there. Each guard fixed its case and opened the next one: a refresh could fail
silently, overlap an upload, or ignore a 403. Removing listing altogether would make the chat
show a wrong list whenever an answer is lost, and the API supports listing for exactly that
reason, so listing stays. Instead, every attachment operation goes through the same lock and the
same failure handling (rules 11 to 15), so a new case needs no new guard: it is covered by the
rule for its kind of failure.

The turn rules (1 to 9) mirror the NevaBridge web chat page, which recovers from the same lost
answers the same way.
