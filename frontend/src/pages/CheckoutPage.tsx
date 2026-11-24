import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { PaymentForm } from '../components/PaymentForm';
import { ApiError, apiRequest, describeError, isUnauthorized } from '../lib/api';
import { formatAmount, phaseFromOrder, pollOrder } from '../lib/order';
import type {
  AddAddressRequest,
  AddressResponse,
  BasketResponse,
  CheckoutPhase,
  DeliveryMethodResponse,
  OrderResponse,
  ProductSummary,
} from '../types/api';

const EMPTY_ADDRESS: AddAddressRequest = {
  firstName: '',
  lastName: '',
  street: '',
  city: '',
  state: '',
  zipCode: '',
  country: '',
  isDefault: true,
};

const PHASE_LABELS: Record<CheckoutPhase, string> = {
  idle: 'Ready to check out',
  'creating-order': 'Creating order…',
  'awaiting-card': 'Waiting for card details',
  processing: 'Processing payment…',
  redirecting: 'Redirecting for 3DS verification…',
  verifying: 'Verifying payment with the server…',
  paid: 'Paid',
  failed: 'Payment failed',
  cancelled: 'Order cancelled',
};

export default function CheckoutPage() {
  const navigate = useNavigate();

  const [basket, setBasket] = useState<BasketResponse | null>(null);
  const [address, setAddress] = useState<AddressResponse | null>(null);
  const [methods, setMethods] = useState<DeliveryMethodResponse[]>([]);
  const [selectedMethodId, setSelectedMethodId] = useState('');
  const [products, setProducts] = useState<ProductSummary[]>([]);
  const [addressForm, setAddressForm] = useState<AddAddressRequest>(EMPTY_ADDRESS);
  const [showAddressForm, setShowAddressForm] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyAction, setBusyAction] = useState(false);

  const [phase, setPhase] = useState<CheckoutPhase>('idle');
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [clientSecret, setClientSecret] = useState<string | null>(null);

  // One key per logical checkout attempt. It is reused if create-order fails
  // before an order exists, and replaced only after an order was created.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());

  const loadCheckoutData = useCallback(async (signal: AbortSignal) => {
    setLoading(true);
    setLoadError(null);

    try {
      const [basketResult, addressResult, methodResult] = await Promise.all([
        apiRequest<BasketResponse>('/api/v1/Basket', { signal }),
        apiRequest<AddressResponse>('/api/v1/Users/addresses/default', { signal }).catch(
          (err: unknown) => {
            if (err instanceof ApiError && err.status === 404) return null;
            throw err;
          },
        ),
        apiRequest<DeliveryMethodResponse[]>('/api/v1/DeliveryMethods/active', { signal }),
      ]);

      setBasket(basketResult);
      setAddress(addressResult);
      setMethods(methodResult);
      setSelectedMethodId((current) => current || methodResult[0]?.id || '');
    } catch (err) {
      if (signal.aborted) return;
      setLoadError(describeError(err));
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout', { replace: true });
    } finally {
      if (!signal.aborted) setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    const controller = new AbortController();
    void loadCheckoutData(controller.signal);
    return () => controller.abort();
  }, [loadCheckoutData]);

  // Small helper so an empty basket can still be tested end-to-end using the
  // existing products and basket endpoints. No prices are computed here.
  useEffect(() => {
    if (loading || (basket && basket.totalItems > 0)) return;
    const controller = new AbortController();
    void apiRequest<ProductSummary[]>('/api/v1/Products', { signal: controller.signal })
      .then(setProducts)
      .catch(() => setProducts([]));
    return () => controller.abort();
  }, [loading, basket]);

  async function handleAddAddress(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusyAction(true);
    setError(null);
    try {
      const created = await apiRequest<AddressResponse>('/api/v1/Users/addresses', {
        method: 'POST',
        body: addressForm,
      });
      setAddress(created);
      setShowAddressForm(false);
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout', { replace: true });
    } finally {
      setBusyAction(false);
    }
  }

  async function handleAddToBasket(productId: string) {
    setBusyAction(true);
    setError(null);
    try {
      const updated = await apiRequest<BasketResponse>('/api/v1/Basket/items', {
        method: 'POST',
        body: { productId, quantity: 1 },
      });
      setBasket(updated);
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout', { replace: true });
    } finally {
      setBusyAction(false);
    }
  }

  async function handleCreateOrder() {
    if (!address || !selectedMethodId || !basket || basket.totalItems === 0) return;

    setBusyAction(true);
    setError(null);
    setPhase('creating-order');

    try {
      const created = await apiRequest<OrderResponse>('/api/v1/Orders', {
        method: 'POST',
        headers: { 'Idempotency-Key': idempotencyKey },
        body: { addressId: address.id, deliveryMethodId: selectedMethodId },
      });

      setOrder(created);
      setClientSecret(created.clientSecret);
      setPhase(created.clientSecret ? 'awaiting-card' : 'verifying');
      // The order now exists: the next logical attempt must use a fresh key.
      setIdempotencyKey(crypto.randomUUID());
    } catch (err) {
      setError(describeError(err));
      setPhase('failed');
      // Keep the same key: the order may have been created before the failure.
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout', { replace: true });
    } finally {
      setBusyAction(false);
    }
  }

  async function verifyOrder(orderId: string) {
    setPhase('verifying');
    setError(null);
    try {
      const result = await pollOrder(orderId);
      setOrder(result);
      setPhase(phaseFromOrder(result));
    } catch (err) {
      setError(describeError(err));
      setPhase('failed');
      if (isUnauthorized(err)) navigate(`/login?returnTo=${encodeURIComponent('/checkout/return?orderId=' + orderId)}`, { replace: true });
    }
  }

  async function handleRetryPayment() {
    if (!order) return;
    setBusyAction(true);
    setError(null);
    try {
      const retried = await apiRequest<OrderResponse>(`/api/v1/Orders/${order.id}/retry-payment`, {
        method: 'POST',
      });
      setOrder(retried);
      setClientSecret(retried.clientSecret);
      setPhase(retried.clientSecret ? 'awaiting-card' : 'verifying');
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout', { replace: true });
    } finally {
      setBusyAction(false);
    }
  }

  async function handleCancelOrder() {
    if (!order) return;
    setBusyAction(true);
    setError(null);
    try {
      const cancelled = await apiRequest<OrderResponse>(`/api/v1/Orders/${order.id}/cancel`, {
        method: 'POST',
      });
      setOrder(cancelled);
      setClientSecret(null);
      setPhase('cancelled');
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) navigate('/login?returnTo=%2Fcheckout', { replace: true });
    } finally {
      setBusyAction(false);
    }
  }

  const basketIsValid = Boolean(basket && basket.totalItems > 0 && !basket.hasUnavailableItems);
  const canCreateOrder = basketIsValid && Boolean(address) && Boolean(selectedMethodId);

  return (
    <section className="card">
      <h1>Checkout</h1>

      <p className="status" data-phase={phase} aria-live="polite">
        <strong>Status:</strong> {PHASE_LABELS[phase]}
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
          <p>
            Total (from API, currency set by backend): <strong>{formatAmount(order.total)}</strong>
          </p>

          {phase === 'awaiting-card' && clientSecret && (
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
                  void verifyOrder(order.id);
                }
              }}
            />
          )}

          {(phase === 'verifying' || phase === 'processing' || phase === 'redirecting') && (
            <p className="hint">
              Do not close this page. The webhook is the source of truth; the backend order is
              being checked.
            </p>
          )}

          {phase === 'paid' && (
            <p className="success" role="status">
              Payment confirmed by the backend. Thank you.
            </p>
          )}

          {phase === 'failed' && (
            <div className="actions">
              <button type="button" onClick={() => void handleRetryPayment()} disabled={busyAction}>
                Retry payment
              </button>
              <button
                type="button"
                className="secondary"
                onClick={() => void handleCancelOrder()}
                disabled={busyAction}
              >
                Cancel order
              </button>
            </div>
          )}

          {phase === 'cancelled' && (
            <p role="status">This order was cancelled and no payment will be taken.</p>
          )}
        </div>
      )}

      {!order && loading && <p aria-live="polite">Loading checkout data…</p>}

      {!order && loadError && (
        <div>
          <p role="alert" className="error">
            {loadError}
          </p>
          <button type="button" onClick={() => window.location.reload()}>
            Try again
          </button>
        </div>
      )}

      {!order && !loading && !loadError && (
        <>
          <div className="panel">
            <h2>1. Basket</h2>
            {basketIsValid ? (
              <>
                <p>{basket!.totalItems} item(s) ready.</p>
                <ul>
                  {basket!.items.map((item) => (
                    <li key={item.productId}>
                      {item.productName} × {item.quantity} — {formatAmount(item.lineTotal)}
                      {!item.canCheckout && <strong> (not available for checkout)</strong>}
                    </li>
                  ))}
                </ul>
                <p className="hint">
                  Basket subtotal (from API): {formatAmount(basket!.subtotal)}. The final total is
                  calculated by the backend when the order is created.
                </p>
              </>
            ) : (
              <div>
                <p>Your basket is empty or contains unavailable items.</p>
                {products.length > 0 && (
                  <ul className="product-list">
                    {products.map((product) => (
                      <li key={product.id}>
                        <span>{product.name} — {formatAmount(product.price)}</span>
                        <button
                          type="button"
                          onClick={() => void handleAddToBasket(product.id)}
                          disabled={busyAction}
                        >
                          Add to basket
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )}
          </div>

          <div className="panel">
            <h2>2. Shipping address</h2>
            {address && !showAddressForm ? (
              <>
                <address>
                  {address.firstName} {address.lastName}
                  <br />
                  {address.street}
                  <br />
                  {address.city}, {address.state} {address.zipCode}
                  <br />
                  {address.country}
                </address>
                <button type="button" className="secondary" onClick={() => setShowAddressForm(true)}>
                  Use a different address
                </button>
              </>
            ) : (
              <form onSubmit={(event) => void handleAddAddress(event)} className="stack">
                <fieldset>
                  <legend>Add a new address</legend>
                  <div className="grid">
                    <div className="field">
                      <label htmlFor="firstName">First name</label>
                      <input id="firstName" required maxLength={100} value={addressForm.firstName}
                        onChange={(e) => setAddressForm({ ...addressForm, firstName: e.target.value })} />
                    </div>
                    <div className="field">
                      <label htmlFor="lastName">Last name</label>
                      <input id="lastName" required maxLength={100} value={addressForm.lastName}
                        onChange={(e) => setAddressForm({ ...addressForm, lastName: e.target.value })} />
                    </div>
                    <div className="field wide">
                      <label htmlFor="street">Street</label>
                      <input id="street" required maxLength={256} value={addressForm.street}
                        onChange={(e) => setAddressForm({ ...addressForm, street: e.target.value })} />
                    </div>
                    <div className="field">
                      <label htmlFor="city">City</label>
                      <input id="city" required maxLength={100} value={addressForm.city}
                        onChange={(e) => setAddressForm({ ...addressForm, city: e.target.value })} />
                    </div>
                    <div className="field">
                      <label htmlFor="state">State</label>
                      <input id="state" required maxLength={100} value={addressForm.state}
                        onChange={(e) => setAddressForm({ ...addressForm, state: e.target.value })} />
                    </div>
                    <div className="field">
                      <label htmlFor="zipCode">Postal code</label>
                      <input id="zipCode" required maxLength={20} value={addressForm.zipCode}
                        onChange={(e) => setAddressForm({ ...addressForm, zipCode: e.target.value })} />
                    </div>
                    <div className="field">
                      <label htmlFor="country">Country</label>
                      <input id="country" required maxLength={100} value={addressForm.country}
                        onChange={(e) => setAddressForm({ ...addressForm, country: e.target.value })} />
                    </div>
                  </div>
                </fieldset>
                <div className="actions">
                  <button type="submit" disabled={busyAction}>Save address</button>
                  {address && (
                    <button type="button" className="secondary" onClick={() => setShowAddressForm(false)}>
                      Cancel
                    </button>
                  )}
                </div>
              </form>
            )}
          </div>

          <div className="panel">
            <h2>3. Delivery method</h2>
            {methods.length === 0 ? (
              <p>No active delivery methods are configured.</p>
            ) : (
              <fieldset>
                <legend>Choose a delivery method</legend>
                {methods.map((method) => (
                  <div className="radio-row" key={method.id}>
                    <input
                      type="radio"
                      id={`delivery-${method.id}`}
                      name="deliveryMethod"
                      value={method.id}
                      checked={selectedMethodId === method.id}
                      onChange={() => setSelectedMethodId(method.id)}
                    />
                    <label htmlFor={`delivery-${method.id}`}>
                      {method.name} — {formatAmount(method.price)}
                      {method.description ? ` (${method.description})` : ''}
                    </label>
                  </div>
                ))}
              </fieldset>
            )}
          </div>

          <button
            type="button"
            onClick={() => void handleCreateOrder()}
            disabled={!canCreateOrder || busyAction || phase === 'creating-order'}
          >
            {phase === 'creating-order' ? 'Creating order…' : 'Create order and pay'}
          </button>
        </>
      )}
    </section>
  );
}
