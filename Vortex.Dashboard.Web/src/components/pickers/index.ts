// Row layouts, by the name a directory asks for. Four, not seventeen: most directories differ only
// in where their rows come from, and a shared layout is why adding one is a single line.
import FigurePickerRow from './FigurePickerRow.svelte';
import FurniturePickerRow from './FurniturePickerRow.svelte';
import PetPickerRow from './PetPickerRow.svelte';
import PlainPickerRow from './PlainPickerRow.svelte';
import PlayerPickerRow from './PlayerPickerRow.svelte';
import RoomPickerRow from './RoomPickerRow.svelte';
import type { Component } from 'svelte';
import type { PickerRow } from '../../lib/pickers/directories';

/** What every row layout takes: the row, and how to say it was chosen. */
export type PickerRowProps = { row: PickerRow; onchoose: (row: PickerRow) => void };

export const PICKER_ROWS: Record<string, Component<PickerRowProps>> = {
  furniture: FurniturePickerRow,
  room: RoomPickerRow,
  player: PlayerPickerRow,
  plain: PlainPickerRow,
  figureSet: FigurePickerRow,
  pet: PetPickerRow,
};
