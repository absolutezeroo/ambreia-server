using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Dashboard.API.Operations.Hotel;

/// <summary>
/// Staff's edits to the hotel-wide word list.
/// </summary>
/// <remarks>
/// Through the runner like every other operator write: the list decides what every player may
/// write, so who changed it, when and why has to be in the journal. An outcome other than
/// <see cref="WordFilterAdminOutcome.Done"/> is thrown rather than returned so the runner records
/// the attempt and its refusal, and the page shows the code (<c>wordfilter_duplicate</c>, …).
/// </remarks>
internal sealed class WordFilterOperations(OperationRunner runner, IWordFilterAdminService admin)
{
    private readonly OperationRunner _runner = runner;
    private readonly IWordFilterAdminService _admin = admin;

    public Task<OperationResult> AddAsync(
        AddWordFilterWordRequest body,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.wordfilter.word.add",
            actor,
            body.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                body.Word,
                body.Action,
                body.Match,
                body.Replacement,
            },
            work: async c =>
                Check(
                    await _admin
                        .AddAsync(body.Word, body.Action, body.Match, body.Replacement, actor, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateAsync(
        UpdateWordFilterWordRequest body,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.wordfilter.word.update",
            actor,
            body.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                body.Id,
                body.Word,
                body.Action,
                body.Match,
                body.Replacement,
            },
            work: async c =>
                Check(
                    await _admin
                        .UpdateAsync(
                            body.Id,
                            body.Word,
                            body.Action,
                            body.Match,
                            body.Replacement,
                            c
                        )
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> RemoveAsync(
        RemoveWordFilterWordRequest body,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.wordfilter.word.remove",
            actor,
            body.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { body.Id },
            work: async c => Check(await _admin.RemoveAsync(body.Id, c).ConfigureAwait(false)),
            ct
        );

    private static void Check(WordFilterAdminOutcome outcome)
    {
        if (outcome != WordFilterAdminOutcome.Done)
        {
            throw new InvalidOperationException(
                "wordfilter_" + outcome.ToString().ToLowerInvariant()
            );
        }
    }
}
