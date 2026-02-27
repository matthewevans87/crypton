import { create } from 'zustand';

export interface AppError {
  id: string;
  message: string;
  status?: number;
  type: 'error' | 'warning' | 'info';
  timestamp: number;
  retryable: boolean;
  retry?: () => void;
}

interface ErrorState {
  errors: AppError[];
  addError: (error: Omit<AppError, 'id' | 'timestamp'>) => void;
  removeError: (id: string) => void;
  clearErrors: () => void;
}

export const useErrorStore = create<ErrorState>((set) => ({
  errors: [],
  
  addError: (error) => set((state) => {
    const newError: AppError = {
      ...error,
      id: `error-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`,
      timestamp: Date.now(),
    };
    
    const newErrors = [newError, ...state.errors].slice(0, 3);
    return { errors: newErrors };
  }),
  
  removeError: (id) => set((state) => ({
    errors: state.errors.filter(e => e.id !== id),
  })),
  
  clearErrors: () => set({ errors: [] }),
}));

export function getErrorMessage(status?: number, isRetryable?: boolean): string {
  if (!status) {
    return 'Unable to connect. Check your connection.';
  }
  
  switch (status) {
    case 400:
      return 'Invalid request. Please try again.';
    case 401:
      return 'Session expired. Please refresh.';
    case 403:
      return 'Access denied.';
    case 404:
      return 'Data not found.';
    case 408:
      return 'Request timed out. Retrying...';
    case 429:
      return 'Too many requests. Please wait.';
    case 500:
      return 'Server error. Retrying...';
    case 502:
    case 503:
      return 'Service unavailable. Retrying...';
    case 504:
      return 'Gateway timeout. Retrying...';
    default:
      if (isRetryable) {
        return 'Connection error. Retrying...';
      }
      return `Error${status ? `: ${status}` : ''}`;
  }
}
