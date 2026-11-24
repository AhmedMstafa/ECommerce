import { Link, Navigate, Route, Routes, useNavigate } from 'react-router-dom';
import { RequireAuth } from './components/RequireAuth';
import { clearToken, getToken } from './lib/auth';
import AdminRefundPage from './pages/AdminRefundPage';
import CheckoutPage from './pages/CheckoutPage';
import LoginPage from './pages/LoginPage';
import ReturnPage from './pages/ReturnPage';

export default function App() {
  const navigate = useNavigate();
  const signedIn = Boolean(getToken());

  return (
    <>
      <header className="site-header">
        <nav aria-label="Main navigation">
          <Link to="/checkout">Checkout</Link>
          <Link to="/admin/refund">Admin refund</Link>
          {signedIn ? (
            <button
              type="button"
              className="link-button"
              onClick={() => {
                clearToken();
                navigate('/login', { replace: true });
              }}
            >
              Sign out
            </button>
          ) : (
            <Link to="/login">Sign in</Link>
          )}
        </nav>
      </header>

      <main>
        <Routes>
          <Route path="/" element={<Navigate to="/checkout" replace />} />
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/checkout"
            element={
              <RequireAuth>
                <CheckoutPage />
              </RequireAuth>
            }
          />
          <Route
            path="/checkout/return"
            element={
              <RequireAuth>
                <ReturnPage />
              </RequireAuth>
            }
          />
          <Route
            path="/admin/refund"
            element={
              <RequireAuth>
                <AdminRefundPage />
              </RequireAuth>
            }
          />
          <Route path="*" element={<Navigate to="/checkout" replace />} />
        </Routes>
      </main>
    </>
  );
}
