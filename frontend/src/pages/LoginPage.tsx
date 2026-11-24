import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { apiRequest, describeError, isUnauthorized } from '../lib/api';
import { setToken } from '../lib/auth';
import type { AuthResponse } from '../types/api';

export default function LoginPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [email, setEmail] = useState('demo.customer@example.com');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const returnTo = searchParams.get('returnTo') || '/checkout';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      const result = await apiRequest<AuthResponse>('/api/v1/Auth/login', {
        method: 'POST',
        body: { email: email.trim(), password },
        auth: false,
      });
      setToken(result.token);
      navigate(returnTo.startsWith('/') ? returnTo : '/checkout', { replace: true });
    } catch (err) {
      setError(describeError(err));
      if (isUnauthorized(err)) setPassword('');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card">
      <h1>Sign in</h1>
      <p className="hint">Use a demo customer for checkout or an Admin account for refunds.</p>
      <form onSubmit={handleSubmit} className="stack">
        <div className="field">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="password">Password</label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </div>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <button type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </section>
  );
}
