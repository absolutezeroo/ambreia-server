using System.Reflection;

namespace Vortex.Tests.Support;

/// <summary>A single intercepted call: the method that was invoked and the arguments it got.</summary>
public sealed record ProxyCall(MethodInfo Method, object?[]? Args);
