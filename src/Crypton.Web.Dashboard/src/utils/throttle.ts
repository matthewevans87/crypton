interface ThrottleOptions {
  limit: number;
  windowMs: number;
}

export function createThrottle({ limit, windowMs }: ThrottleOptions) {
  let lastCall = 0;
  let callCount = 0;
  let windowStart = Date.now();

  return function throttle<T extends (...args: unknown[]) => void>(fn: T): T {
    const now = Date.now();
    const timeSinceLastCall = now - lastCall;

    if (timeSinceLastCall >= windowMs) {
      windowStart = now;
      callCount = 0;
    }

    return ((...args: unknown[]) => {
      const nowInner = Date.now();
      
      if (nowInner - windowStart >= windowMs) {
        windowStart = nowInner;
        callCount = 0;
      }

      if (callCount < limit) {
        callCount++;
        lastCall = nowInner;
        fn(...args);
      }
    }) as T;
  };
}

export function debounce<T extends (...args: unknown[]) => void>(
  fn: T,
  delayMs: number
): T {
  let timeoutId: ReturnType<typeof setTimeout> | null = null;

  return ((...args: unknown[]) => {
    if (timeoutId) {
      clearTimeout(timeoutId);
    }
    timeoutId = setTimeout(() => {
      fn(...args);
    }, delayMs);
  }) as T;
}

export function batchUpdates<T>(
  callback: (items: T[]) => void,
  windowMs: number = 16
) {
  let batch: T[] = [];
  let scheduled = false;

  return (item: T) => {
    batch.push(item);

    if (!scheduled) {
      scheduled = true;
      requestAnimationFrame(() => {
        callback([...batch]);
        batch = [];
        scheduled = false;
      });
    }
  };
}
