using System.Text;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Memory;

public static class Transcript
{
    public static string Of(IEnumerable<Message> messages)
    {
        var text = new StringBuilder();
        foreach (var message in messages.OrderBy(m => m.Sequence))
        {
            text.AppendLine($"{message.SpeakerName}: {message.Content.Trim()}");
        }

        return text.ToString();
    }
}
