using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>What the inbox screen does: open a message, delete the lot. (Messages are posted where the events happen: see <see cref="InboxService"/>.)</summary>
public sealed partial class GameEngine
{
    /// <summary>Opens a message: it is marked as read. Returns null if there is no such message.</summary>
    public InboxMessage? OpenMessage(string id)
    {
        var existing = State.Inbox.FirstOrDefault(m => m.Id == id);
        if (existing is null) return null;
        bool wasUnread = !existing.Read;
        InboxService.MarkRead(State, id);
        if (wasUnread) StateChanged?.Invoke();
        return existing;
    }

    /// <summary>
    /// Deletes every message in the inbox and returns how many were deleted. Only the inbox is cleared: nothing that happened
    /// (a battle, a treaty, a shortage) is undone, and a shortage already reported is not reported again.
    /// </summary>
    public int DeleteAllMessages()
    {
        int count = InboxService.DeleteAll(State);
        StateChanged?.Invoke();
        return count;
    }
}
