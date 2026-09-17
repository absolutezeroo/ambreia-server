<script lang="ts">
  // Every Relic that exists and whose hands it has passed through. Read-only on purpose: a Relic is
  // made by a player converting furni, and its serial number is the one thing about it that must
  // never be editable — that number is the whole reason the object is worth anything.
  import AssetImage from '../../components/AssetImage.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import EntityLink from '../../components/EntityLink.svelte';
  import { formatNumber, formatDate } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import { openPlayer } from '../../lib/session';
  import type { TabProps } from './shared';

  let { data }: TabProps = $props();
</script>

<section class="panel" style="margin-top: 12px;">
  <div class="panel-head"><h2>{$t('collectibles.relicsTitle')}</h2></div>
  <p class="muted">{$t('collectibles.relicsDescription')}</p>
  {#if (data.assets || []).length === 0}
    <EmptyState message={$t('collectibles.noRelics')} />
  {:else}
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>{$t('collectibles.colPlayer')}</th>
            <th>{$t('collectibles.colItem')}</th>
            <th>{$t('collectibles.colSerial')}</th>
            <th>{$t('collectibles.colStampPrice')}</th>
            <th>{$t('collectibles.colMintedAt')}</th>
            <th>{$t('collectibles.colHistory')}</th>
          </tr>
        </thead>
        <tbody>
          {#each data.assets as asset}
            <tr>
              <td>
                <EntityLink type="player" id={asset.playerId} label={asset.playerName} {openPlayer} />
              </td>
              <td>
                <span class="cell">
                  <AssetImage src={asset.iconUrl} alt={asset.productCode} size={32} />
                  <code>{asset.productCode}</code>
                </span>
              </td>
              <td>
                {#if asset.editionSize > 0}
                  #{formatNumber(asset.serialNumber)} / {formatNumber(asset.editionSize)}
                {:else}
                  #{formatNumber(asset.serialNumber)}
                {/if}
              </td>
              <td>{formatNumber(asset.stampCost)}</td>
              <td>{formatDate(asset.mintedAt)}</td>
              <td class="history">
                {#each asset.history as move}
                  <div>
                    <span class="status-badge">{move.reason}</span>
                    {move.fromPlayer ?? '—'} → {move.toPlayer}
                    <span class="muted">{formatDate(move.at)}</span>
                  </div>
                {/each}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</section>

<style>
  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }

  /* A Relic's whole history in one cell: short lines, and the oldest at the bottom, so the most
     recent hand it passed through is the one read first. */
  .history {
    font-size: 0.85em;
    line-height: 1.5;
  }
</style>
