using System;
using System.Collections.Generic;
using System.Linq;
using Engie.Mca.EventHandler.Models;

namespace Engie.Mca.EventHandler.Services;

public class MessageStore
{
    private readonly Dictionary<string, MessageContext> _messages = new();
    private readonly object _lock = new();

    public void Save(MessageContext context)
    {
        lock (_lock)
        {
            _messages[context.MessageId] = context;
        }
    }

    public MessageContext? Get(string messageId)
    {
        lock (_lock)
        {
            return _messages.TryGetValue(messageId, out var msg) ? msg : null;
        }
    }

    public List<MessageContext> GetByStatus(string status)
    {
        lock (_lock)
        {
            return _messages.Values
                .Where(m => m.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public List<MessageContext> GetAll()
    {
        lock (_lock)
        {
            return new List<MessageContext>(_messages.Values);
        }
    }
}
