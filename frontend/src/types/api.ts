/** Derived from src/ECommerce.API/Models/ApiResponse.cs and the live Swagger document. */
export interface ApiResponse<T> {
  statusCode: number;
  success: boolean;
  message: string | null;
  data: T;
  meta?: unknown;
}

export interface AuthResponse {
  token: string;
  email: string;
  fullName: string;
  expiresAt: string;
  refreshToken?: string;
  refreshTokenExpiresAt?: string;
}

export interface AddressResponse {
  id: string;
  firstName: string;
  lastName: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  country: string;
  isDefault: boolean;
}

export interface AddAddressRequest {
  firstName: string;
  lastName: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  country: string;
  isDefault: boolean;
}

export interface DeliveryMethodResponse {
  id: string;
  name: string;
  description: string | null;
  price: number;
  isActive: boolean;
}

export interface BasketItemResponse {
  productId: string;
  productName: string;
  pictureUrl: string;
  quantity: number;
  priceAtAdd: number | null;
  currentUnitPrice: number;
  hasPriceChanged: boolean;
  priceDifference: number;
  lineTotal: number;
  availableQuantity: number;
  status: string;
  canCheckout: boolean;
}

export interface BasketResponse {
  id: string;
  userId: string | null;
  items: BasketItemResponse[];
  totalItems: number;
  subtotal: number;
  hasUnavailableItems: boolean;
  hasPriceChanges: boolean;
}

export interface ProductSummary {
  id: string;
  name: string;
  description: string;
  pictureUrl: string;
  price: number;
  productBrand: string;
  productType: string;
}

export interface OrderItemResponse {
  productId: string;
  productName: string;
  pictureUrl: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface OrderShippingAddressResponse {
  firstName: string;
  lastName: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  country: string;
}

export interface OrderResponse {
  id: string;
  buyerId: string;
  orderStatus: string;
  paymentStatus: string;
  subtotal: number;
  deliveryFee: number;
  total: number;
  deliveryMethodName: string;
  deliveryMethodPrice: number;
  shippingAddress: OrderShippingAddressResponse;
  items: OrderItemResponse[];
  paymentIntentId: string | null;
  /** Only present on create/retry. Never persist or log this value. */
  clientSecret: string | null;
  requiresReconciliation: boolean;
  reconciliationReason: string | null;
  createdAtUtc: string;
  paidAtUtc: string | null;
  cancelledAtUtc: string | null;
  refundedAtUtc: string | null;
}

export type CheckoutPhase =
  | 'idle'
  | 'creating-order'
  | 'awaiting-card'
  | 'processing'
  | 'redirecting'
  | 'verifying'
  | 'paid'
  | 'failed'
  | 'cancelled';
