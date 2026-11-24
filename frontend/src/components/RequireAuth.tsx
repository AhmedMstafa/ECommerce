import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { getToken } from '../lib/auth';

export function RequireAuth({ children }: { children: ReactNode }) {
  const location = useLocation();

  if (!getToken()) {
    const returnTo = encodeURIComponent(`${location.pathname}${location.search}`);
    return <Navigate to={`/login?returnTo=${returnTo}`} replace />;
  }

  return <>{children}</>;
}
