import { useEffect } from 'react';
import { useErrorStore, type AppError } from '../store/errors';

function ErrorItem({ error, onDismiss }: { error: AppError; onDismiss: () => void }) {
  useEffect(() => {
    const timer = setTimeout(onDismiss, 5000);
    return () => clearTimeout(timer);
  }, [onDismiss]);

  const typeStyles = {
    error: 'bg-loss/20 border-loss text-loss',
    warning: 'bg-warning/20 border-warning text-warning',
    info: 'bg-info/20 border-info text-info',
  };

  const iconType = {
    error: '✕',
    warning: '⚠',
    info: 'ℹ',
  };

  return (
    <div
      className={`
        flex items-center gap-3 px-4 py-3 
        border-l-2 animate-slide-in
        ${typeStyles[error.type as keyof typeof typeStyles]}
      `}
    >
      <span className="text-sm font-mono">{iconType[error.type as keyof typeof iconType]}</span>
      <span className="flex-1 text-sm">{error.message}</span>
      {error.retryable && error.retry && (
        <button
          onClick={error.retry}
          className="text-xs underline hover:no-underline opacity-80 hover:opacity-100"
        >
          Retry
        </button>
      )}
      <button
        onClick={onDismiss}
        className="text-xs opacity-60 hover:opacity-100"
      >
        ✕
      </button>
    </div>
  );
}

export function ErrorToast() {
  const { errors, removeError } = useErrorStore();

  if (errors.length === 0) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 w-80">
      {errors.map((error: AppError) => (
        <ErrorItem
          key={error.id}
          error={error}
          onDismiss={() => removeError(error.id)}
        />
      ))}
    </div>
  );
}
