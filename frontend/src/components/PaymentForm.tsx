import { Elements, PaymentElement, useElements, useStripe } from '@stripe/react-stripe-js';
import { useState, type FormEvent } from 'react';
import { stripeConfigError, stripePromise } from '../lib/stripe';

interface PaymentFormProps {
  clientSecret: string;
  orderId: string;
  onProcessing: () => void;
  onRedirecting: () => void;
  onComplete: (errorMessage: string | null) => void;
}

export function PaymentForm({ clientSecret, orderId, onProcessing, onRedirecting, onComplete }: PaymentFormProps) {
  if (stripeConfigError || !stripePromise) {
    return <p role="alert">{stripeConfigError ?? 'Stripe.js could not be loaded.'}</p>;
  }

  return (
    <Elements stripe={stripePromise} options={{ clientSecret }} key={clientSecret}>
      <PaymentFormInner
        clientSecret={clientSecret}
        orderId={orderId}
        onProcessing={onProcessing}
        onRedirecting={onRedirecting}
        onComplete={onComplete}
      />
    </Elements>
  );
}

function PaymentFormInner({
  clientSecret,
  orderId,
  onProcessing,
  onRedirecting,
  onComplete,
}: PaymentFormProps) {
  const stripe = useStripe();
  const elements = useElements();
  const [busy, setBusy] = useState(false);
  const [elementReady, setElementReady] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!stripe || !elements || !elementReady || busy) return;

    // Stripe.js requires the Payment Element to be submitted (validated)
    // before confirmPayment. Do this immediately on the pay press, before
    // any other asynchronous work or parent phase changes.
    const { error: submitError } = await elements.submit();
    if (submitError) {
      const message = submitError.message ?? 'Please check your payment details.';
      setLocalError(message);
      onComplete(message);
      return;
    }

    setBusy(true);
    setLocalError(null);
    // The parent must keep this component mounted while the phase is
    // "processing" or "redirecting"; confirmPayment needs a live Payment
    // Element for the whole call.
    onProcessing();

    const returnUrl = `${window.location.origin}/checkout/return?orderId=${encodeURIComponent(orderId)}`;

    try {
      const result = await stripe.confirmPayment({
        elements,
        clientSecret,
        confirmParams: { return_url: returnUrl },
        redirect: 'if_required',
      });

      if (result.error) {
        const message = result.error.message ?? 'The payment was not confirmed.';
        setLocalError(message);
        onComplete(message);
        return;
      }

      // A 3DS challenge navigates away; when it does not, we still verify the
      // authoritative backend order status instead of trusting this result.
      onRedirecting();
      onComplete(null);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Payment could not be confirmed.';
      setLocalError(message);
      onComplete(message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="payment-form" aria-busy={busy}>
      <h3>Payment details</h3>
      <p className="hint">Card entry and 3DS are handled securely by Stripe.</p>
      <PaymentElement onReady={() => setElementReady(true)} />
      {localError && (
        <p role="alert" className="error">
          {localError}
        </p>
      )}
      <button type="submit" disabled={!stripe || !elements || !elementReady || busy}>
        {busy ? 'Confirming…' : elementReady ? 'Pay now' : 'Preparing payment…'}
      </button>
    </form>
  );
}
