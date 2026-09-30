using System.Text.Json;
using NevaBridge.Chat.Tests.Support;

namespace NevaBridge.Chat.Tests;

public sealed class ChatViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unexpected_adapter_failure_is_shown_without_exposing_private_details(bool submitting)
    {
        // Given
        FakeConversations nevaBridge = new();
        ChatViewModel chat = new(nevaBridge, "product-1", new NevaBridgeReporter("jdoe"));
        chat.Draft = "It crashes.";
        if (submitting)
        {
            chat.SendCommand.Execute(null);
        }
        nevaBridge.FailWith = new InvalidOperationException("Private adapter details");

        // When
        if (submitting)
        {
            chat.SubmitCommand.Execute(null);
        }
        else
        {
            chat.SendCommand.Execute(null);
        }

        // Then
        Assert.Equal("The application could not complete the chat request.", chat.ErrorText);
        Assert.False(chat.IsBusy);
        if (!submitting)
        {
            Assert.Equal("It crashes.", chat.Draft);
        }
    }

    [Fact]
    public void An_answer_from_NevaBridge_that_cannot_be_read_is_shown_in_the_chat()
    {
        // Given
        FakeConversations nevaBridge = new() { FailWith = new JsonException("Unexpected end of data.") };
        ChatViewModel chat = new(nevaBridge, "product-1", new NevaBridgeReporter("jdoe"));
        chat.Draft = "It crashes.";

        // When
        chat.SendCommand.Execute(null);

        // Then
        Assert.NotNull(chat.ErrorText);
        Assert.Equal("It crashes.", chat.Draft);
        Assert.False(chat.IsBusy);
    }
}
