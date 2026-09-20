<script lang="ts">
  import type { PickerRow } from '../../lib/pickers/directories';
  import AssetImage from '../AssetImage.svelte';
  import { Shirt } from '@lucide/svelte';
  import { t } from '../../lib/i18n';

  // A garment, which figuredata never names: the picture carries the recognition and the text only
  // says which part type and which number, so the value saved can be checked against the filter.
  //
  // The imaging server crops each one to its own bounds -- a shoe comes back 25x14, a hat 33x32 --
  // so the shared tile's object-fit does the framing and there is nothing here to position.
  type Props = { row: PickerRow; onchoose: (row: PickerRow) => void };

  let { row, onchoose }: Props = $props();

  let typeLabel = $derived.by(() => {
    const key = `pickerModal.partType_${row.type}`;
    const translated = $t(key);

    return translated === key ? String(row.type ?? '') : translated;
  });
</script>

<button type="button" class="pick-row" onclick={() => onchoose(row)}>
  <AssetImage src={row.imageUrl as string | undefined} alt="" size={38} fallbackIcon={Shirt} />
  <span class="pick-main">
    <strong>{typeLabel}</strong>
    <small>
      #{row.id}{row.gender && row.gender !== 'U' ? ` - ${row.gender}` : ''}{row.club
        ? ` - ${$t('pickerModal.clubOnly')}`
        : ''}
    </small>
  </span>
</button>
