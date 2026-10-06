// Transient notifications. The dashboard had none: every write reported itself inline, next to the
// form, which works while you are looking at the form and not at all when the write finishes after
// you have scrolled somewhere else.
//
//   import { toast } from '../lib/toasts';
//   toast.success('Offer created');
//   toast.error(describeApiError(err), { timeout: 0 });   // 0 = stays until dismissed
//
// The host lives once in AppShell; nothing else needs to render it.
import { writable } from 'svelte/store';

export type ToastKind = 'success' | 'info' | 'warning' | 'error';

export type Toast = { id: number; kind: ToastKind; message: string; title: string };

/** How long a toast stays. 0 holds it until dismissed. */
export type ToastOptions = { timeout?: number; title?: string };

export const toasts = writable<Toast[]>([]);

let nextId = 1;

function push(
  kind: ToastKind,
  message: string,
  options: ToastOptions = {},
): number | null {
  if (!message) return null;

  const id = nextId++;
  const timeout = options.timeout ?? (kind === 'error' ? 9000 : 5000);

  toasts.update((list) => [...list, { id, kind, message, title: options.title || '' }]);

  // 0 means "hold until dismissed" -- an error the operator has not read yet is not noise.
  if (timeout > 0) {
    setTimeout(() => dismiss(id), timeout);
  }

  return id;
}

export function dismiss(id: number): void {
  toasts.update((list) => list.filter((entry) => entry.id !== id));
}

export const toast = {
  success: (message: string, options?: ToastOptions) => push('success', message, options),
  info: (message: string, options?: ToastOptions) => push('info', message, options),
  warning: (message: string, options?: ToastOptions) => push('warning', message, options),
  error: (message: string, options?: ToastOptions) => push('error', message, options),
};
