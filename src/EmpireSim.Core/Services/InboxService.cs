using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The player's inbox: messages about what happened, filed in one of three categories by the type of the event that
/// produced them (<see cref="InboxTopic"/>). Messages are posted where the events really happen; nothing is invented to fill the
/// inbox. Deleting messages only clears the record: the battle, treaty or economic change behind a message stays as it is.
/// </summary>
public static class InboxService
{
    /// <summary>The inbox keeps this many messages; past it the oldest read messages go first, then the oldest of all.</summary>
    public const int MaxMessages = 500;

    /// <summary>Posts a message to the inbox. <paramref name="title"/> defaults to the topic's title (with the country's name when there is one).</summary>
    public static InboxMessage Notify(GameState state, InboxTopic topic, string summary, string? countryId = null,
        string? title = null, string? details = null)
    {
        var message = new InboxMessage
        {
            Date = state.CurrentDate,
            Topic = topic,
            Title = TitleFor(state, topic, countryId, title),
            Summary = summary,
            Details = details ?? summary,
            CountryId = countryId,
        };
        state.Inbox.Add(message);
        Trim(state);
        return message;
    }

    private static string TitleFor(GameState state, InboxTopic topic, string? countryId, string? title)
    {
        if (title is not null) return title;
        string baseTitle = InboxCatalog.TopicTitle(topic);
        string? country = countryId is null ? null : state.AllNations().FirstOrDefault(n => n.Id == countryId)?.Name;
        return country is null ? baseTitle : $"{baseTitle} — {country}";
    }

    /// <summary>
    /// Reports a standing condition (a shortage that goes on day after day) when it begins or changes, not every day:
    /// <paramref name="signature"/> describes it, and the same signature is not reported twice. If the last message about this
    /// topic is still unread, it is brought up to date instead of a second message being added, so the player never
    /// finds a pile of near-identical reports. Returns whether the inbox changed. <see cref="ClearCondition"/> ends the
    /// condition, so if it comes back it is reported again.
    /// </summary>
    public static bool NotifyCondition(GameState state, InboxTopic topic, string conditionId, string signature, string summary,
        string? countryId = null, string? title = null, string? details = null)
    {
        if (state.InboxConditions.TryGetValue(conditionId, out var current) && current == signature) return false;
        state.InboxConditions[conditionId] = signature;

        var last = state.Inbox.LastOrDefault(m => m.Topic == topic);
        if (last is { Read: false })
        {
            last.Date = state.CurrentDate;
            last.Title = TitleFor(state, topic, countryId, title);
            last.Summary = summary;
            last.Details = details ?? summary;
            last.CountryId = countryId;
            return true;
        }

        Notify(state, topic, summary, countryId, title, details);
        return true;
    }

    /// <summary>The standing condition is over.</summary>
    public static void ClearCondition(GameState state, string conditionId) => state.InboxConditions.Remove(conditionId);

    /// <summary>The messages of one category, newest first.</summary>
    public static List<InboxMessage> Messages(GameState state, InboxCategory category) =>
        state.Inbox
            .Where(m => m.Category == category)
            .Reverse()   // messages are appended in time order, so reversing keeps same-day messages newest-first
            .OrderByDescending(m => m.Date)
            .ToList();

    /// <summary>How many messages are unread, in one category or in all.</summary>
    public static int UnreadCount(GameState state, InboxCategory? category = null) =>
        state.Inbox.Count(m => !m.Read && (category is null || m.Category == category));

    /// <summary>Marks a message as read. Returns the message, or null if there is none with that id.</summary>
    public static InboxMessage? MarkRead(GameState state, string id)
    {
        var message = state.Inbox.FirstOrDefault(m => m.Id == id);
        if (message is not null) message.Read = true;
        return message;
    }

    /// <summary>
    /// Deletes every message and returns how many there were. Only the inbox is cleared: the events behind the messages are
    /// untouched, and conditions already reported (a shortage that goes on) are not reported again.
    /// </summary>
    public static int DeleteAll(GameState state)
    {
        int count = state.Inbox.Count;
        state.Inbox.Clear();
        return count;
    }

    private static void Trim(GameState state)
    {
        int excess = state.Inbox.Count - MaxMessages;
        for (int i = 0; i < state.Inbox.Count && excess > 0; )
        {
            if (!state.Inbox[i].Read) { i++; continue; }
            state.Inbox.RemoveAt(i);
            excess--;
        }
        if (excess > 0) state.Inbox.RemoveRange(0, excess);
    }
}
