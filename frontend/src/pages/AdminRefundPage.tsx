import { useState, type FormEvent } from 'react';
import { apiRequest, describeError, isUnauthorized } from '../lib/api';
import { formatAmount } from '../lib/order';
import type { OrderResponse } from '../types/api';

export default function AdminRefundPage() {
  const [orderId, setOrderId] = useState('');
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function loadOrder() {
    if (!orderId.trim()) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      setOrder(await apiRequest<OrderResponse>(`/api/v1/admin/orders/${orderId.trim()}`));
    } catch (err) {
      setOrder(null);
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }

  async function refund(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!orderId.trim()) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const updated = await apiRequest<OrderResponse>(
        `/api/v1/admin/orders/${orderId.trim()}/refund`,
        { method: 'POST' },
      );
      setOrder(updated);
      setNotice('Refund requested. The backend webhook confirms the final refund status.');
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) setNotice('Sign in with an Admin account to use this page.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card">
      <h1>Admin refund</h1>
      <p className="hint">Requires a JWT for a user in the Admin role. The frontend never calls the webhook.</p>

      <form onSubmit={(event) => void refund(event)} className="stack">
        <div className="field">
          <label htmlFor="orderId">Order ID</label>
          <input
            id="orderId"
            required
            value={orderId}
            onChange={(event) => setOrderId(event.target.value)}
            placeholder="00000000-0000-0000-0000-000000000000"
          />
        </div>
        <div className="actions">
          <button type="button" className="secondary" onClick={() => void loadOrder()} disabled={busy}>
            Load order
          </button>
          <button type="submit" disabled={busy || !orderId.trim()}>
            {busy ? 'Working…' : 'Request refund'}
          </button>
        </div>
      </form>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {notice && (
        <p role="status" className="success">
          {notice}
        </p>
      )}

      {order && (
        <div className="panel">
          <h2>Order {order.id}</h2>
          <p>
            Order status: <strong>{order.orderStatus}</strong> · Payment status:{' '}
            <strong>{order.paymentStatus}</strong>
          </p>
          <p>Total (from API): {formatAmount(order.total)}</p>
          <p>
            Refunded at: {order.refundedAtUtc ? new Date(order.refundedAtUtc).toLocaleString() : '—'}
          </p>
        </div>
      )}
    </section>
  );
}
