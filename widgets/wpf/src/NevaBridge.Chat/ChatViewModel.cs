using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NevaBridge.Chat;

public sealed record ChatMessageItem(string Text, bool IsFromReporter);

public sealed record ReportField(string Name, string Value);

public sealed record ReportTypeOption(string Category, string Label);

/// <summary>
/// Drives one NevaBridge reporting conversation for <see cref="NevaBridgeChatControl"/>: the
/// first message starts it, later messages continue it, and Submit finalizes the report.
/// </summary>
public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly INevaBridgeConversations _conversations;
    private readonly string _productId;
    private readonly NevaBridgeReporter _reporter;
    private readonly IReadOnlyDictionary<string, string>? _applicationContext;

    private string? _conversationId;
    private Report? _report;
    private string _draft = string.Empty;
    private bool _isBusy;
    private string? _busyText;
    private string? _errorText;
    private string? _title;
    private string? _outcomeText;
    private string _category = ReportCategories.SupportRequest;

    public ChatViewModel(
        INevaBridgeConversations conversations,
        string productId,
        NevaBridgeReporter reporter,
        IReadOnlyDictionary<string, string>? applicationContext = null)
    {
        _conversations = conversations;
        _productId = productId;
        _reporter = reporter;
        _applicationContext = applicationContext;
        SendCommand = new AsyncCommand(SendAsync, () => !IsBusy && !IsFinished && !string.IsNullOrWhiteSpace(Draft));
        SubmitCommand = new AsyncCommand(SubmitAsync, () => !IsBusy && _report is { IsInProgress: true });
        NewConversationCommand = new AsyncCommand(StartOverAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public ObservableCollection<ReportField> ReportFields { get; } = [];

    public IReadOnlyList<ReportTypeOption> ReportTypes { get; } =
    [
        new(ReportCategories.BugReport, "Bug report"),
        new(ReportCategories.FeatureRequest, "Feature request"),
        new(ReportCategories.SupportRequest, "Support request"),
    ];

    /// <summary>
    /// The report type the conversation starts as. It selects the report template, so it can only
    /// change before the first message.
    /// </summary>
    public string Category
    {
        get => _category;
        set
        {
            if (CanChooseReportType)
            {
                Set(ref _category, value);
            }
            else
            {
                // Tell the view to snap back to the type the conversation started with.
                OnPropertyChanged(nameof(Category));
            }
        }
    }

    public bool CanChooseReportType => _conversationId is null && !IsBusy;

    public AsyncCommand SendCommand { get; }

    public AsyncCommand SubmitCommand { get; }

    public AsyncCommand NewConversationCommand { get; }

    public string Draft
    {
        get => _draft;
        set
        {
            if (Set(ref _draft, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                SubmitCommand.RaiseCanExecuteChanged();
                NewConversationCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanChooseReportType));
            }
        }
    }

    public string? BusyText
    {
        get => _busyText;
        private set => Set(ref _busyText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    public string Title
    {
        get => _title ?? "Support";
        private set => Set(ref _title, value);
    }

    public string? ReportStatus => _report is null ? null : DescribeStatus(_report);

    public bool HasReport => _report is not null;

    /// <summary>The report left the reporter's hands; this conversation takes no more messages.</summary>
    public bool IsFinished => _report is { IsInProgress: false };

    public string? OutcomeText
    {
        get => _outcomeText;
        private set => Set(ref _outcomeText, value);
    }

    private async Task SendAsync()
    {
        string text = Draft.Trim();
        Draft = string.Empty;
        ErrorText = null;
        Messages.Add(new ChatMessageItem(text, IsFromReporter: true));
        IsBusy = true;
        BusyText = "The assistant is replying...";
        try
        {
            ConversationTurn turn = _conversationId is null
                ? await _conversations.StartConversationAsync(
                    _productId,
                    _reporter,
                    new StartConversationRequest(new UserMessage(text))
                    {
                        Category = Category,
                        ApplicationContext = _applicationContext,
                    })
                : await _conversations.AppendMessageAsync(_conversationId, new AppendMessageRequest(text));

            _conversationId = turn.AssistantMessage.ConversationId;
            OnPropertyChanged(nameof(CanChooseReportType));
            Messages.Add(new ChatMessageItem(turn.AssistantMessage.Content, IsFromReporter: false));
            if (turn.DisplayTitle is not null)
            {
                Title = turn.DisplayTitle;
            }

            ShowReport(turn.Reports.FirstOrDefault(r => r.IsInProgress) ?? turn.Reports.FirstOrDefault());
            if (turn.Connectors is not null)
            {
                OutcomeText = "The assistant judged the report complete and sent it. " + DescribeDelivery(turn.Connectors);
            }
        }
        catch (Exception error)
        {
            // Give the text back so the reporter can send it again.
            Messages.RemoveAt(Messages.Count - 1);
            Draft = text;
            ErrorText = DescribeError(error);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    private async Task SubmitAsync()
    {
        if (_conversationId is null || _report is null)
        {
            return;
        }

        ErrorText = null;
        IsBusy = true;
        BusyText = "Submitting the report...";
        try
        {
            SubmitReportResult result = await _conversations.SubmitReportAsync(_conversationId, _report.Id);
            ShowReport(result.Report);
            OutcomeText = "Thank you, the report was submitted. " + DescribeDelivery(result.Connectors);
        }
        catch (Exception error)
        {
            ErrorText = DescribeError(error);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    private Task StartOverAsync()
    {
        _conversationId = null;
        _title = null;
        _category = ReportCategories.SupportRequest;
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(CanChooseReportType));
        Messages.Clear();
        Draft = string.Empty;
        ErrorText = null;
        OutcomeText = null;
        ShowReport(null);
        OnPropertyChanged(nameof(Title));
        return Task.CompletedTask;
    }

    private void ShowReport(Report? report)
    {
        _report = report;
        ReportFields.Clear();
        if (report is not null)
        {
            foreach ((string name, string value) in report.Structured)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    ReportFields.Add(new ReportField(Humanize(name), value));
                }
            }
        }

        OnPropertyChanged(nameof(ReportStatus));
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(IsFinished));
        SendCommand.RaiseCanExecuteChanged();
        SubmitCommand.RaiseCanExecuteChanged();
    }

    private static string DescribeStatus(Report report) => report.Status switch
    {
        "reporting_in_progress" => "Draft, still collecting details",
        "submitted" => "Submitted",
        "assigned" => "Assigned to an engineer",
        "engineering_in_progress" => "Being worked on",
        "pending_release" => "Fixed, awaiting release",
        "closed" => "Closed",
        string other => other,
    };

    private static string DescribeDelivery(IReadOnlyDictionary<string, ConnectorDeliveryResult>? connectors)
    {
        if (connectors is null || connectors.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> parts = connectors.Select(c => c.Value.Status == "success"
            ? $"{c.Key}: {c.Value.Ticket}"
            : $"{c.Key}: not delivered ({c.Value.Error})");
        return "Delivered to " + string.Join("; ", parts) + ".";
    }

    private static string DescribeError(Exception error) => error switch
    {
        NevaBridgeApiException { StatusCode: 402 } =>
            "NevaBridge has no credits left for this organization. Please contact your administrator.",
        NevaBridgeApiException { StatusCode: 401 or 403 } api =>
            $"NevaBridge refused the request ({api.Message}). Check the stored API key and product.",
        NevaBridgeApiException { IsRetryable: true } =>
            "NevaBridge is busy or took too long to answer. Please try again in a moment.",
        NevaBridgeApiException api => $"NevaBridge could not process the request: {api.Message}",
        TaskCanceledException => "The request to NevaBridge timed out. Please try again.",
        JsonException => "NevaBridge sent an answer this version of the chat cannot read.",
        HttpRequestException => "NevaBridge could not be reached. Check the network connection and try again.",
        _ => "The application could not complete the chat request.",
    };

    private static string Humanize(string key)
    {
        string spaced = string.Concat(key.Select((c, i) =>
            c == '_' ? " " : i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1]) ? " " + c : c.ToString()));
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
