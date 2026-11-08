# How the Generic Intake chat keeps its state

`IntakeChatViewModel` (in `src/NevaBridge.Chat`) runs one Generic Intake conversation. Its turns run
in the background on NevaBridge and every answer can be lost on the network. This note lists the
rules the view model keeps, so a change, or a copy in your own application, can check itself
against all of them at once.

## Rules

### Turns

1. One message is on its way at a time. Send, Finish and Check again are unavailable while a turn
   is followed (`IsBusy`) or while a reply could not be loaded (`CanCheckAgain`). A message sent
   while a file is being picked or uploaded shows as sent and waits until that file is uploaded
   or failed, so the reply sees it. Finish is offered only after the first reply, because
   NevaBridge refuses to finish a conversation without a message (`NothingToFinish`).
2. Without files, the first message starts the conversation. A first turn that ends in `error`
   deletes that conversation on NevaBridge, so the chat forgets it and the next message starts a
   new one. When files came first (rule 10), the first message is a turn at revision 0 on the
   conversation they started; its failure keeps the conversation and its files, and the message
   goes back to the composer.
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
   the API, not prevented here. The same holds for a lost answer to a start with a file: the file
   is reported as not attached, and NevaBridge deletes the empty conversation after 24 hours.

### Attachments

10. Attachments are offered only with a file picker and never after the conversation ended.
    Before the first message, the first picked file starts the conversation without a message
    (`StartHeldIntakeConversationAsync`) and uploads at once to the address NevaBridge returned;
    later files take the ordinary upload. A refused first file creates nothing and says why. When
    NevaBridge stored the conversation but could not sign the upload (424 with a
    `conversationId`), the chat keeps that conversation and requests the upload again. While a
    first message without files is on its way, attaching waits: that conversation is deleted if
    its first turn fails, and files attached to it would go with it. A file that did not upload
    stays listed as not uploaded with Retry and Dismiss and never holds up a reply. Because
    NevaBridge attaches arrived bytes even unconfirmed, and a listing omits them while their
    address is live, the chat never forgets an upload whose outcome is open: the first file keeps
    its admission, and only deleting that admission on NevaBridge lets go of it. Dismiss deletes
    it; Retry deletes it before uploading a new copy, so at most one copy is ever attached; when
    deleting fails, the file stays as it was. A listing that shows the admission drops the row. A
    later file, uploaded through `AttachFileAsync`, gets Retry and Dismiss only when storage
    refused it outright; when the outcome is open (no answer, or 412 because storage already holds
    it) it is settled by listing (rule 12).
11. One attachment operation runs at a time: attach, remove and listing share one lock
    (`_isAttaching`). A conversation read that comes while an upload, removal or listing runs
    queues its listing until that operation has ended, and attach and remove are refused while a
    listing runs, so a list answer never overwrites a later change and no listing is lost.
12. NevaBridge holds the real list. After every conversation read, and after an attach or remove
    whose outcome is uncertain, the chat replaces its uploaded files with what NevaBridge lists;
    files still uploading or not uploaded stay, because NevaBridge does not hold them.
13. When listing fails, the list keeps what it showed, and until a listing succeeds the notice
    says the files could not be loaded, whatever else it says, with Load files again, which lists
    them without another message.
14. A 403 from any attachment call means attachments were switched off: the chat stops offering
    them and says so.

### Closing

15. Disposing the view model, when its window closes, cancels every request. Cancellation caused
    by closing ends any operation quietly; nothing is shown and no exception leaves a command.
    Voice stops too: the microphone closes, reading stops, and words heard but not sent are
    discarded with everything else.

### Voice

`IntakeVoiceViewModel` (`chat.Voice`) keeps these rules on top of the ones above. It exists only
when the host passes an `IntakeChatVoice` with dictation, the hands-free conversation, or both.

16. A spoken message is sent on the same path as a typed one, so rules 1 to 9 hold for it,
    including rule 9's exception. When it is not sent, its words go back into the composer on
    their own line after the text typed before voice mode, and voice mode ends.
17. While dictation runs or voice mode is on, Send, Finish and the language menu wait. Dictation
    appends settled words to the composer text it started from, separated by a space, and shows
    unsettled words apart; stopping keeps both. A change to the composer that dictation did not
    make, such as a typed message the chat put back, stops dictation at once, and so does the
    conversation closing.
18. In voice mode the microphone is open only while listening. 1.2 seconds after the last final
    phrase, once no phrase of any session is still being revised and no rotated-out session is
    still draining, or on Send now, what was heard is sent and the microphone closes until the reply has
    been read, so the reply is never heard as the reporter's speech.
19. The reply is read sentence by sentence as the live reply completes sentences. At most one
    synthesis runs ahead of the sentence being read, and the next one starts only after the
    current one's audio arrived. Each synthesis counts as soon as it completes, so a refusal for
    the next sentence takes effect while the current one plays. After `done` the saved reply adds what the live text had not
    delivered; if it does not continue what was already read, nothing more is read.
20. Stop reading drops the rest of that reply, the saved addition included. Voice mode listens
    again once the turn is done, as after a reply read to its end. A closed conversation ends
    voice mode after its reply was read.
21. End voice mode and Escape put words heard but not sent into the composer, as in rule 16.
22. A 403 from any voice call switches voice off for this chat: no voice call follows. A 402 ends
    dictation or voice mode with NevaBridge's message, and voice stays offered. A missing or denied
    microphone says so, and voice stays offered. Recognition that stops keeps what was heard and
    offers to start again. A sentence that cannot be synthesized or played for any other reason is
    read past, and voice mode listens again once the turn is done.
23. Each listening session requests a paid recognition session and moves to a new one 3 seconds
    before the 60 paid seconds run out. While the previous session drains, both report; results
    carry their session, and the heard text, and the composer when dictation stops, keep the
    sessions' order. Listening stops while waiting and reading, so no session
    is requested then. The session's presigned address never appears in a notice, an exception
    message or `ToString()`.

## Why attachments are one serialized unit

The first version guarded each attachment case separately: a catch for closing here, a refresh
after a failure there. Each guard fixed its case and opened the next one: a refresh could fail
silently, overlap an upload, or ignore a 403. Removing listing altogether would make the chat
show a wrong list whenever an answer is lost, and the API supports listing for exactly that
reason, so listing stays. Instead, every attachment operation goes through the same lock and the
same failure handling (rules 11 to 15), so a new case needs no new guard: it is covered by the
rule for its kind of failure.

The turn rules (1 to 9) mirror the NevaBridge web chat page, which recovers from the same lost
answers the same way. The voice rules (16 to 23) follow the NevaBridge web app's voice, so the two
behave alike.
