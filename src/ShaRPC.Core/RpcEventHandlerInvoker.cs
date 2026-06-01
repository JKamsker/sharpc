namespace ShaRPC.Core;

internal static class RpcEventHandlerInvoker
{
    public static void Raise<TEventArgs>(
        EventHandler<TEventArgs>? handler,
        object sender,
        TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TEventArgs>)subscriber).Invoke(sender, args);
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report(
                    $"Event handler '{subscriber.Method.DeclaringType?.FullName}.{subscriber.Method.Name}' failed",
                    ex);
            }
        }
    }
}
