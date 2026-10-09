using System;
using System.Collections.Generic;

namespace Vortex.Primitives.Chat;

/// <summary>
/// What the chat rules remember of one sender (spec F3c §7), held by its presence and lost at
/// disconnect. Each rule keeps its own part, by type.
/// </summary>
public sealed class ChatSenderState
{
    private readonly Dictionary<Type, object> _parts = [];

    public T Get<T>()
        where T : class, new()
    {
        if (!_parts.TryGetValue(typeof(T), out object? part))
        {
            part = new T();
            _parts[typeof(T)] = part;
        }

        return (T)part;
    }
}
