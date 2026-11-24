import { loadStripe, type Stripe } from '@stripe/stripe-js';

const publishableKey = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY;

export const stripeConfigError = publishableKey
  ? null
  : 'VITE_STRIPE_PUBLISHABLE_KEY is missing. Add it to frontend/.env.';

/** Only the publishable key is ever sent to the browser. */
export const stripePromise: Promise<Stripe | null> | null = publishableKey
  ? loadStripe(publishableKey)
  : null;
