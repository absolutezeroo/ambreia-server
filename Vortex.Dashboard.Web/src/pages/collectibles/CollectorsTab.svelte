<script lang="ts">
  // The standings. Read-only: a collector score is computed from what a player holds, so there is
  // nothing here an admin could set without the next recount undoing it.
  import EntityLink from '../../components/EntityLink.svelte';
  import { formatNumber } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import { openPlayer } from '../../lib/session';
  import type { TabProps } from './shared';

  let { data }: TabProps = $props();
</script>

<section class="panel" style="margin-top: 12px;">
  <div class="panel-head"><h2>{$t('collectibles.leaderboardTitle')}</h2></div>
  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>{$t('collectibles.colPlayer')}</th>
          <th>{$t('collectibles.colCollectorScore')}</th>
        </tr>
      </thead>
      <tbody>
        {#each data.topCollectors || [] as row}
          <tr>
            <td><EntityLink type="player" id={row.playerId} label={row.playerName} {openPlayer} /></td>
            <td>{formatNumber(row.score)}</td>
          </tr>
        {:else}
          <tr><td colspan="2" class="muted">{$t('collectibles.noCollectors')}</td></tr>
        {/each}
      </tbody>
    </table>
  </div>
</section>
