import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { PaymentForm } from '../components/PaymentForm';
import { apiRequest, describeError, isUnauthorized } from '../lib/api';
import { formatAmount, phaseFromOrder, pollOrder } from '../lib/order';
import type { CheckoutPhase, OrderResponse } from '../types/api';

export default function ReturnPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const orderId = searchParams.get('orderId') ?? '';

  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [phase, setPhase] = useState<CheckoutPhase>('verifying');
  const [error, setError] = useState<string | null>(null);
  const [clientSecret, setClientSecret] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!orderId) {
      setError('The return link is missing the orderId parameter.');
      setPhase('failed');
      return;
    }

    const controller = new AbortController();
    void (async () => {
      try {
        const result = await pollOrder(orderId, controller.signal);
        if (controller.signal.aborted) return;
        setOrder(result);
        setPhase(phaseFromOrder(result));
      } catch (err) {
        if (controller.signal.aborted) return;
        setError(describeError(err));
        setPhase('failed');
        if (isUnauthorized(err)) {
          navigate(
            `/login?returnTo=${encodeURIComponent(`/checkout/return?orderId=${orderId}`)}`,
            { replace: true },
          );
        }
      }
    })();

    return () => controller.abort();
  }, [orderId, navigate]);

  async function verifyAgain() {
    if (!orderId) return;
    setPhase('verifying');
    setError(null);
    try {
      const result = await pollOrder(orderId);
      setOrder(result);
      setPhase(phaseFromOrder(result));
    } catch (err) {
      setError(describeError(err));
      setPhase('failed');
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout%2Freturn', { replace: true });
    }
  }

  async function retryPayment() {
    if (!orderId) return;
    setBusy(true);
    setError(null);
    try {
      const retried = await apiRequest<OrderResponse>(`/api/v1/Orders/${orderId}/retry-payment`, {
        method: 'POST',
      });
      setOrder(retried);
      setClientSecret(retried.clientSecret);
      setPhase(retried.clientSecret ? 'awaiting-card' : 'verifying');
    } catch (err) {
      setError(describeError(err));
      setPhase('failed');
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout%2Freturn', { replace: true });
    } finally {
      setBusy(false);
    }
  }

  async function cancelOrder() {
    if (!orderId) return;
    setBusy(true);
    setError(null);
    try {
      const cancelled = await apiRequest<OrderResponse>(`/api/v1/Orders/${orderId}/cancel`, {
        method: 'POST',
      });
      setOrder(cancelled);
      setClientSecret(null);
      setPhase('cancelled');
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout%2Freturn', { replace: true });
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card">
      <h1>Payment return</h1>
      <p className="status" data-phase={phase} aria-live="polite">
        <strong>Status:</strong> {phase === 'paid' ? 'Paid' : phase === 'failed' ? 'Payment failed' : phase === 'cancelled' ? 'Order cancelled' : 'Verifying payment with the server…'}
      </p>

      {error && (
        <p role="alert" className="error">
          {error}
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
          {order.requiresReconciliation && (
            <p className="hint">This order requires reconciliation by the backend.</p>
          )}
        </div>
      )}

      {(phase === 'awaiting-card' || phase === 'processing' || phase === 'redirecting') && clientSecret && order && (
        <PaymentForm
          clientSecret={clientSecret}
          orderId={order.id}
          onProcessing={() => setPhase('processing')}
          onRedirecting={() => setPhase('redirecting')}
          onComplete={(message) => {
            if (message) {
              setError(message);
              setPhase('failed');
            } else {
              void verifyAgain();
            }
          }}
        />
      )}

      {(phase === 'verifying' || phase === 'processing' || phase === 'redirecting') && (
        <p className="hint">
          Stripe redirects first; the webhook updates the order. We are polling the backend with
          backoff before showing a final state.
        </p>
      )}

      {phase === 'paid' && (
        <p className="success" role="status">
          The backend confirms this order is paid.
        </p>
      )}

      <div className="actions">
        {phase !== 'paid' && (
          <button type="button" onClick={() => void verifyAgain()}>
            Check status again
          </button>
        )}
        {phase === 'failed' && (
          <button type="button" onClick={() => void retryPayment()} disabled={busy}>
            Retry payment
          </button>
        )}
        {phase !== 'paid' && phase !== 'cancelled' && (
          <button type="button" className="secondary" onClick={() => void cancelOrder()} disabled={busy}>
            Cancel order
          </button>
        )}
        <Link to="/checkout">Back to checkout</Link>
      </div>
    </section>
  );
}
