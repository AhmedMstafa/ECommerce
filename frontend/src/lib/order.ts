import { apiRequest } from './api';
import type { OrderResponse } from '../types/api';

const POLL_DELAYS_MS = [0, 1000, 2000, 4000, 8000];

const TERMINAL_PAYMENT_STATUSES = new Set([
  'Succeeded',
  'Failed',
  'Cancelled',
  'RefundPending',
  'Refunded',
]);

export function isTerminalOrder(order: OrderResponse): boolean {
  return (
    TERMINAL_PAYMENT_STATUSES.has(order.paymentStatus) ||
    order.orderStatus === 'Cancelled' ||
    order.refundedAtUtc !== null
  );
}

function wait(ms: number): Promise<void> {
  return new Promise((resolve) => {
    window.setTimeout(resolve, ms);
  });
}

/**
 * The webhook is the source of truth. Poll the backend order with backoff
 * instead of trusting the browser confirmPayment result.
 */
export async function pollOrder(
  orderId: string,
  signal?: AbortSignal,
): Promise<OrderResponse> {
  let last: OrderResponse | null = null;

  for (const delay of POLL_DELAYS_MS) {
    if (signal?.aborted) throw new DOMException('Aborted', 'AbortError');
    if (delay > 0) await wait(delay);

    last = await apiRequest<OrderResponse>(`/api/v1/Orders/my/${orderId}`, { signal });
    if (isTerminalOrder(last)) return last;
  }

  return last!;
}

export function phaseFromOrder(order: OrderResponse):
  | 'verifying'
  | 'paid'
  | 'failed'
  | 'cancelled' {
  if (order.paymentStatus === 'Succeeded') return 'paid';
  if (
    order.paymentStatus === 'Failed' ||
    order.paymentStatus === 'Cancelled' ||
    order.paymentStatus === 'Refunded'
  ) {
    return 'failed';
  }
  if (order.orderStatus === 'Cancelled' || order.paymentStatus === 'RefundPending') {
    return 'cancelled';
  }
  return 'verifying';
}

/** Display only. Amount and currency are owned by the backend PaymentIntent. */
export function formatAmount(value: number): string {
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}
