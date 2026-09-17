// Audit probe: compiles the repository's actual WalletPurchaseExtensions.cs with minimal doubles.
// No database, network, or production state. Expectations describe defects at the audited SHA.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;

internal static class AuditProbe
{
    public static async Task Main()
    {
        var requests = new List<WalletDebitRequest> { new() };
        var operation = new CommerceOperationId(Guid.NewGuid());
        var wallet = new FakeWallet();
        int goods = 0;
        try
        {
            await wallet.ExecutePurchaseAsync<int>(
                requests,
                operation,
                _ =>
                {
                    goods++; // models the completed durable inventory call; not a real SQL commit
                    throw new InvalidOperationException("injected journal failure after delivery");
                },
                NullLogger.Instance,
                CancellationToken.None
            );
        }
        catch (InvalidOperationException error)
            when (error.Message.StartsWith("injected ", StringComparison.Ordinal))
        {
            Console.WriteLine("Expected injected failure: " + error.Message);
        }
        Check(
            wallet.Balance == 100 && goods == 1 && wallet.Refunds == 1,
            "POST-GRANT FAILURE: goods=1, balance=100, refunds=1 (free delivery reproduced in executor)"
        );

        wallet = new FakeWallet { FailAfterDebit = true };
        goods = 0;
        try
        {
            await wallet.ExecutePurchaseAsync(
                requests,
                operation,
                _ =>
                {
                    goods++;
                    return Task.FromResult(1);
                },
                NullLogger.Instance,
                CancellationToken.None
            );
        }
        catch (InvalidOperationException error)
            when (error.Message.StartsWith("injected ", StringComparison.Ordinal))
        {
            Console.WriteLine("Expected injected failure: " + error.Message);
        }
        Check(
            wallet.Balance == 90 && goods == 0 && wallet.Refunds == 0,
            "POST-DEBIT FAILURE: goods=0, balance=90, refunds=0 (no recovery in executor)"
        );

        wallet = new FakeWallet();
        try
        {
            await wallet.ExecutePurchaseAsync<int>(
                requests,
                operation,
                _ => throw new InvalidOperationException("injected failure before any delivery"),
                NullLogger.Instance,
                CancellationToken.None
            );
        }
        catch (InvalidOperationException error)
            when (error.Message.StartsWith("injected ", StringComparison.Ordinal))
        {
            Console.WriteLine("Expected injected failure: " + error.Message);
        }
        Check(
            wallet.Balance == 100 && wallet.Refunds == 1,
            "CONTROL: failure before delivery refunds the debit once"
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("Audited behavior changed: " + message);
        Console.WriteLine(message);
    }
}

internal sealed class FakeWallet : IPlayerWalletGrain
{
    public int Balance = 100;
    public int Refunds;
    public bool FailAfterDebit;

    public Task<WalletDebitResult> TryDebitAsync(
        List<WalletDebitRequest> requests,
        CommerceOperationId id,
        CancellationToken ct
    )
    {
        Balance -= 10;
        if (FailAfterDebit)
            throw new InvalidOperationException("injected post-commit notification failure");
        return Task.FromResult(WalletDebitResult.Success());
    }

    public Task CreditBackAsync(
        List<WalletDebitRequest> requests,
        CommerceOperationId id,
        CancellationToken ct
    )
    {
        Balance += 10;
        Refunds++;
        return Task.CompletedTask;
    }
}

namespace Vortex.Primitives.Players.Wallet
{
    public sealed class WalletDebitRequest { }

    public sealed class WalletDebitFailure { }

    public sealed record WalletDebitResult(bool Succeeded, WalletDebitFailure? Failure)
    {
        public static WalletDebitResult Success() => new(true, null);
    }
}

namespace Vortex.Primitives.Commerce
{
    public readonly record struct CommerceOperationId(Guid Value)
    {
        public bool IsNone => Value == Guid.Empty;
        public static CommerceOperationId None => new(Guid.Empty);
    }

    public enum CommerceOperationState
    {
        FailedBeforePivot,
    }

    public static class CommerceStepKeys
    {
        public const string REFUND = "refund";
    }

    public interface ICommerceJournal
    {
        Task TransitionAsync(
            CommerceOperationId id,
            CommerceOperationState state,
            string? step,
            string? error,
            CancellationToken ct
        );
    }
}

namespace Vortex.Primitives.Players.Grains
{
    public interface IPlayerWalletGrain
    {
        Task<WalletDebitResult> TryDebitAsync(
            List<WalletDebitRequest> requests,
            CommerceOperationId id,
            CancellationToken ct
        );
        Task CreditBackAsync(
            List<WalletDebitRequest> requests,
            CommerceOperationId id,
            CancellationToken ct
        );
    }
}
