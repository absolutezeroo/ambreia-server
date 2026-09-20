<script lang="ts">
  import type { PickerRow } from '../../lib/pickers/directories';
  import AssetImage from '../AssetImage.svelte';
  import { PawPrint } from '@lucide/svelte';

  // A pet species. There is no name for one anywhere in the hotel -- no table, and none in the
  // client's texts -- so the number is the label and the picture is what tells two apart.
  type Props = { row: PickerRow; onchoose: (row: PickerRow) => void };

  let { row, onchoose }: Props = $props();
</script>

<button type="button" class="pick-row" onclick={() => onchoose(row)}>
  <AssetImage src={row.imageUrl as string | undefined} alt="" size={38} fallbackIcon={PawPrint} />
  <span class="pick-main">
    <strong>{row.name}</strong>
    <small>#{row.id}</small>
  </span>
</button>
