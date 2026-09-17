# Beta readiness audit — validation

Audited application revision: `04550328f8d696358d477cc835c7d86dffb2b8bc`.

The [report](../beta-readiness-audit-v2.md) separates static evidence, the local probe, CI and environment limits. [evidence.txt](evidence.txt) keeps excerpts of the outputs, failures included; it does not replace the full logs or a MySQL/Orleans test.

## Replaying the purchase probe

Prerequisites: Python 3, the .NET 10 SDK with its runtime and the ASP.NET Core reference pack, and a `dotnet` executable on the PATH. From the repository root:

```bash
python3 docs/audits/validation/run-wallet-purchase-probe.py
```

The launcher compiles the production file `Vortex.Primitives/Players/Wallet/WalletPurchaseExtensions.cs` together with the doubles defined in `wallet_purchase_probe.cs`. It calls the SDK compiler directly, with no NuGet restore, writes into a temporary directory and then deletes it. No server, player account, database or external service is involved.

The three assertions check that:

1. A simulated grant that completed, followed by an exception, still triggers a refund.
2. A simulated debit that committed, followed by an exception before it returns, triggers neither a grant nor any compensation by this executor.
3. A failure before delivery does trigger a compensation.

**A passing probe confirms the defects of the audited revision.** This is not a regression suite meant to stay green after the repair. When fixing, convert these scenarios into assertions of the intended invariants and extend them with the real SQL/Orleans owners, with failpoints before and after each commit.

The `goods` and `balance` messages are the states of the doubles. They must not be presented as a measurement of real loss on MySQL, or as a reproduced network exploit.
